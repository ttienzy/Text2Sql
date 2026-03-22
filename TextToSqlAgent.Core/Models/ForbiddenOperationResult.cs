namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Result of forbidden operation rejection
/// </summary>
public class ForbiddenOperationResult
{
    /// <summary>Always true for forbidden operations</summary>
    public bool IsBlocked { get; set; } = true;

    /// <summary>User's original question</summary>
    public string OriginalQuestion { get; set; } = string.Empty;

    /// <summary>Reason for rejection</summary>
    public string RejectionReason { get; set; } = string.Empty;

    /// <summary>Detected dangerous patterns</summary>
    public List<string> DetectedPatterns { get; set; } = new();

    /// <summary>Safe alternatives to suggest</summary>
    public List<SafeAlternative> SafeAlternatives { get; set; } = new();

    /// <summary>User-facing error message</summary>
    public string UserFacingMessage { get; set; } = string.Empty;

    /// <summary>Timestamp of rejection</summary>
    public DateTime RejectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Intent classification that triggered rejection</summary>
    public IntentClassificationResult? IntentClassification { get; set; }
}
