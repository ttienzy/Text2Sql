using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Detects changes between two database schema snapshots
/// </summary>
public class SchemaChangeDetector
{
    private readonly ILogger<SchemaChangeDetector> _logger;

    public SchemaChangeDetector(ILogger<SchemaChangeDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compare two schemas and detect changes
    /// </summary>
    public SchemaChangeReport DetectChanges(
        EnhancedDatabaseSchema oldSchema,
        EnhancedDatabaseSchema newSchema)
    {
        _logger.LogInformation("[SchemaChangeDetector] Comparing schemas...");

        var report = new SchemaChangeReport
        {
            ComparedAt = DateTime.UtcNow,
            OldFingerprint = oldSchema.Fingerprint,
            NewFingerprint = newSchema.Fingerprint
        };

        // Quick check: if fingerprints match, no changes
        if (oldSchema.Fingerprint == newSchema.Fingerprint)
        {
            _logger.LogInformation("[SchemaChangeDetector] No changes detected (fingerprints match)");
            return report;
        }

        var oldTables = oldSchema.EnhancedTables.ToDictionary(t => t.TableName);
        var newTables = newSchema.EnhancedTables.ToDictionary(t => t.TableName);

        // Detect new tables
        foreach (var tableName in newTables.Keys.Except(oldTables.Keys))
        {
            report.NewTables.Add(new TableChange
            {
                TableName = tableName,
                Schema = newTables[tableName].Schema,
                Type = ChangeType.Added,
                NewDescription = null
            });
        }

        // Detect deleted tables
        foreach (var tableName in oldTables.Keys.Except(newTables.Keys))
        {
            report.DeletedTables.Add(new TableChange
            {
                TableName = tableName,
                Schema = oldTables[tableName].Schema,
                Type = ChangeType.Removed,
                OldDescription = null
            });
        }

        // Detect modified tables
        foreach (var tableName in oldTables.Keys.Intersect(newTables.Keys))
        {
            var oldTable = oldTables[tableName];
            var newTable = newTables[tableName];

            var tableChange = DetectTableChanges(oldTable, newTable);
            if (tableChange.ColumnChanges.Count > 0 || tableChange.IndexChanges.Count > 0)
            {
                report.ModifiedTables.Add(tableChange);
            }
        }

        _logger.LogInformation(
            "[SchemaChangeDetector] Changes detected: {New} new, {Deleted} deleted, {Modified} modified",
            report.NewTables.Count, report.DeletedTables.Count, report.ModifiedTables.Count);

        return report;
    }

    /// <summary>
    /// Detect changes within a single table
    /// </summary>
    private TableChange DetectTableChanges(EnhancedTableInfo oldTable, EnhancedTableInfo newTable)
    {
        var change = new TableChange
        {
            TableName = newTable.TableName,
            Schema = newTable.Schema,
            Type = ChangeType.Modified,
            OldDescription = null,
            NewDescription = null
        };

        var oldColumns = oldTable.Columns.ToDictionary(c => c.ColumnName);
        var newColumns = newTable.Columns.ToDictionary(c => c.ColumnName);

        // Detect new columns
        foreach (var columnName in newColumns.Keys.Except(oldColumns.Keys))
        {
            change.ColumnChanges.Add(new ColumnChange
            {
                ColumnName = columnName,
                Type = ChangeType.Added,
                NewDataType = newColumns[columnName].DataType,
                NewIsNullable = newColumns[columnName].IsNullable,
                NewMaxLength = newColumns[columnName].MaxLength
            });
        }

        // Detect deleted columns
        foreach (var columnName in oldColumns.Keys.Except(newColumns.Keys))
        {
            change.ColumnChanges.Add(new ColumnChange
            {
                ColumnName = columnName,
                Type = ChangeType.Removed,
                OldDataType = oldColumns[columnName].DataType,
                OldIsNullable = oldColumns[columnName].IsNullable,
                OldMaxLength = oldColumns[columnName].MaxLength
            });
        }

        // Detect modified columns
        foreach (var columnName in oldColumns.Keys.Intersect(newColumns.Keys))
        {
            var oldCol = oldColumns[columnName];
            var newCol = newColumns[columnName];

            if (oldCol.DataType != newCol.DataType ||
                oldCol.IsNullable != newCol.IsNullable ||
                oldCol.MaxLength != newCol.MaxLength)
            {
                change.ColumnChanges.Add(new ColumnChange
                {
                    ColumnName = columnName,
                    Type = ChangeType.Modified,
                    OldDataType = oldCol.DataType,
                    NewDataType = newCol.DataType,
                    OldIsNullable = oldCol.IsNullable,
                    NewIsNullable = newCol.IsNullable,
                    OldMaxLength = oldCol.MaxLength,
                    NewMaxLength = newCol.MaxLength
                });
            }
        }

        // Detect index changes
        var oldIndexes = oldTable.Indexes.ToDictionary(i => i.IndexName);
        var newIndexes = newTable.Indexes.ToDictionary(i => i.IndexName);

        foreach (var indexName in newIndexes.Keys.Except(oldIndexes.Keys))
        {
            change.IndexChanges.Add(new IndexChange
            {
                IndexName = indexName,
                Type = ChangeType.Added,
                Columns = newIndexes[indexName].Columns
            });
        }

        foreach (var indexName in oldIndexes.Keys.Except(newIndexes.Keys))
        {
            change.IndexChanges.Add(new IndexChange
            {
                IndexName = indexName,
                Type = ChangeType.Removed,
                Columns = oldIndexes[indexName].Columns
            });
        }

        return change;
    }
}
