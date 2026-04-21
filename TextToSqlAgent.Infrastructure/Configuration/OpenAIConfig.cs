namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Configuration for OpenAI API
/// Simple POCO class - no complex logic
/// API key should be set explicitly before use
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// OpenAI API Key (starts with "sk-")
    /// MUST be set explicitly before using the client
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// OpenAI model to use for chat completion
    /// Examples: "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo"
    /// </summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>
    /// Model for reasoning steps in ReAct agent
    /// </summary>
    public string ReasoningModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Model for SQL generation
    /// </summary>
    public string SqlGenerationModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Model for reflection and self-correction
    /// </summary>
    public string ReflectionModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OpenAI embedding model to use
    /// Examples: "text-embedding-3-small", "text-embedding-3-large"
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Embedding output dimensions (Matryoshka representation)
    /// text-embedding-3-small supports up to 1536, but can output 3072 via Matryoshka
    /// Keep at 3072 for zero-migration compatibility with existing Qdrant collection
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 3072;

    /// <summary>
    /// Maximum tokens for completion response
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Temperature (0.0 - 2.0)
    /// Lower = more deterministic, Higher = more creative
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Top P sampling parameter
    /// </summary>
    public double TopP { get; set; } = 0.95;

    /// <summary>
    /// Frequency penalty
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0.0;

    /// <summary>
    /// Presence penalty
    /// </summary>
    public double PresencePenalty { get; set; } = 0.0;

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int RequestTimeout { get; set; } = 120;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Enable caching for responses
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache embeddings
    /// </summary>
    public bool CacheEmbeddings { get; set; } = true;

    /// <summary>
    /// Optional organization ID for OpenAI API
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Base URL for OpenAI API (for custom endpoints)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Validate that configuration is complete and valid
    /// </summary>
    public bool IsValid(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errorMessage = "API key is not set";
            return false;
        }

        if (!ApiKey.StartsWith("sk-"))
        {
            errorMessage = "API key should start with 'sk-'";
            return false;
        }

        if (ApiKey.Length < 20)
        {
            errorMessage = "API key seems too short";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
