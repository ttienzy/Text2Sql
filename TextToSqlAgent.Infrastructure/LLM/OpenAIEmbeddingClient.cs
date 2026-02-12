using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.LLM;

/// <summary>
/// OpenAI Embedding client implementation
/// </summary>
public class OpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIConfig _config;
    private readonly ILogger<OpenAIEmbeddingClient> _logger;
    private const string BaseUrl = "https://api.openai.com/v1/embeddings";

    public OpenAIEmbeddingClient(OpenAIConfig config, ILogger<OpenAIEmbeddingClient> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
        
        if (!string.IsNullOrEmpty(config.OrganizationId))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", config.OrganizationId);
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("[OpenAI Embedding] Generating embedding for text ({Length} chars)", text.Length);

            var request = new
            {
                input = text,
                model = _config.EmbeddingModel
            };

            var response = await _httpClient.PostAsJsonAsync(BaseUrl, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);

            if (result?.Data == null || result.Data.Length == 0)
            {
                throw new InvalidOperationException("Empty embedding response from OpenAI");
            }

            var embedding = result.Data[0].Embedding;

            if (embedding == null || embedding.Length == 0)
            {
                throw new InvalidOperationException("Empty embedding vector from OpenAI");
            }

            _logger.LogDebug("[OpenAI Embedding] Generated vector of size {Size}", embedding.Length);

            return embedding;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[OpenAI Embedding] HTTP error generating embedding");
            throw new InvalidOperationException($"OpenAI API error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenAI Embedding] Error generating embedding");
            throw;
        }
    }

    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(
        List<string> texts,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[OpenAI Embedding] Generating {Count} embeddings", texts.Count);

        var embeddings = new List<float[]>();

        try
        {
            // OpenAI supports batch embedding - send all texts at once
            var request = new
            {
                input = texts,
                model = _config.EmbeddingModel
            };

            var response = await _httpClient.PostAsJsonAsync(BaseUrl, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);

            if (result?.Data == null || result.Data.Length == 0)
            {
                throw new InvalidOperationException("Empty batch embedding response from OpenAI");
            }

            // Extract embeddings in order
            foreach (var item in result.Data.OrderBy(x => x.Index))
            {
                if (item.Embedding != null && item.Embedding.Length > 0)
                {
                    embeddings.Add(item.Embedding);
                }
            }

            _logger.LogInformation("[OpenAI Embedding] Batch complete, generated {Count} embeddings", embeddings.Count);

            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenAI Embedding] Error in batch embedding generation");
            throw;
        }
    }

    #region Response Models

    private class EmbeddingResponse
    {
        public string? Object { get; set; }
        public EmbeddingData[]? Data { get; set; }
        public string? Model { get; set; }
        public Usage? Usage { get; set; }
    }

    private class EmbeddingData
    {
        public string? Object { get; set; }
        public float[]? Embedding { get; set; }
        public int Index { get; set; }
    }

    private class Usage
    {
        public int Prompt_Tokens { get; set; }
        public int Total_Tokens { get; set; }
    }

    #endregion
}
