using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Pipeline for handling DDL operations (CREATE INDEX, ALTER TABLE, CREATE VIEW/PROC)
/// Requires impact analysis and user confirmation before execution
/// </summary>
public interface IDDLPipeline
{
    /// <summary>
    /// Generate preview of DDL operation with impact analysis
    /// </summary>
    Task<DDLOperationPreview> GeneratePreviewAsync(
        DDLOperationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Execute DDL operation after user confirmation
    /// </summary>
    Task<DDLOperationResult> ExecuteAsync(
        DDLOperationRequest request,
        DDLOperationPreview preview,
        CancellationToken ct = default);
}
