using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.ErrorHandling;

namespace TextToSqlAgent.Infrastructure.LLM;

/// <summary>
/// OpenAI LLM client implementation using Semantic Kernel
/// </summary>
public class OpenAIClient : ILLMClient
{
    private readonly Kernel _kernel;
    private readonly OpenAIConfig _config;
    private readonly ILogger<OpenAIClient> _logger;
    private readonly LLMErrorHandler? _llmErrorHandler;

    public OpenAIClient(
        OpenAIConfig config,
        ILogger<OpenAIClient> logger,
        LLMErrorHandler? llmErrorHandler = null)
    {
        _config = config;
        _logger = logger;
        _llmErrorHandler = llmErrorHandler;

        // Validate API key
        if (!ValidateApiKey(config.ApiKey))
        {
            _logger.LogWarning(
                "[OpenAI Client] API key validation failed. LLM requests will likely fail.");
            _logger.LogWarning(
                "[OpenAI Client] To set API key: dotnet user-secrets set \"OpenAI:ApiKey\" \"YOUR_KEY\"");
        }

        try
        {
            var builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(
                modelId: config.Model,
                apiKey: config.ApiKey,
                orgId: config.OrganizationId);

            _kernel = builder.Build();

            _logger.LogInformation("[OpenAI Client] Initialized with model: {Model}", config.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenAI Client] Failed to initialize Semantic Kernel");
            throw new LLMApiException(
                $"Failed to initialize OpenAI client: {ex.Message}\n" +
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

        _logger.LogDebug("[OpenAI Client] Sending prompt ({Length} chars)", prompt.Length);

        try
        {
            // Call LLM directly
            return await InvokePromptInternalAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OpenAI Client] LLM call failed, attempting error recovery...");

            // Use error handler for recovery if available
            if (_llmErrorHandler != null)
            {
                return await _llmErrorHandler.HandleLLMErrorAsync(
                    async () => await InvokePromptInternalAsync(prompt, cancellationToken),
                    ex,
                    cancellationToken);
            }

            _logger.LogError(ex, "[OpenAI Client] No error handler available, throwing");
            throw;
        }
    }

    private async Task<string> InvokePromptInternalAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = new OpenAIPromptExecutionSettings
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
                    "OpenAI API returned an empty response.");
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
                $"OpenAI API error: {ex.Message}",
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
            _logger.LogError("[OpenAI Client] API key is null or empty");
            return false;
        }

        // OpenAI API keys typically start with "sk-"
        if (!apiKey.StartsWith("sk-"))
        {
            _logger.LogWarning(
                "[OpenAI Client] API key does not start with 'sk-' - may be invalid");
            return false;
        }

        // OpenAI API keys are typically longer than 40 characters
        if (apiKey.Length < 40)
        {
            _logger.LogWarning(
                "[OpenAI Client] API key length ({Length}) seems too short",
                apiKey.Length);
            return false;
        }

        _logger.LogDebug("[OpenAI Client] API key format looks valid");
        return true;
    }

    private Exception HandleHttpException(HttpRequestException ex)
    {
        var statusCode = ex.StatusCode;

        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                new LLMApiException(
                    "Invalid OpenAI API key. Please check your configuration.",
                    401,
                    ex),

            System.Net.HttpStatusCode.Forbidden =>
                new QuotaExceededException(
                    $"OpenAI API quota exceeded: {ex.Message}"),

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
