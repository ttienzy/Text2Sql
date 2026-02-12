namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Configuration for OpenAI API
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// OpenAI API Key (starts with "sk-")
    /// Store securely using User Secrets or Environment Variables
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// OpenAI model to use for chat completion
    /// Examples: "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo"
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OpenAI embedding model to use
    /// Examples: "text-embedding-3-small", "text-embedding-3-large"
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Maximum tokens for completion response
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Temperature (0.0 - 2.0)
    /// Lower = more deterministic, Higher = more creative
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Optional organization ID for OpenAI API
    /// </summary>
    public string? OrganizationId { get; set; }
}
