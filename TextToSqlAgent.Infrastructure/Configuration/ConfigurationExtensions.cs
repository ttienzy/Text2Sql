using Microsoft.Extensions.Configuration;

namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Extension methods for loading configuration from environment variables
/// with fallback to appsettings.json
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Get OpenAI API Key from environment variable or configuration
    /// Priority: Environment Variable > appsettings.json
    /// </summary>
    public static string GetOpenAiApiKey(this IConfiguration configuration)
    {
        // First try environment variable
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            return envApiKey;
        }

        // Fallback to appsettings
        return configuration["OpenAI:ApiKey"] ?? string.Empty;
    }

    /// <summary>
    /// Get Gemini API Key from environment variable or configuration
    /// Priority: Environment Variable > appsettings.json
    /// </summary>
    public static string GetGeminiApiKey(this IConfiguration configuration)
    {
        // First try environment variable
        var envApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            return envApiKey;
        }

        // Fallback to appsettings
        return configuration["Gemini:ApiKey"] ?? string.Empty;
    }

    /// <summary>
    /// Get Qdrant API Key from environment variable or configuration
    /// Priority: Environment Variable > appsettings.json
    /// </summary>
    public static string GetQdrantApiKey(this IConfiguration configuration)
    {
        // First try environment variable
        var envApiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            return envApiKey;
        }

        // Fallback to appsettings
        return configuration["Qdrant:ApiKey"] ?? string.Empty;
    }

    /// <summary>
    /// Get JWT Secret from environment variable or configuration
    /// Priority: Environment Variable > appsettings.json
    /// </summary>
    public static string GetJwtSecret(this IConfiguration configuration)
    {
        // First try environment variable
        var envSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (!string.IsNullOrWhiteSpace(envSecret))
        {
            return envSecret;
        }

        // Fallback to appsettings
        return configuration["Jwt:Key"] ?? string.Empty;
    }
}
