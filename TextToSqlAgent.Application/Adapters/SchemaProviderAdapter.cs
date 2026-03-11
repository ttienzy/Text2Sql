namespace TextToSqlAgent.Application.Adapters;

using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Infrastructure.Database;

/// <summary>
/// Adapter: Wraps SchemaScanner with caching
/// </summary>
public class CachedSchemaProvider : ISchemaProvider
{
    private readonly SchemaScanner _scanner;
    private readonly ILogger<CachedSchemaProvider> _logger;
    private DatabaseSchema? _cachedSchema;

    public CachedSchemaProvider(
        SchemaScanner scanner,
        ILogger<CachedSchemaProvider> logger)
    {
        _scanner = scanner;
        _logger = logger;
    }

    public async Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default)
    {
        if (_cachedSchema != null)
        {
            _logger.LogDebug("[SchemaProvider] Using cached schema");
            return _cachedSchema;
        }

        _logger.LogInformation("[SchemaProvider] Scanning database schema...");
        _cachedSchema = await _scanner.ScanAsync(ct);
        _logger.LogInformation(
            "[SchemaProvider] Schema loaded: {Tables} tables, {Rels} relationships",
            _cachedSchema.Tables.Count,
            _cachedSchema.Relationships.Count);

        return _cachedSchema;
    }

    public void ClearCache()
    {
        _cachedSchema = null;
        _logger.LogInformation("[SchemaProvider] Cache cleared");
    }
}
