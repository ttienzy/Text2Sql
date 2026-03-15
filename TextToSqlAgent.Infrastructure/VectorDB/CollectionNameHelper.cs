using System.Text;

namespace TextToSqlAgent.Infrastructure.VectorDB;

/// <summary>
/// Provides consistent collection name generation across all components.
/// Ensures that all components (AgentOrchestrator, SchemaIndexer, SchemaRetriever, QdrantService)
/// generate identical collection names for the same database.
/// </summary>
public static class CollectionNameHelper
{
    private const string CollectionPrefix = "schema_embeddings";

    /// <summary>
    /// Normalizes a database name and generates a consistent collection name.
    /// Format: "schema_embeddings_{normalized_database_name}"
    /// Normalization: converts to lowercase and replaces special characters with underscores.
    /// </summary>
    /// <param name="databaseName">The database name to normalize</param>
    /// <returns>A normalized collection name</returns>
    public static string NormalizeCollectionName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be null or whitespace", nameof(databaseName));
        }

        // Normalize by converting to lowercase and replacing special chars with underscores
        var normalized = NormalizeDatabaseName(databaseName);

        return $"{CollectionPrefix}_{normalized}";
    }

    /// <summary>
    /// Normalizes a database name by converting to lowercase and replacing special characters with underscores.
    /// </summary>
    /// <param name="databaseName">The database name to normalize</param>
    /// <returns>A normalized database name</returns>
    private static string NormalizeDatabaseName(string databaseName)
    {
        var sb = new StringBuilder();

        foreach (var c in databaseName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                // Replace special characters with underscores
                sb.Append('_');
            }
        }

        // Remove consecutive underscores
        var result = sb.ToString();
        while (result.Contains("__"))
        {
            result = result.Replace("__", "_");
        }

        // Trim leading/trailing underscores
        return result.Trim('_');
    }
}
