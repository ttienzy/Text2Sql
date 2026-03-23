using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Pipeline that handles forbidden operations (DELETE, DROP, TRUNCATE)
/// Always rejects with safe alternatives
/// </summary>
public interface IForbiddenPipeline
{
    /// <summary>
    /// Reject forbidden operation and provide safe alternatives (AI-generated message)
    /// </summary>
    /// <param name="question">User's question</param>
    /// <param name="intentResult">Intent classification result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rejection result with safe alternatives</returns>
    Task<ForbiddenOperationResult> RejectAsync(
        string question,
        IntentClassificationResult intentResult,
        CancellationToken cancellationToken = default);
}
