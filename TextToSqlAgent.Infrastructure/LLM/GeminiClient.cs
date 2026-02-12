using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Google;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.ErrorHandling;

namespace TextToSqlAgent.Infrastructure.LLM;

public class GeminiClient : ILLMClient
{
    private readonly Kernel _kernel;
    private readonly GeminiConfig _config;
    private readonly ILogger<GeminiClient> _logger;
    private readonly LLMErrorHandler? _llmErrorHandler;

    public GeminiClient(
    GeminiConfig config,
    ILogger<GeminiClient> logger,
    LLMErrorHandler? llmErrorHandler = null)
    {
        _config = config;
        _logger = logger;
        _llmErrorHandler = llmErrorHandler;

        // ✅ Validate API key
        if (!ValidateApiKey(config.ApiKey))
        {
            _logger.LogWarning(
                "[Gemini Client] API key validation failed. LLM requests will likely fail.");
            _logger.LogWarning(
                "[Gemini Client] To set API key: dotnet user-secrets set \"Gemini:ApiKey\" \"YOUR_KEY\"");
        }

        try
        {
            var builder = Kernel.CreateBuilder();

            builder.AddGoogleAIGeminiChatCompletion(
                modelId: config.Model,
                apiKey: config.ApiKey);

            _kernel = builder.Build();

            _logger.LogInformation("[Gemini Client] Initialized with model: {Model}", config.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini Client] Failed to initialize Semantic Kernel");
            throw new LLMApiException(
                $"Failed to initialize Gemini client: {ex.Message}\n" +
                "Please check your configuration and API key.",
                null,
                ex);
        }
    }

    public async Task<string> CompleteAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
        }

        _logger.LogDebug("[Gemini Client] Sending prompt ({Length} chars)", prompt.Length);
        try
        {
            // Use LLM error handler if available
            if (_llmErrorHandler != null)
            {
                return await _llmErrorHandler.HandleLLMErrorAsync(
                    async () => await InvokePromptInternalAsync(prompt, cancellationToken),
                    new Exception("Pre-check"),
                    cancellationToken);
            }
            else
            {
                // Fallback to direct call
                return await InvokePromptInternalAsync(prompt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini Client] Unexpected error calling Gemini API");
            throw;
        }
    }

    private async Task<string> InvokePromptInternalAsync(
    string prompt,
    CancellationToken cancellationToken)
    {
        try
        {
            var settings = new GeminiPromptExecutionSettings
            {
                Temperature = _config.Temperature,
                MaxTokens = _config.MaxTokens
            };

            var result = await _kernel.InvokePromptAsync(
                prompt,
                new KernelArguments(settings),
                cancellationToken: cancellationToken);

            var response = result.ToString();

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new LLMApiException(
                    "Gemini API returned an empty response.");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw HandleHttpException(ex);
        }
        catch (KernelException ex)
        {
            var innerEx = ex.InnerException;
            if (innerEx is HttpRequestException httpEx)
            {
                throw HandleHttpException(httpEx);  
            }

            throw new LLMApiException(
                $"Gemini API error: {ex.Message}",
                null,
                ex);
        }
    }

    public async Task<string> CompleteWithSystemPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var fullPrompt = $"{systemPrompt}\n\n{userPrompt}";
        return await CompleteAsync(fullPrompt, cancellationToken);
    }
    private bool ValidateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("[Gemini Client] API key is null or empty");
            return false;
        }

        // Gemini API keys typically start with "AIza"
        if (!apiKey.StartsWith("AIza"))
        {
            _logger.LogWarning(
                "[Gemini Client] API key does not start with 'AIza' - may be invalid");
            return false;
        }

        // Gemini API keys are typically 39 characters
        if (apiKey.Length < 30 || apiKey.Length > 50)
        {
            _logger.LogWarning(
                "[Gemini Client] API key length ({Length}) seems unusual",
                apiKey.Length);
            return false;
        }

        _logger.LogDebug("[Gemini Client] API key format looks valid");
        return true;
    }
    private Exception HandleHttpException(HttpRequestException ex)
    {
        var statusCode = ex.StatusCode;

        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                new LLMApiException(
                    "Invalid Gemini API key. Please check your configuration.",
                    401,
                    ex),

            System.Net.HttpStatusCode.Forbidden =>
                new QuotaExceededException(
                    $"Gemini API quota exceeded: {ex.Message}"),

            System.Net.HttpStatusCode.TooManyRequests =>
                new RateLimitException(
                    $"Rate limit exceeded: {ex.Message}",
                    60),

            System.Net.HttpStatusCode.ServiceUnavailable =>
                new LLMApiException(
                    $"Service unavailable: {ex.Message}",
                    503,
                    ex),

            _ => new LLMApiException(
                $"HTTP error ({statusCode}): {ex.Message}",
                (int?)statusCode,
                ex)
        };
    }
}