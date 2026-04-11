using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Stores pending DML/DDL confirmations in Redis.
/// SSE controller stores a PendingConfirmation, then polls for a ConfirmationResult.
/// The /confirm endpoint writes the ConfirmationResult.
/// </summary>
public interface IConfirmationStore
{
    /// <summary>
    /// Store a pending confirmation with auto-expiry TTL.
    /// Returns the generated confirmId.
    /// </summary>
    Task<string> StoreAsync(PendingConfirmation confirmation, CancellationToken ct = default);

    /// <summary>
    /// Retrieve the pending confirmation without removing it (for validation).
    /// Returns null if expired or not found.
    /// </summary>
    Task<PendingConfirmation?> GetAsync(string confirmId, CancellationToken ct = default);

    /// <summary>
    /// Record the user's decision (approve/cancel).
    /// The SSE polling loop will pick this up.
    /// </summary>
    Task SetResultAsync(string confirmId, ConfirmationResult result, CancellationToken ct = default);

    /// <summary>
    /// Poll for the user's decision. Returns null if no decision yet.
    /// Called by the SSE controller in a polling loop (every 1s, up to TimeoutSeconds).
    /// </summary>
    Task<ConfirmationResult?> GetResultAsync(string confirmId, CancellationToken ct = default);

    /// <summary>
    /// Clean up all keys related to a confirmation (pending + result).
    /// Called after execution completes or on timeout.
    /// </summary>
    Task CleanupAsync(string confirmId, CancellationToken ct = default);
}
