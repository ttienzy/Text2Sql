using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Caching;

namespace TextToSqlAgent.Infrastructure.LLM;

/// <summary>
/// PHASE-2 TASK 2.2e: Decorator that adds caching to any ILLMClient implementation.
/// Caches responses based on prompt content to avoid redundant LLM calls.
/// </summary>
public class CachedLLMClient : ILLMClient
{
    private readonly ILLMClient _innerClient;
    private readonly LLMResponseCache _cache;
    private readonly ILogger<CachedLLMClient> _logger;
    private readonly string _modelName;

    public CachedLLMClient(
        ILLMClient innerClient,
        LLMResponseCache cache,
        ILogger<CachedLLMClient> logger,
        string modelName = "default")
    {
        _innerClient = innerClient;
        _cache = cache;
        _logger = logger;
        _modelName = modelName;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // For single prompt, use empty system prompt
        return await CompleteWithSystemPromptAsync("", prompt, cancellationToken);
    }

    public async Task<string> CompleteWithSystemPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cached = _cache.Get(systemPrompt, userPrompt, _modelName);
        if (cached != null)
        {
            _logger.LogDebug("[CachedLLM] ⚡ Cache HIT - saved LLM call");
            return cached;
        }

        // Cache miss - call actual LLM
        _logger.LogDebug("[CachedLLM] Cache MISS - calling LLM");
        var response = await _innerClient.CompleteWithSystemPromptAsync(
            systemPrompt, userPrompt, cancellationToken);

        // Store in cache
        _cache.Set(systemPrompt, userPrompt, response, _modelName);

        return response;
    }

    public async Task<string> CompleteWithSystemPromptStreamAsync(
        string systemPrompt,
        string userPrompt,
        Action<string>? tokenCallback = null,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ Streaming cannot be cached effectively (tokens arrive incrementally)
        // We could cache the final result, but not the streaming behavior
        // For now, bypass cache for streaming calls

        _logger.LogDebug("[CachedLLM] Streaming call - bypassing cache");
        var response = await _innerClient.CompleteWithSystemPromptStreamAsync(
            systemPrompt, userPrompt, tokenCallback, cancellationToken);

        // Optionally cache the final result for future non-streaming calls
        _cache.Set(systemPrompt, userPrompt, response, _modelName);

        return response;
    }
}
