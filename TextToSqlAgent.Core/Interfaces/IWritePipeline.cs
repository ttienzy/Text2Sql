using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Pipeline for handling write operations (INSERT, UPDATE)
/// Requires user confirmation before execution
/// </summary>
public interface IWritePipeline
{
    /// <summary>
    /// Generate preview of write operation without executing
    /// </summary>
    Task<WriteOperationPreview> GeneratePreviewAsync(
        WriteOperationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Execute write operation after user confirmation
    /// </summary>
    Task<WriteOperationResult> ExecuteAsync(
        WriteOperationRequest request,
        WriteOperationPreview preview,
        CancellationToken ct = default);
}
