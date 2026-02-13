using System.Data.Common;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.Database;

/// <summary>
/// Database schema scanner that uses adapters for database-agnostic schema retrieval
/// </summary>
public class SchemaScanner
{
    private readonly DatabaseConfig _config;
    private readonly IDatabaseAdapter _adapter;
    private readonly ILogger<SchemaScanner> _logger;

    public SchemaScanner(
        DatabaseConfig config,
        IDatabaseAdapter adapter,
        ILogger<SchemaScanner> logger)
    {
        _config = config;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<DatabaseSchema> ScanAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SchemaScanner] Scanning schema using {Provider} adapter...", _adapter.Provider);

        if (string.IsNullOrEmpty(_config.ConnectionString))
        {
            throw new InvalidOperationException("Connection string is not configured");
        }

        try
        {
            using var connection = _adapter.CreateConnection(_config.ConnectionString);
            
            // Cast to DbConnection for OpenAsync support
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }

            var schema = await _adapter.GetSchemaAsync(connection, cancellationToken);

            _logger.LogDebug(
                "[SchemaScanner] Scan complete: {TableCount} tables, {RelationshipCount} relationships",
                schema.Tables.Count,
                schema.Relationships.Count);

            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaScanner] Error scanning schema for provider {Provider}", _adapter.Provider);
            throw;
        }
    }


    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _adapter.TestConnectionAsync(_config.ConnectionString, cancellationToken);
            
            if (success)
            {
                _logger.LogInformation("[SchemaScanner] Connected successfully to {Provider}", _adapter.Provider);
            }
            else
            {
                _logger.LogError("[SchemaScanner] Cannot connect to {Provider} database", _adapter.Provider);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaScanner] Connection test failed for {Provider}", _adapter.Provider);
            return false;
        }
    }
}
