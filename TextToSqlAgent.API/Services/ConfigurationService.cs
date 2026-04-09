using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service for managing configuration validation and environment-specific settings
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Validates that all required configuration values are present
    /// </summary>
    /// <returns>Configuration validation result</returns>
    ConfigurationValidationResult ValidateConfiguration();

    /// <summary>
    /// Gets a configuration value with environment variable override support
    /// </summary>
    /// <param name="key">Configuration key (supports nested keys with ':')</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>Configuration value</returns>
    string? GetConfigurationValue(string key, string? defaultValue = null);

    /// <summary>
    /// Gets a secure configuration value (API keys, connection strings, etc.)
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <returns>Secure configuration value</returns>
    string? GetSecureValue(string key);

    /// <summary>
    /// Generates a secure random key for encryption/JWT purposes
    /// </summary>
    /// <param name="length">Key length in bytes</param>
    /// <returns>Base64 encoded secure key</returns>
    string GenerateSecureKey(int length = 32);
}

public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly IWebHostEnvironment _environment;

    public ConfigurationService(
        IConfiguration configuration,
        ILogger<ConfigurationService> logger,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    public ConfigurationValidationResult ValidateConfiguration()
    {
        var result = new ConfigurationValidationResult();
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate JWT configuration
        var jwtKey = GetSecureValue("Jwt:Key");
        if (string.IsNullOrEmpty(jwtKey))
        {
            errors.Add("JWT Key is not configured. Set JWT_SECRET environment variable or Jwt:Key in configuration.");
        }
        else if (jwtKey.Length < 32)
        {
            errors.Add("JWT Key must be at least 32 characters long for security.");
        }
        else if (jwtKey == "SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678" ||
                 jwtKey == "TextToSqlAgentDevKey2024Secure!ThisIsLongEnough32+")
        {
            if (_environment.IsProduction())
            {
                errors.Add("JWT Key is using a default development value in production environment.");
            }
            else
            {
                warnings.Add("JWT Key is using a default development value. Consider using a unique key.");
            }
        }

        // Validate Encryption Key
        var encryptionKey = GetSecureValue("Encryption:Key");
        if (string.IsNullOrEmpty(encryptionKey))
        {
            errors.Add("Encryption Key is not configured. Set ENCRYPTION_KEY environment variable or Encryption:Key in configuration.");
        }
        else if (encryptionKey.Length < 32)
        {
            errors.Add("Encryption Key must be at least 32 characters long for AES-256 encryption.");
        }

        // Validate LLM Provider configuration
        var llmProvider = _configuration["LLMProvider"] ?? "OpenAI";
        if (llmProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var openAIKey = GetSecureValue("OpenAI:ApiKey");
            if (string.IsNullOrEmpty(openAIKey))
            {
                errors.Add("OpenAI API Key is not configured. Set OPENAI_API_KEY environment variable or OpenAI:ApiKey in configuration.");
            }
            else if (!openAIKey.StartsWith("sk-"))
            {
                warnings.Add("OpenAI API Key format appears invalid (should start with 'sk-').");
            }
        }
        else if (llmProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var geminiKey = GetSecureValue("Gemini:ApiKey");
            if (string.IsNullOrEmpty(geminiKey))
            {
                errors.Add("Gemini API Key is not configured. Set GEMINI_API_KEY environment variable or Gemini:ApiKey in configuration.");
            }
        }

        // Validate Database configuration
        var dbConnectionString = GetSecureValue("Database:ConnectionString") ??
                                GetSecureValue("ConnectionStrings:DefaultConnection");
        if (string.IsNullOrEmpty(dbConnectionString))
        {
            warnings.Add("Database connection string is not configured. Using default SQLite database.");
        }

        // Validate Qdrant configuration (optional)
        var qdrantUrl = _configuration["Qdrant:Url"];
        if (string.IsNullOrEmpty(qdrantUrl))
        {
            warnings.Add("Qdrant URL is not configured. Vector search will use fallback in-memory storage.");
        }

        // Environment-specific validations
        if (_environment.IsProduction())
        {
            // Production-specific validations
            var allowedHosts = _configuration["AllowedHosts"];
            if (allowedHosts == "*")
            {
                warnings.Add("AllowedHosts is set to '*' in production. Consider restricting to specific domains.");
            }

            var rateLimitingEnabled = _configuration.GetValue<bool>("Production:EnableRateLimiting");
            if (!rateLimitingEnabled)
            {
                warnings.Add("Rate limiting is disabled in production environment.");
            }
        }

        result.IsValid = errors.Count == 0;
        result.Errors = errors;
        result.Warnings = warnings;

        // Log results
        if (result.IsValid)
        {
            _logger.LogInformation("Configuration validation passed with {WarningCount} warnings", warnings.Count);
            foreach (var warning in warnings)
            {
                _logger.LogWarning("Configuration warning: {Warning}", warning);
            }
        }
        else
        {
            _logger.LogError("Configuration validation failed with {ErrorCount} errors", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogError("Configuration error: {Error}", error);
            }
        }

        return result;
    }

    public string? GetConfigurationValue(string key, string? defaultValue = null)
    {
        // First try environment variable (convert key format)
        var envKey = ConvertToEnvironmentVariableKey(key);
        var envValue = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        // Then try configuration
        var configValue = _configuration[key];
        return configValue ?? defaultValue;
    }

    public string? GetSecureValue(string key)
    {
        // Priority order:
        // 1. Environment variables (highest priority)
        // 2. Configuration files
        // 3. Default values (lowest priority)

        var envKey = ConvertToEnvironmentVariableKey(key);
        var envValue = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        // Special handling for common secure keys
        switch (key.ToLowerInvariant())
        {
            case "jwt:key":
                return Environment.GetEnvironmentVariable("JWT_SECRET") ?? _configuration["Jwt:Key"];

            case "openai:apikey":
                return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"];

            case "gemini:apikey":
                return Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? _configuration["Gemini:ApiKey"];

            case "encryption:key":
                return Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? _configuration["Encryption:Key"];

            case "database:connectionstring":
                return Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ??
                       Environment.GetEnvironmentVariable("CONNECTION__DefaultConnection") ??
                       _configuration["Database:ConnectionString"] ??
                       _configuration.GetConnectionString("DefaultConnection");

            case "connectionstrings:defaultconnection":
                return Environment.GetEnvironmentVariable("IDENTITY_CONNECTION_STRING") ??
                       Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ??
                       Environment.GetEnvironmentVariable("CONNECTION__DefaultConnection") ??
                       _configuration.GetConnectionString("DefaultConnection") ??
                       _configuration["ConnectionStrings:DefaultConnection"];

            case "qdrant:apikey":
                return Environment.GetEnvironmentVariable("QDRANT_API_KEY") ?? _configuration["Qdrant:ApiKey"];

            default:
                return _configuration[key];
        }
    }

    public string GenerateSecureKey(int length = 32)
    {
        using var rng = RandomNumberGenerator.Create();
        var keyBytes = new byte[length];
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }

    private static string ConvertToEnvironmentVariableKey(string configKey)
    {
        // Convert configuration key format (e.g., "OpenAI:ApiKey") to environment variable format (e.g., "OPENAI_API_KEY")
        return configKey.Replace(":", "_").ToUpperInvariant();
    }
}

/// <summary>
/// Result of configuration validation
/// </summary>
public class ConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}