using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.ErrorHandling;

namespace TextToSqlAgent.Infrastructure.Factories;

/// <summary>
/// Factory to create the appropriate LLM client based on configuration
/// </summary>
public class LLMClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly GeminiConfig _geminiConfig;
    private readonly OpenAIConfig _openAIConfig;
    private readonly ILogger<LLMClientFactory> _logger;
    private readonly LLMErrorHandler? _llmErrorHandler;

    public LLMClientFactory(
        IConfiguration configuration,
        GeminiConfig geminiConfig,
        OpenAIConfig openAIConfig,
        ILogger<LLMClientFactory> logger,
        LLMErrorHandler? llmErrorHandler = null)
    {
        _configuration = configuration;
        _geminiConfig = geminiConfig;
        _openAIConfig = openAIConfig;
        _logger = logger;
        _llmErrorHandler = llmErrorHandler;
    }

    /// <summary>
    /// Create LLM client based on configured provider
    /// </summary>
    public ILLMClient CreateClient()
    {
        var providerString = _configuration["LLMProvider"] ?? "Gemini";
        
        if (!Enum.TryParse<LLMProvider>(providerString, ignoreCase: true, out var provider))
        {
            _logger.LogWarning(
                "[LLMClientFactory] Invalid LLMProvider '{Provider}', defaulting to Gemini",
                providerString);
            provider = LLMProvider.Gemini;
        }

        _logger.LogInformation("[LLMClientFactory] Creating {Provider} client", provider);

        return provider switch
        {
            LLMProvider.Gemini => CreateGeminiClient(),
            LLMProvider.OpenAI => CreateOpenAIClient(),
            _ => throw new InvalidOperationException($"Unsupported LLM provider: {provider}")
        };
    }

    private ILLMClient CreateGeminiClient()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var geminiLogger = loggerFactory.CreateLogger<LLM.GeminiClient>();
        
        return new LLM.GeminiClient(_geminiConfig, geminiLogger, _llmErrorHandler);
    }

    private ILLMClient CreateOpenAIClient()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var openAILogger = loggerFactory.CreateLogger<LLM.OpenAIClient>();
        
        return new LLM.OpenAIClient(_openAIConfig, openAILogger, _llmErrorHandler);
    }
}
