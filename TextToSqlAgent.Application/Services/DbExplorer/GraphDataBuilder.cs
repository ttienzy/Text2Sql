using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Builds graph data for ER diagram visualization
/// </summary>
public class GraphDataBuilder
{
    private readonly ILogger<GraphDataBuilder> _logger;

    public GraphDataBuilder(ILogger<GraphDataBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build graph data from enhanced schema
    /// </summary>
    public GraphData BuildGraph(EnhancedDatabaseSchema schema)
    {
        _logger.LogInformation("[GraphDataBuilder] Building graph for {Tables} tables...",
            schema.EnhancedTables.Count);

        var graph = new GraphData();

        // Build nodes
        foreach (var table in schema.EnhancedTables)
        {
            graph.Nodes.Add(new GraphNode
            {
                Id = table.TableName,
                Label = table.TableName,
                Role = table.Role ?? TableRole.Unknown,
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Module = table.Module,
                PrimaryKeys = table.PrimaryKeys,
                ForeignKeys = table.ForeignKeys,
                Columns = table.Columns.Select(c => new GraphColumn
                {
                    Name = c.ColumnName,
                    Type = c.DataType + (c.MaxLength.HasValue ? $"({c.MaxLength})" : ""),
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey,
                    IsNullable = c.IsNullable
                }).ToList()
            });
        }

        // Build edges
        var edgeId = 0;
        foreach (var rel in schema.BaseSchema.Relationships)
        {
            var edge = new GraphEdge
            {
                Id = $"edge_{edgeId++}",
                Source = rel.FromTable,
                Target = rel.ToTable,
                Via = rel.FromColumn,
                Type = DetermineRelationshipType(rel, schema),
                Strength = DetermineRelationshipStrength(rel, schema)
            };

            graph.Edges.Add(edge);
        }

        _logger.LogInformation("[GraphDataBuilder] ✅ Graph built: {Nodes} nodes, {Edges} edges",
            graph.Nodes.Count, graph.Edges.Count);

        return graph;
    }

    private RelationshipType DetermineRelationshipType(
        Core.Models.RelationshipInfo rel,
        EnhancedDatabaseSchema schema)
    {
        // Check if it's a bridge table (many-to-many)
        var fromTable = schema.EnhancedTables.FirstOrDefault(t => t.TableName == rel.FromTable);

        if (fromTable != null && fromTable.Role == TableRole.Bridge)
        {
            return RelationshipType.ManyToMany;
        }

        // Check if FK is unique (one-to-one)
        var fromColumn = fromTable?.Columns.FirstOrDefault(c => c.ColumnName == rel.FromColumn);
        if (fromColumn != null)
        {
            var hasUniqueIndex = fromTable?.Indexes.Any(i =>
                i.IsUnique && i.Columns.Count == 1 && i.Columns[0] == rel.FromColumn) ?? false;

            if (hasUniqueIndex)
            {
                return RelationshipType.OneToOne;
            }
        }

        // Default: many-to-one
        return RelationshipType.ManyToOne;
    }

    private RelationshipStrength DetermineRelationshipStrength(
        Core.Models.RelationshipInfo rel,
        EnhancedDatabaseSchema schema)
    {
        var fromTable = schema.EnhancedTables.FirstOrDefault(t => t.TableName == rel.FromTable);
        var fromColumn = fromTable?.Columns.FirstOrDefault(c => c.ColumnName == rel.FromColumn);

        if (fromColumn == null)
        {
            return RelationshipStrength.Loose;
        }

        // NOT NULL FK = Tight or Moderate
        if (!fromColumn.IsNullable)
        {
            // TODO: Check for cascade delete (requires additional metadata)
            // For now, assume Moderate for NOT NULL
            return RelationshipStrength.Moderate;
        }

        // Nullable FK = Loose
        return RelationshipStrength.Loose;
    }
}
