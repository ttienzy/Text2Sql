using Microsoft.Extensions.Logging;

namespace TextToSqlAgent.Infrastructure.Analysis;

/// <summary>
/// Detects DML (Data Manipulation Language) queries that require user confirmation
/// </summary>
public class DmlDetector
{
    private readonly ILogger<DmlDetector> _logger;

    // SQL keywords that indicate DML operations
    private static readonly string[] DmlKeywords = { "UPDATE", "DELETE", "INSERT", "MERGE", "TRUNCATE" };
    private static readonly string[] DangerousKeywords = { "DROP", "ALTER", "CREATE" };

    public DmlDetector(ILogger<DmlDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze SQL query to determine if it requires confirmation
    /// </summary>
    public DmlAnalysisResult Analyze(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new DmlAnalysisResult
            {
                IsDml = false,
                RequiresConfirmation = false
            };
        }

        var upperSql = sql.ToUpperInvariant();

        // Check for DML operations
        var detectedDml = new List<string>();
        foreach (var keyword in DmlKeywords)
        {
            if (upperSql.Contains(keyword))
            {
                detectedDml.Add(keyword);
            }
        }

        // Check for dangerous operations
        var dangerousOps = new List<string>();
        foreach (var keyword in DangerousKeywords)
        {
            if (upperSql.Contains(keyword))
            {
                dangerousOps.Add(keyword);
            }
        }

        var isDml = detectedDml.Count > 0;
        var isDangerous = dangerousOps.Count > 0;

        // Determine if confirmation is required
        // UPDATE and DELETE always require confirmation
        // INSERT requires confirmation for safety
        var requiresConfirmation = isDml && (detectedDml.Contains("UPDATE") || detectedDml.Contains("DELETE"));

        if (isDml)
        {
            _logger.LogWarning(
                "DML query detected: {DmlTypes}. Confirmation required: {Required}",
                string.Join(", ", detectedDml),
                requiresConfirmation);
        }

        return new DmlAnalysisResult
        {
            IsDml = isDml,
            DmlTypes = detectedDml,
            IsDangerous = isDangerous,
            DangerousOperations = dangerousOps,
            RequiresConfirmation = requiresConfirmation,
            SqlPreview = sql.Length > 200 ? sql.Substring(0, 200) + "..." : sql
        };
    }

    /// <summary>
    /// Check if query is a SELECT (read-only) - safe to execute without confirmation
    /// </summary>
    public bool IsSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        var upperSql = sql.ToUpperInvariant().Trim();

        // Must start with SELECT
        if (!upperSql.StartsWith("SELECT") && !upperSql.StartsWith("WITH")) // CTE queries
            return false;

        // Check that it's not a SELECT INTO (which creates a table)
        if (upperSql.Contains("INTO") && !upperSql.Contains("INSERT INTO"))
            return false;

        return true;
    }
}

/// <summary>
/// Result of DML analysis
/// </summary>
public class DmlAnalysisResult
{
    /// <summary>Whether the query contains DML operations</summary>
    public bool IsDml { get; set; }

    /// <summary>Types of DML operations detected</summary>
    public List<string> DmlTypes { get; set; } = new();

    /// <summary>Whether the query contains dangerous operations (DDL)</summary>
    public bool IsDangerous { get; set; }

    /// <summary>Dangerous operations detected</summary>
    public List<string> DangerousOperations { get; set; } = new();

    /// <summary>Whether user confirmation is required before execution</summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>Preview of the SQL (truncated if long)</summary>
    public string? SqlPreview { get; set; }
}