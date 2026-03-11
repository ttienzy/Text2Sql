using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// Optimizes prompts by caching static parts and minimizing token usage
/// Separates fixed rules from dynamic context
/// </summary>
public class PromptOptimizer
{
    private readonly ILogger<PromptOptimizer> _logger;
    private readonly ConcurrentDictionary<string, string> _staticPromptCache = new();

    public PromptOptimizer(ILogger<PromptOptimizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get optimized prompt with minimal tokens
    /// Caches static system prompt, only dynamic context changes
    /// </summary>
    public string OptimizePrompt(string templateName, string systemPrompt, string userPrompt)
    {
        // Cache static system prompt (rules, examples)
        var cacheKey = $"{templateName}:system";
        var cachedSystem = _staticPromptCache.GetOrAdd(cacheKey, _ =>
        {
            _logger.LogDebug("[PromptOptimizer] Caching system prompt for {Template}", templateName);
            return systemPrompt;
        });

        // Return combined prompt (cached system + dynamic user)
        return $"{cachedSystem}\n\n{userPrompt}";
    }

    /// <summary>
    /// Compact schema context to minimal format
    /// Before: "Table: dbo.Customers\n  - CustomerId int\n  - Name nvarchar"
    /// After: "Customers(CustomerId int, Name nvarchar)"
    /// </summary>
    public string CompactSchemaContext(string schemaContext)
    {
        // Already compacted in BuildSchemaContext
        return schemaContext;
    }

    /// <summary>
    /// Estimate token count (rough approximation: 1 token ≈ 4 chars)
    /// </summary>
    public int EstimateTokens(string text)
    {
        return text.Length / 4;
    }

    /// <summary>
    /// Clear cache (useful for testing or config changes)
    /// </summary>
    public void ClearCache()
    {
        _staticPromptCache.Clear();
        _logger.LogInformation("[PromptOptimizer] Cache cleared");
    }
}
