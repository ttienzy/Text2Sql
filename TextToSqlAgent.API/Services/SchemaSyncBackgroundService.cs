using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Background service that periodically checks for schema changes
/// and automatically re-indexes when changes are detected
/// </summary>
public class SchemaSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchemaSyncBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;
    private string? _lastSchemaHash;

    public SchemaSyncBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<SchemaSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _checkInterval = TimeSpan.FromMinutes(30); // ✅ SMALL-6: Sync every 30 minutes
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[SchemaSync] Background service started (check interval: {Minutes} minutes)",
            _checkInterval.TotalMinutes);

        try
        {
            // Initial delay to let application start up
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndSyncSchemaAsync(stoppingToken);
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Expected during shutdown - not an error
                    _logger.LogInformation("[SchemaSync] Background service stopping gracefully");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SchemaSync] Error in sync loop, will retry in 1 minute");
                    // Continue running despite errors
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
            _logger.LogInformation("[SchemaSync] Background service cancelled during startup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaSync] Unexpected error in background service");
        }

        _logger.LogInformation("[SchemaSync] Background service stopped");
    }

    private async Task CheckAndSyncSchemaAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        try
        {
            // ⚠️ IMPORTANT: SchemaScanner requires a specific connection
            // Background service cannot scan without knowing which connection to use
            // This is a design limitation - schema sync should be per-connection, not global

            _logger.LogWarning(
                "[SchemaSync] ⚠️  Schema auto-sync is disabled due to design limitation. " +
                "SchemaScanner requires a specific connection ID, but background service doesn't know which connection to scan. " +
                "Schema sync should be triggered per-connection when users interact with the system.");

            // TODO: Refactor to support per-connection schema sync
            // Options:
            // 1. Scan all active connections from database
            // 2. Use webhook-based schema change detection
            // 3. Trigger sync on first query per connection

            return;

            /* COMMENTED OUT - Original implementation has design flaw
            var scanner = scope.ServiceProvider.GetRequiredService<SchemaScanner>();
            var indexer = scope.ServiceProvider.GetRequiredService<SchemaIndexer>();

            _logger.LogDebug("[SchemaSync] Checking for schema changes...");

            // Scan current schema
            var currentSchema = await scanner.ScanAsync(cancellationToken);
            var currentHash = ComputeSchemaHash(currentSchema);

            // First run - just store the hash
            if (_lastSchemaHash == null)
            {
                _lastSchemaHash = currentHash;
                _logger.LogInformation("[SchemaSync] Initial schema hash stored");
                return;
            }

            // Check if schema changed
            if (currentHash != _lastSchemaHash)
            {
                _logger.LogWarning(
                    "[SchemaSync] ⚠️  Schema change detected! Re-indexing... (Tables: {Count})",
                    currentSchema.Tables.Count);

                // Re-index schema
                var fingerprint = CreateFingerprintFromHash(currentSchema, currentHash);
                await indexer.IndexSchemaAsync(currentSchema, fingerprint, cancellationToken);

                // Update hash
                _lastSchemaHash = currentHash;

                _logger.LogInformation("[SchemaSync] ✓ Re-indexing complete");
            }
            else
            {
                _logger.LogDebug("[SchemaSync] No schema changes detected");
            }
            */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaSync] Error checking schema");
            throw;
        }
    }

    private static string ComputeSchemaHash(DatabaseSchema schema)
    {
        // Serialize schema to JSON
        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        // Compute SHA256 hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hashBytes);
    }

    private static SchemaFingerprint CreateFingerprintFromHash(DatabaseSchema schema, string hash)
    {
        return new SchemaFingerprint
        {
            Hash = hash,
            ComputedAt = DateTime.UtcNow,
            TableCount = schema.Tables.Count,
            ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
            RelationshipCount = schema.Relationships.Count,
            TableNames = schema.Tables.Select(t => t.TableName).OrderBy(n => n).ToList()
        };
    }
}
