using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Infrastructure.Caching;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Service for managing agent session state in Redis for Human-in-the-Loop support
/// </summary>
public interface IAgentSessionService
{
    /// <summary>
    /// Save agent state when clarification is needed
    /// </summary>
    Task<string> SaveSessionAsync(
        string userId,
        string question,
        AgentState? agentState,
        Dictionary<string, object>? workingMemory,
        List<AgentStep>? steps,
        ClarificationType clarificationType,
        string? pendingSql = null,
        string? conversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load agent state from session
    /// </summary>
    Task<AgentSessionState?> GetSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Remove session after clarification is answered
    /// </summary>
    Task<bool> RemoveSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Check if session exists and is valid
    /// </summary>
    Task<bool> SessionExistsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Get clarification request for session
    /// </summary>
    Task<ClarificationRequest?> GetClarificationRequestAsync(string sessionId, CancellationToken ct = default);
}

public class AgentSessionService : IAgentSessionService
{
    private readonly CacheService _cache;
    private readonly ILogger<AgentSessionService> _logger;
    private readonly TimeSpan _sessionTtl = TimeSpan.FromMinutes(10);
    private const string SessionKeyPrefix = "agent_session:";

    public AgentSessionService(
        CacheService cache,
        ILogger<AgentSessionService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> SaveSessionAsync(
        string userId,
        string question,
        AgentState? agentState,
        Dictionary<string, object>? workingMemory,
        List<AgentStep>? steps,
        ClarificationType clarificationType,
        string? pendingSql = null,
        string? conversationId = null,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new AgentSessionState
        {
            SessionId = sessionId,
            UserId = userId,
            ConversationId = conversationId,
            OriginalQuestion = question,
            AgentStateJson = agentState != null ? JsonSerializer.Serialize(agentState) : null,
            WorkingMemory = workingMemory,
            Steps = steps,
            PendingClarificationType = clarificationType,
            PendingSql = pendingSql,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_sessionTtl)
        };

        var key = GetSessionKey(sessionId);
        await _cache.SetAsync(key, session, _sessionTtl, ct);

        _logger.LogInformation(
            "Saved agent session {SessionId} for user {UserId}, type: {Type}, expires: {Expires}",
            sessionId, userId, clarificationType, session.ExpiresAt);

        return sessionId;
    }

    public async Task<AgentSessionState?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var key = GetSessionKey(sessionId);
        var session = await _cache.GetAsync<AgentSessionState>(key, ct);

        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found or expired", sessionId);
            return null;
        }

        // Check if expired
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Session {SessionId} has expired at {Expires}", sessionId, session.ExpiresAt);
            await _cache.RemoveAsync(key, ct);
            return null;
        }

        _logger.LogDebug("Retrieved session {SessionId}, type: {Type}",
            sessionId, session.PendingClarificationType);

        return session;
    }

    public async Task<bool> RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var key = GetSessionKey(sessionId);
        await _cache.RemoveAsync(key, ct);
        _logger.LogInformation("Removed session {SessionId}", sessionId);
        return true;
    }

    public async Task<bool> SessionExistsAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct);
        return session != null;
    }

    public async Task<ClarificationRequest?> GetClarificationRequestAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct);
        if (session == null)
            return null;

        var clarification = new ClarificationRequest
        {
            SessionId = session.SessionId,
            Type = session.PendingClarificationType ?? ClarificationType.ambiguous_question,
            OriginalQuestion = session.OriginalQuestion,
            SqlQuery = session.PendingSql,
            Timeout = (int)(session.ExpiresAt - DateTime.UtcNow).TotalSeconds,
            RequestedAt = session.CreatedAt
        };

        // Set the question based on clarification type
        clarification.Question = session.PendingClarificationType switch
        {
            ClarificationType.dml_confirmation => BuildDmlConfirmationQuestion(session.PendingSql),
            _ => "I need some clarification to answer your question properly."
        };

        // Set options based on clarification type
        if (session.PendingClarificationType == ClarificationType.dml_confirmation)
        {
            clarification.Options = new List<string> { "Yes, execute the query", "No, cancel" };
        }

        return clarification;
    }

    private string BuildDmlConfirmationQuestion(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
            return "This query will modify data. Do you want to proceed?";

        var preview = sql.Length > 200 ? sql.Substring(0, 200) + "..." : sql;
        return $"This query will execute the following SQL:\n\n```sql\n{preview}\n```\n\nDo you want to proceed?";
    }

    private static string GetSessionKey(string sessionId) => $"{SessionKeyPrefix}{sessionId}";
}