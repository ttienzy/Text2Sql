using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Infrastructure.Caching;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// Cached agent wrapper that caches SQL results
/// </summary>
public class CachedAgent : IAgent
{
    private readonly IAgent _innerAgent;
    private readonly CacheService _cacheService;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<CachedAgent> _logger;

    public CachedAgent(
        IAgent innerAgent,
        CacheService cacheService,
        CacheOptions cacheOptions,
        ILogger<CachedAgent> logger)
    {
        _innerAgent = innerAgent;
        _cacheService = cacheService;
        _cacheOptions = cacheOptions;
        _logger = logger;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        if (!_cacheOptions.EnableCaching)
        {
            return await _innerAgent.RunAsync(request, ct);
        }

        // Generate cache key based on question and database
        var cacheKey = $"agent_result:{request.DatabaseId}:{GetQuestionHash(request.Question)}";

        try
        {
            // Try to get from cache
            var cachedResult = await _cacheService.GetAsync<AgentResult>(cacheKey, ct);

            if (cachedResult != null)
            {
                _logger.LogInformation(
                    "Cache hit for question: {Question}",
                    request.Question);

                // Mark as cached
                cachedResult.FromCache = true;
                return cachedResult;
            }

            // Cache miss - execute agent
            _logger.LogInformation(
                "Cache miss for question: {Question}",
                request.Question);

            var result = await _innerAgent.RunAsync(request, ct);

            // Cache successful results only
            if (result.Success)
            {
                await _cacheService.SetAsync(
                    cacheKey,
                    result,
                    _cacheOptions.SqlResultExpiration,
                    ct);

                _logger.LogInformation(
                    "Cached result for question: {Question}",
                    request.Question);
            }

            result.FromCache = false;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache operation failed, falling back to direct execution");
            return await _innerAgent.RunAsync(request, ct);
        }
    }

    public AgentState GetState()
    {
        return _innerAgent.GetState();
    }

    public void Reset()
    {
        _innerAgent.Reset();
    }

    private string GetQuestionHash(string question)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(question.ToLower().Trim());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}
