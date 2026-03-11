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
        _checkInterval = TimeSpan.FromMinutes(5); // Check every 5 minutes
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[SchemaSync] Background service started (check interval: {Minutes} minutes)",
            _checkInterval.TotalMinutes);

        // Initial delay to let application start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSyncSchemaAsync(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[SchemaSync] Background service stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaSync] Error in sync loop");
                // Continue running despite errors
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("[SchemaSync] Background service stopped");
    }

    private async Task CheckAndSyncSchemaAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<SchemaScanner>();
        var indexer = scope.ServiceProvider.GetRequiredService<SchemaIndexer>();

        try
        {
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
                await indexer.IndexSchemaAsync(currentSchema, cancellationToken);

                // Update hash
                _lastSchemaHash = currentHash;

                _logger.LogInformation("[SchemaSync] ✓ Re-indexing complete");
            }
            else
            {
                _logger.LogDebug("[SchemaSync] No schema changes detected");
            }
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
}
