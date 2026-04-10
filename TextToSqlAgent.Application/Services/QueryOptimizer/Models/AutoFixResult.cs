using System.Collections.Generic;

namespace TextToSqlAgent.Application.Services.QueryOptimizer.Models;

/// <summary>
/// Result of automatic query fix operation
/// </summary>
public class AutoFixResult
{
    public string OriginalSql { get; set; } = string.Empty;
    public string FixedSql { get; set; } = string.Empty;
    public bool RequiresSemanticValidation { get; set; }
    public ConfidenceLevel Confidence { get; set; }
    public List<string> FixesApplied { get; set; } = new();
    public List<string> SemanticRisks { get; set; } = new();
    public string? ValidationQuery { get; set; }

    /// <summary>
    /// Can this fix be auto-applied without user confirmation?
    /// </summary>
    public bool CanAutoApply => Confidence == ConfidenceLevel.High && !RequiresSemanticValidation;
}
