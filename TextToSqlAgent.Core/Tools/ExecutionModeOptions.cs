namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Defines execution mode presets for the agent
/// </summary>
public static class ExecutionModeOptions
{
    /// <summary>
    /// Fast mode: Uses only essential tools for quick dashboard queries
    /// </summary>
    public const string Fast = "fast";

    /// <summary>
    /// Standard mode: Uses all tools for most queries
    /// </summary>
    public const string Standard = "standard";

    /// <summary>
    /// Thorough mode: Uses all tools with aggressive retry for complex reports
    /// </summary>
    public const string Thorough = "thorough";

    /// <summary>
    /// Safe mode: Uses all tools with mandatory validation for production
    /// </summary>
    public const string Safe = "safe";

    /// <summary>
    /// Default execution mode
    /// </summary>
    public const string Default = Standard;

    /// <summary>
    /// Tool names for fast mode (minimal set for real-time dashboards)
    /// </summary>
    public static readonly string[] FastModeTools = new[]
    {
        "explore_schema",
        "generate_sql",
        "execute_sql"
    };

    /// <summary>
    /// All available tool names (for standard, thorough, safe modes)
    /// </summary>
    public static readonly string[] AllTools = new[]
    {
        "explore_schema",
        "generate_sql",
        "execute_sql",
        "validate_sql",
        "decompose_query",
        "detect_ambiguity",
        "analyze_complexity",
        "verify_result"
    };

    /// <summary>
    /// Get the default max steps for an execution mode
    /// </summary>
    public static int GetMaxSteps(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            Fast => 5,
            Standard => 15,
            Thorough => 30,
            Safe => 20,
            _ => 15 // Default to standard
        };
    }

    /// <summary>
    /// Get the allowed tools for an execution mode
    /// </summary>
    public static List<string> GetAllowedTools(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            Fast => FastModeTools.ToList(),
            Standard => AllTools.ToList(),
            Thorough => AllTools.ToList(),
            Safe => AllTools.ToList(),
            _ => AllTools.ToList() // Default to standard
        };
    }

    /// <summary>
    /// Get whether aggressive retry is enabled for an execution mode
    /// </summary>
    public static bool IsAggressiveRetryEnabled(string? mode)
    {
        return mode?.ToLowerInvariant() == Thorough;
    }

    /// <summary>
    /// Get whether mandatory validation is enabled for an execution mode
    /// </summary>
    public static bool IsMandatoryValidationEnabled(string? mode)
    {
        return mode?.ToLowerInvariant() == Safe;
    }

    /// <summary>
    /// Validate execution mode
    /// </summary>
    public static bool IsValidMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return true; // null/empty is valid (uses default)

        var lowerMode = mode.ToLowerInvariant();
        return lowerMode == Fast || lowerMode == Standard ||
               lowerMode == Thorough || lowerMode == Safe;
    }

    /// <summary>
    /// Normalize execution mode to standard value
    /// </summary>
    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return Default;

        var lowerMode = mode.ToLowerInvariant();
        return lowerMode switch
        {
            Fast => Fast,
            Standard => Standard,
            Thorough => Thorough,
            Safe => Safe,
            _ => Default
        };
    }
}
