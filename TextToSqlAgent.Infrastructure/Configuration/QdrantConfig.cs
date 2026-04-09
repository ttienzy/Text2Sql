namespace TextToSqlAgent.Infrastructure.Configuration;

public class QdrantConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334; // gRPC port (6333 is REST API)
    public string ApiKey { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "schema_embeddings";
    public bool UseGrpc { get; set; } = true;
    // ✅ TD-9: Default matches text-embedding-3-large (3072) used in appsettings.json
    public int VectorSize { get; set; } = 3072;

    /// <summary>
    /// OpenAI embedding model to use for generating embeddings (default: text-embedding-3-small).
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}