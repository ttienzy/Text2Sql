namespace TextToSqlAgent.Infrastructure.Configuration;

public class RAGConfig
{
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Minimum cosine similarity score for RAG results.
    /// Lower values = more results (better recall), higher values = more precise.
    /// Typical range: 0.3 (broad) to 0.7 (strict).
    /// </summary>
    public double MinimumScore { get; set; } = 0.3;

    public bool EnableHybridSearch { get; set; } = false;
    public int MaxContextTables { get; set; } = 10;
}