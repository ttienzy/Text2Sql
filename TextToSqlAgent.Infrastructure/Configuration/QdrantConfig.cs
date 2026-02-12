namespace TextToSqlAgent.Infrastructure.Configuration;

public class QdrantConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334; // gRPC port (6333 is REST API)
    public string ApiKey { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "schema_embeddings";
    public bool UseGrpc { get; set; } = true;
    public int VectorSize { get; set; } = 768;
}