using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Infers relationships and JOIN paths between tables
/// </summary>
public class RelationshipInference
{
    private readonly ILogger<RelationshipInference> _logger;

    public RelationshipInference(ILogger<RelationshipInference> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find shortest JOIN path between tables
    /// </summary>
    public List<RelationshipInfo> FindJoinPath(
        List<string> tableNames,
        DatabaseSchema fullSchema)
    {
        _logger.LogDebug("[RelationshipInference] Finding JOIN path for {Count} tables", tableNames.Count);

        if (tableNames.Count <= 1)
            return new List<RelationshipInfo>();

        var allRelationships = new List<RelationshipInfo>();

        // For each pair of tables, find direct or indirect relationships
        for (int i = 0; i < tableNames.Count - 1; i++)
        {
            for (int j = i + 1; j < tableNames.Count; j++)
            {
                var path = FindShortestPath(tableNames[i], tableNames[j], fullSchema);
                allRelationships.AddRange(path);
            }
        }

        // Remove duplicates
        var uniqueRelationships = allRelationships
            .GroupBy(r => $"{r.FromTable}.{r.FromColumn}->{r.ToTable}.{r.ToColumn}")
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation("[RelationshipInference] Found {Count} relationships", uniqueRelationships.Count);

        return uniqueRelationships;
    }

    /// <summary>
    /// Find shortest path between two tables using BFS
    /// </summary>
    private List<RelationshipInfo> FindShortestPath(
        string fromTable,
        string toTable,
        DatabaseSchema schema)
    {
        // Build adjacency list
        var graph = BuildGraph(schema);

        // BFS to find shortest path
        var queue = new Queue<(string Table, List<RelationshipInfo> Path)>();
        var visited = new HashSet<string>();

        queue.Enqueue((fromTable, new List<RelationshipInfo>()));
        visited.Add(fromTable);

        while (queue.Count > 0)
        {
            var (currentTable, path) = queue.Dequeue();

            if (currentTable.Equals(toTable, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[RelationshipInference] Found path: {From} -> {To} ({Hops} hops)",
                    fromTable, toTable, path.Count);
                return path;
            }

            // Explore neighbors
            if (graph.ContainsKey(currentTable))
            {
                foreach (var (neighbor, relationship) in graph[currentTable])
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        var newPath = new List<RelationshipInfo>(path) { relationship };
                        queue.Enqueue((neighbor, newPath));
                    }
                }
            }
        }

        _logger.LogWarning("[RelationshipInference] No path found between {From} and {To}", fromTable, toTable);
        return new List<RelationshipInfo>();
    }

    /// <summary>
    /// Build graph from relationships
    /// </summary>
    private Dictionary<string, List<(string Neighbor, RelationshipInfo Relationship)>> BuildGraph(
        DatabaseSchema schema)
    {
        var graph = new Dictionary<string, List<(string, RelationshipInfo)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rel in schema.Relationships)
        {
            var fromTable = ExtractTableName(rel.FromTable);
            var toTable = ExtractTableName(rel.ToTable);

            // Add bidirectional edges
            if (!graph.ContainsKey(fromTable))
                graph[fromTable] = new List<(string, RelationshipInfo)>();

            if (!graph.ContainsKey(toTable))
                graph[toTable] = new List<(string, RelationshipInfo)>();

            graph[fromTable].Add((toTable, rel));
            graph[toTable].Add((fromTable, rel));
        }

        return graph;
    }

    /// <summary>
    /// Extract table name from qualified name (schema.table)
    /// </summary>
    private string ExtractTableName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    /// <summary>
    /// Add intermediate tables if needed for JOIN path
    /// </summary>
    public List<TableInfo> AddIntermediateTables(
        List<string> selectedTables,
        List<RelationshipInfo> relationships,
        DatabaseSchema fullSchema)
    {
        var allTables = new HashSet<string>(selectedTables, StringComparer.OrdinalIgnoreCase);

        // Add tables from relationships
        foreach (var rel in relationships)
        {
            allTables.Add(ExtractTableName(rel.FromTable));
            allTables.Add(ExtractTableName(rel.ToTable));
        }

        // Get full table info
        var tableInfos = fullSchema.Tables
            .Where(t => allTables.Contains(t.TableName))
            .ToList();

        _logger.LogInformation("[RelationshipInference] Total tables including intermediates: {Count}",
            tableInfos.Count);

        return tableInfos;
    }
}
