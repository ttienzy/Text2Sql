using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace TextToSqlAgent.API.Validation;

/// <summary>
/// Validates that a string is a valid database provider
/// </summary>
public class ValidDatabaseProviderAttribute : ValidationAttribute
{
    private static readonly string[] ValidProviders = { "sqlserver", "postgresql", "mysql", "sqlite" };

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string provider && ValidProviders.Contains(provider.ToLowerInvariant()))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult($"Invalid database provider. Valid values are: {string.Join(", ", ValidProviders)}");
    }
}

/// <summary>
/// Validates that a string doesn't contain SQL injection patterns
/// </summary>
public class NoSqlInjectionAttribute : ValidationAttribute
{
    private static readonly string[] DangerousPatterns =
    {
        "drop", "delete", "truncate", "alter", "create", "exec", "execute",
        "sp_", "xp_", "union", "insert", "update", "--", "/*", "*/"
    };

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string input || string.IsNullOrEmpty(input))
        {
            return ValidationResult.Success;
        }

        var lowerInput = input.ToLowerInvariant();
        foreach (var pattern in DangerousPatterns)
        {
            if (lowerInput.Contains(pattern))
            {
                return new ValidationResult($"Input contains potentially dangerous SQL pattern: {pattern}");
            }
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a string is a valid connection string format
/// </summary>
public class ValidConnectionStringAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string connectionString || string.IsNullOrEmpty(connectionString))
        {
            return new ValidationResult("Connection string is required");
        }

        // Basic validation - should contain key=value pairs
        if (!connectionString.Contains('='))
        {
            return new ValidationResult("Invalid connection string format");
        }

        // Check for dangerous keywords
        var dangerous = new[] { "drop", "delete", "truncate", "alter table" };
        var lower = connectionString.ToLowerInvariant();

        foreach (var keyword in dangerous)
        {
            if (lower.Contains(keyword))
            {
                return new ValidationResult($"Connection string contains dangerous keyword: {keyword}");
            }
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a string is a safe identifier (table/column name)
/// </summary>
public class SafeIdentifierAttribute : ValidationAttribute
{
    private static readonly Regex SafeIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string identifier || string.IsNullOrEmpty(identifier))
        {
            return ValidationResult.Success; // Allow empty for optional fields
        }

        if (!SafeIdentifierRegex.IsMatch(identifier))
        {
            return new ValidationResult("Identifier must start with a letter or underscore and contain only letters, numbers, and underscores");
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a string is a valid GUID
/// </summary>
public class ValidGuidAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string guidString || string.IsNullOrEmpty(guidString))
        {
            return new ValidationResult("GUID is required");
        }

        if (!Guid.TryParse(guidString, out _))
        {
            return new ValidationResult("Invalid GUID format");
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a string doesn't exceed maximum length and doesn't contain dangerous content
/// </summary>
public class SafeTextAttribute : ValidationAttribute
{
    public int MaxLength { get; set; } = 1000;
    public bool AllowHtml { get; set; } = false;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text)
        {
            return ValidationResult.Success;
        }

        if (text.Length > MaxLength)
        {
            return new ValidationResult($"Text exceeds maximum length of {MaxLength} characters");
        }

        if (!AllowHtml && (text.Contains('<') || text.Contains('>')))
        {
            return new ValidationResult("HTML content is not allowed");
        }

        // Check for script injection
        var dangerous = new[] { "<script", "javascript:", "vbscript:", "onload=", "onerror=" };
        var lower = text.ToLowerInvariant();

        foreach (var pattern in dangerous)
        {
            if (lower.Contains(pattern))
            {
                return new ValidationResult($"Text contains potentially dangerous content: {pattern}");
            }
        }

        return ValidationResult.Success;
    }
}