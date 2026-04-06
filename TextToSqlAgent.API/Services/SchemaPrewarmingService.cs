using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// ✅ P2: Background service that pre-warms schema cache for all connections.
/// Runs every 5 minutes to ensure schemas are loaded and ready.
/// </summary>
public class SchemaPrewarmingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchemaPrewarmingService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public SchemaPrewarmingService(
        IServiceProvider serviceProvider,
        ILogger<SchemaPrewarmingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SchemaPrewarming] Service started");

        try
        {
            // Wait 30 seconds before first run to let application initialize
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PrewarmSchemasAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SchemaPrewarming] Error during schema pre-warming");
                }

                // Wait for next interval
                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when application is shutting down
            _logger.LogInformation("[SchemaPrewarming] Service stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaPrewarming] Unexpected error in service");
        }

        _logger.LogInformation("[SchemaPrewarming] Service stopped");
    }

    private async Task PrewarmSchemasAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var schemaCache = scope.ServiceProvider.GetRequiredService<ISchemaCache>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IConnectionEncryptionService>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        _logger.LogDebug("[SchemaPrewarming] Starting schema pre-warming cycle");

        var connections = await unitOfWork.Connections.GetAllAsync();
        var prewarmCount = 0;
        var skipCount = 0;

        foreach (var connection in connections)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Check if schema is already in cache
                var existingSchema = await schemaCache.GetAsync(connection.Id, ct);
                if (existingSchema != null)
                {
                    skipCount++;
                    continue;
                }

                _logger.LogInformation(
                    "[SchemaPrewarming] Pre-warming schema for connection {ConnectionId} ({ConnectionName})",
                    connection.Id, connection.Name);

                // Decrypt connection string
                var connectionString = encryptionService.DecryptPassword(
                    connection.ConnectionString,
                    connection.Id);

                // Parse provider string to enum (default to SqlServer if invalid)
                if (!Enum.TryParse<DatabaseProvider>(connection.Provider, ignoreCase: true, out var provider))
                {
                    provider = DatabaseProvider.SqlServer;
                }

                // Create temporary database config for this connection
                var tempDbConfig = new DatabaseConfig
                {
                    ConnectionString = connectionString,
                    Provider = provider
                };

                // Create adapter directly (only SqlServer is supported)
                var adapterLogger = loggerFactory.CreateLogger<SqlServerAdapter>();
                var adapter = new SqlServerAdapter(adapterLogger);
                var scannerLogger = loggerFactory.CreateLogger<SchemaScanner>();
                var schemaScanner = new SchemaScanner(tempDbConfig, adapter, scannerLogger);

                // Scan schema
                var schema = await schemaScanner.ScanAsync(ct);

                // Cache it
                await schemaCache.SetAsync(connection.Id, schema, ct);

                prewarmCount++;
                _logger.LogInformation(
                    "[SchemaPrewarming] ✅ Schema loaded for {ConnectionName}: {TableCount} tables",
                    connection.Name, schema.Tables.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SchemaPrewarming] Failed to pre-warm schema for connection {ConnectionId}",
                    connection.Id);
            }
        }

        _logger.LogInformation(
            "[SchemaPrewarming] Cycle complete: {PrewarmCount} schemas loaded, {SkipCount} already cached",
            prewarmCount, skipCount);
    }
}
