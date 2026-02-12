using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.Factories;

/// <summary>
/// Factory to create the appropriate embedding client based on configuration
/// </summary>
public class EmbeddingClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly GeminiConfig _geminiConfig;
    private readonly OpenAIConfig _openAIConfig;
    private readonly ILogger<EmbeddingClientFactory> _logger;

    public EmbeddingClientFactory(
        IConfiguration configuration,
        GeminiConfig geminiConfig,
        OpenAIConfig openAIConfig,
        ILogger<EmbeddingClientFactory> logger)
    {
        _configuration = configuration;
        _geminiConfig = geminiConfig;
        _openAIConfig = openAIConfig;
        _logger = logger;
    }

    /// <summary>
    /// Create embedding client based on configured provider
    /// </summary>
    public IEmbeddingClient CreateClient()
    {
        var providerString = _configuration["LLMProvider"] ?? "Gemini";
        
        if (!Enum.TryParse<LLMProvider>(providerString, ignoreCase: true, out var provider))
        {
            _logger.LogWarning(
                "[EmbeddingClientFactory] Invalid LLMProvider '{Provider}', defaulting to Gemini",
                providerString);
            provider = LLMProvider.Gemini;
        }

        _logger.LogInformation("[EmbeddingClientFactory] Creating {Provider} embedding client", provider);

        return provider switch
        {
            LLMProvider.Gemini => CreateGeminiEmbeddingClient(),
            LLMProvider.OpenAI => CreateOpenAIEmbeddingClient(),
            _ => throw new InvalidOperationException($"Unsupported embedding provider: {provider}")
        };
    }

    private IEmbeddingClient CreateGeminiEmbeddingClient()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LLM.GeminiEmbeddingClient>();
        
        return new LLM.GeminiEmbeddingClient(_geminiConfig, logger);
    }

    private IEmbeddingClient CreateOpenAIEmbeddingClient()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LLM.OpenAIEmbeddingClient>();
        
        return new LLM.OpenAIEmbeddingClient(_openAIConfig, logger);
    }
}
