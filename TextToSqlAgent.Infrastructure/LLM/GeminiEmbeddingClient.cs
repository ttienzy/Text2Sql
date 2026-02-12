using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.LLM;

public class GeminiEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiConfig _config;
    private readonly ILogger<GeminiEmbeddingClient> _logger;
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiEmbeddingClient(GeminiConfig config, ILogger<GeminiEmbeddingClient> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("[Gemini Embedding] Generating embedding for text ({Length} chars)", text.Length);

            var url = $"{BaseUrl}/{_config.EmbeddingModel}:embedContent?key={_config.ApiKey}";
            _logger.LogDebug("[Gemini Embedding] URL: {Url}", url.Replace(_config.ApiKey, "***API_KEY***"));

            var request = new
            {
                content = new
                {
                    parts = new[]
                    {
                        new { text = text }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);

            if (result?.Embedding?.Values == null)
            {
                throw new InvalidOperationException("Empty embedding response");
            }

            _logger.LogDebug("[Gemini Embedding] Generated vector of size {Size}", result.Embedding.Values.Length);

            return result.Embedding.Values;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini Embedding] Error generating embedding");
            throw;
        }
    }

    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(
        List<string> texts,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Gemini Embedding] Generating {Count} embeddings", texts.Count);

        var embeddings = new List<float[]>();

        // Gemini API doesn't support batch, so we call one by one
        // Add delay to respect rate limits
        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            embeddings.Add(embedding);

            // Rate limiting: ~120 requests per minute (safe margin for 60 RPM limit)
            await Task.Delay(500, cancellationToken);
        }

        _logger.LogInformation("[Gemini Embedding] Batch complete");

        return embeddings;
    }

    private class EmbeddingResponse
    {
        public EmbeddingData? Embedding { get; set; }
    }

    private class EmbeddingData
    {
        public float[]? Values { get; set; }
    }
}