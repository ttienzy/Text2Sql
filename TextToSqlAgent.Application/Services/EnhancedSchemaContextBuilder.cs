using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using System.Data;
using Dapper;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Enhanced schema context builder with sample data, implicit relationships, and data distribution hints
/// Removes 10-table limit and provides richer context for LLM
/// </summary>
public class EnhancedSchemaContextBuilder
{
    private readonly ILogger<EnhancedSchemaContextBuilder> _logger;
    private readonly IDatabaseAdapter _adapter;

    public EnhancedSchemaContextBuilder(
        ILogger<EnhancedSchemaContextBuilder> logger,
        IDatabaseAdapter adapter)
    {
        _logger = logger;
        _adapter = adapter;
    }

    /// <summary>
    /// Build comprehensive schema context with all relevant tables (no 10-table limit)
    /// </summary>
    public async Task<string> BuildFullContextAsync(
        DatabaseSchema schema,
        List<string> relevantTables,
        string connectionString,
        bool includeSampleData = true,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[SchemaBuilder] Building full context for {Count} tables", relevantTables.Count);

        var contextParts = new List<string>();

        // 1. All relevant tables with full columns (NO LIMIT)
        foreach (var tableName in relevantTables)
        {
            var table = schema.Tables.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (table == null) continue;

            var tableContext = BuildTableContext(table);
            contextParts.Add(tableContext);

            // 2. Add sample data if requested
            if (includeSampleData)
            {
                try
                {
                    var sampleData = await GetSampleDataAsync(
                        table.TableName,
                        connectionString,
                        ct);

                    if (!string.IsNullOrEmpty(sampleData))
                    {
                        contextParts.Add($"  Sample: {sampleData}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SchemaBuilder] Failed to get sample data for {Table}", tableName);
                }
            }
        }

        // 3. Complete foreign key relationships
        var relationships = schema.Relationships
            .Where(r => relevantTables.Contains(r.FromTable, StringComparer.OrdinalIgnoreCase) ||
                       relevantTables.Contains(r.ToTable, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (relationships.Any())
        {
            contextParts.Add("\nRelationships:");
            foreach (var rel in relationships)
            {
                contextParts.Add($"  {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}");
            }
        }

        // 4. Implicit relationships (detected by naming conventions)
        var implicitRels = DetectImplicitRelationships(schema, relevantTables);
        if (implicitRels.Any())
        {
            contextParts.Add("\nImplicit Relationships (detected by naming):");
            foreach (var rel in implicitRels)
            {
                contextParts.Add($"  {rel}");
            }
        }

        // 5. Data distribution hints
        var distributionHints = await GetDataDistributionHintsAsync(
            relevantTables,
            connectionString,
            ct);

        if (distributionHints.Any())
        {
            contextParts.Add("\nData Distribution:");
            foreach (var hint in distributionHints)
            {
                contextParts.Add($"  {hint}");
            }
        }

        return string.Join("\n", contextParts);
    }

    /// <summary>
    /// Build compact table context with column details
    /// </summary>
    private string BuildTableContext(TableInfo table)
    {
        var columns = table.Columns.Select(c =>
        {
            var constraints = new List<string>();
            if (c.IsPrimaryKey) constraints.Add("PK");
            if (c.IsForeignKey) constraints.Add("FK");
            if (!c.IsNullable) constraints.Add("NOT NULL");

            var constraintStr = constraints.Any() ? $" [{string.Join(", ", constraints)}]" : "";
            return $"{c.ColumnName} {c.DataType}{constraintStr}";
        });

        return $"{table.TableName}:\n  " + string.Join("\n  ", columns);
    }

    /// <summary>
    /// Get sample data from table (first 3 rows)
    /// </summary>
    private async Task<string> GetSampleDataAsync(
        string tableName,
        string connectionString,
        CancellationToken ct)
    {
        try
        {
            using var connection = _adapter.CreateConnection(connectionString);

            // Cast to DbConnection for async operations
            if (connection is System.Data.Common.DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(ct);
            }
            else
            {
                connection.Open();
            }

            // Get first 3 rows with limited columns
            var sql = _adapter.Provider switch
            {
                Core.Enums.DatabaseProvider.SqlServer =>
                    $"SELECT TOP 3 * FROM {_adapter.GetSafeIdentifier(tableName)}",
                Core.Enums.DatabaseProvider.MySql =>
                    $"SELECT * FROM {_adapter.GetSafeIdentifier(tableName)} LIMIT 3",
                Core.Enums.DatabaseProvider.PostgreSql =>
                    $"SELECT * FROM {_adapter.GetSafeIdentifier(tableName)} LIMIT 3",
                _ => $"SELECT TOP 3 * FROM {_adapter.GetSafeIdentifier(tableName)}"
            };

            var rows = await connection.QueryAsync(sql);
            var rowList = rows.ToList();

            if (!rowList.Any())
                return "";

            // Format sample data compactly
            var samples = rowList.Select(row =>
            {
                var dict = (IDictionary<string, object>)row;
                var values = dict.Values
                    .Take(5) // Limit to first 5 columns
                    .Select(v => v?.ToString() ?? "null");
                return $"[{string.Join(", ", values)}]";
            });

            return string.Join(", ", samples);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SchemaBuilder] Failed to get sample data for {Table}", tableName);
            return "";
        }
    }


    /// <summary>
    /// Detect implicit relationships based on naming conventions
    /// </summary>
    private List<string> DetectImplicitRelationships(
        DatabaseSchema schema,
        List<string> relevantTables)
    {
        var implicitRels = new List<string>();

        foreach (var tableName in relevantTables)
        {
            var table = schema.Tables.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (table == null) continue;

            // Look for columns ending with "Id" or "ID"
            var foreignKeyColumns = table.Columns
                .Where(c => c.ColumnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                           c.ColumnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                .Where(c => !c.IsPrimaryKey) // Exclude primary keys
                .ToList();

            foreach (var fkCol in foreignKeyColumns)
            {
                // Try to find referenced table
                // e.g., CustomerId → Customers, CustomerID → Customer
                var possibleTableNames = new[]
                {
                    fkCol.ColumnName.Replace("Id", "").Replace("ID", ""),
                    fkCol.ColumnName.Replace("Id", "s").Replace("ID", "s"),
                    fkCol.ColumnName.Replace("Id", "").Replace("ID", "") + "s"
                };

                foreach (var possibleTable in possibleTableNames)
                {
                    var referencedTable = schema.Tables.FirstOrDefault(t =>
                        t.TableName.Equals(possibleTable, StringComparison.OrdinalIgnoreCase));

                    if (referencedTable != null)
                    {
                        // Check if this is not already a declared FK
                        var existingFk = schema.Relationships.Any(r =>
                            r.FromTable.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
                            r.FromColumn.Equals(fkCol.ColumnName, StringComparison.OrdinalIgnoreCase));

                        if (!existingFk)
                        {
                            implicitRels.Add(
                                $"{tableName}.{fkCol.ColumnName} → {referencedTable.TableName} (inferred)");
                        }
                        break;
                    }
                }
            }
        }

        return implicitRels;
    }

    /// <summary>
    /// Get data distribution hints (row counts, value ranges)
    /// </summary>
    private async Task<List<string>> GetDataDistributionHintsAsync(
        List<string> relevantTables,
        string connectionString,
        CancellationToken ct)
    {
        var hints = new List<string>();

        try
        {
            using var connection = _adapter.CreateConnection(connectionString);

            // Cast to DbConnection for async operations
            if (connection is System.Data.Common.DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(ct);
            }
            else
            {
                connection.Open();
            }

            foreach (var tableName in relevantTables.Take(5)) // Limit to avoid performance issues
            {
                try
                {
                    // Get row count
                    var countSql = $"SELECT COUNT(*) FROM {_adapter.GetSafeIdentifier(tableName)}";
                    var count = await connection.ExecuteScalarAsync<int>(countSql);

                    if (count > 0)
                    {
                        hints.Add($"{tableName}: {count:N0} rows");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SchemaBuilder] Failed to get row count for {Table}", tableName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SchemaBuilder] Failed to get data distribution hints");
        }

        return hints;
    }

    /// <summary>
    /// Build context for a single table with all enhancements
    /// </summary>
    public async Task<string> BuildTableContextAsync(
        string tableName,
        DatabaseSchema schema,
        string connectionString,
        bool includeSampleData = true,
        CancellationToken ct = default)
    {
        return await BuildFullContextAsync(
            schema,
            new List<string> { tableName },
            connectionString,
            includeSampleData,
            ct);
    }
}
