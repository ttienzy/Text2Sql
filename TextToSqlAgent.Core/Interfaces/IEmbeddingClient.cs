namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for embedding client implementations.
/// Supports multiple providers (Gemini, OpenAI, etc.)
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// Generate embedding vector for a single text
    /// </summary>
    /// <param name="text">Text to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Embedding vector as float array</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embedding vectors for multiple texts
    /// </summary>
    /// <param name="texts">List of texts to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of embedding vectors</returns>
    Task<List<float[]>> GenerateBatchEmbeddingsAsync(
        List<string> texts,
        CancellationToken cancellationToken = default);
}
