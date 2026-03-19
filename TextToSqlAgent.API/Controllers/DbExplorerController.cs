using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services.DbExplorer;
using TextToSqlAgent.Core.Models.DbExplorer;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Database Explorer API
/// Provides schema analysis, visualization, and health checks
/// </summary>
[ApiController]
[Route("api/db-explorer")]
[Authorize]
public class DbExplorerController : BaseController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly EnhancedSchemaScanner _schemaScanner;
    private readonly DatabaseAnalyzer _analyzer;
    private readonly GraphDataBuilder _graphBuilder;
    private readonly QuerySuggestionService _suggestionService;
    private readonly DbExplorerCacheService _cache;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly IConnectionEncryptionService _encryptionService;
    private new readonly ILogger<DbExplorerController> _logger;

    public DbExplorerController(
        IUnitOfWork unitOfWork,
        EnhancedSchemaScanner schemaScanner,
        DatabaseAnalyzer analyzer,
        GraphDataBuilder graphBuilder,
        QuerySuggestionService suggestionService,
        DbExplorerCacheService cache,
        IVectorSearchService vectorSearchService,
        IConnectionEncryptionService encryptionService,
        ILogger<DbExplorerController> logger) : base(logger)
    {
        _unitOfWork = unitOfWork;
        _schemaScanner = schemaScanner;
        _analyzer = analyzer;
        _graphBuilder = graphBuilder;
        _suggestionService = suggestionService;
        _cache = cache;
        _vectorSearchService = vectorSearchService;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Validate connection access and return connection
    /// </summary>
    private async Task<(Connection? connection, IActionResult? errorResult)> ValidateConnectionAccessAsync(string connectionId)
    {
        var connection = await _unitOfWork.Connections.GetByIdAsync(connectionId);
        if (connection == null)
        {
            return (null, NotFound(new { error = "Connection not found" }));
        }

        var userId = GetRequiredUserId();
        if (connection.UserId != userId)
        {
            return (null, Forbid());
        }

        return (connection, null);
    }

    /// <summary>
    /// Get cache status for a connection
    /// </summary>
    [HttpGet("{connectionId}/status")]
    public async Task<IActionResult> GetStatus(string connectionId)
    {
        try
        {
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            var schema = _cache.GetCachedSchema(connectionId);
            var analysis = _cache.GetCachedAnalysis(connectionId);
            var graph = _cache.GetCachedGraph(connectionId);

            // Check Qdrant for existing embeddings
            var hasQdrantData = false;
            var qdrantPointCount = 0;
            try
            {
                var connectionString = _encryptionService.DecryptPassword(connection!.ConnectionString, connection.Id);
                var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);

                if (!string.IsNullOrEmpty(databaseName))
                {
                    var qdrantService = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
                    qdrantService.SetCollectionName(databaseName);
                    var collectionExists = await qdrantService.CollectionExistsAsync();

                    if (collectionExists)
                    {
                        var collectionInfo = await qdrantService.GetCollectionInfoAsync();
                        qdrantPointCount = (int)(collectionInfo?.PointsCount ?? 0);
                        hasQdrantData = qdrantPointCount > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DbExplorer] Failed to check Qdrant status for {ConnectionId}", connectionId);
            }

            return Ok(new
            {
                hasData = schema != null && analysis != null,
                schemaAvailable = schema != null,
                analysisAvailable = analysis != null,
                graphAvailable = graph != null,
                hasQdrantData,
                qdrantPointCount,
                scannedAt = schema?.ScannedAt,
                tableCount = schema?.EnhancedTables.Count ?? 0,
                issueCount = analysis?.HealthIssues.Count ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting status for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get status", details = ex.Message });
        }
    }

    private string? ExtractDatabaseNameFromConnectionString(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return builder.InitialCatalog;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Analyze database schema (trigger full crawl + AI analysis)
    /// </summary>
    [HttpPost("{connectionId}/analyze")]
    public async Task<IActionResult> AnalyzeDatabase(
        string connectionId,
        [FromQuery] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DbExplorer] Analyzing database for connection {ConnectionId}", connectionId);

            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Check cache
            if (!forceRefresh)
            {
                var cachedSchema = _cache.GetCachedSchema(connectionId);
                var cachedAnalysis = _cache.GetCachedAnalysis(connectionId);

                if (cachedSchema != null && cachedAnalysis != null)
                {
                    _logger.LogInformation("[DbExplorer] Returning cached analysis for {ConnectionId}", connectionId);
                    return Ok(new { message = "Using cached analysis", cached = true });
                }
            }

            // Check if Qdrant has embeddings - if yes, we can use them for faster analysis
            var hasQdrantData = false;
            try
            {
                var connectionString = _encryptionService.DecryptPassword(connection!.ConnectionString, connection.Id);
                var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);

                if (!string.IsNullOrEmpty(databaseName))
                {
                    var qdrantService = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
                    qdrantService.SetCollectionName(databaseName);
                    var collectionExists = await qdrantService.CollectionExistsAsync();

                    if (collectionExists)
                    {
                        var collectionInfo = await qdrantService.GetCollectionInfoAsync();
                        hasQdrantData = (collectionInfo?.PointsCount ?? 0) > 0;

                        if (hasQdrantData)
                        {
                            _logger.LogInformation("[DbExplorer] ✅ Found {Count} embeddings in Qdrant - can leverage for faster analysis",
                                collectionInfo?.PointsCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DbExplorer] Failed to check Qdrant, proceeding with standard analysis");
            }

            // Decrypt connection string for schema scanning
            var decryptedConnectionString = _encryptionService.DecryptPassword(connection!.ConnectionString, connection.Id);

            if (string.IsNullOrEmpty(decryptedConnectionString))
            {
                return BadRequest(new { error = "Failed to decrypt connection string" });
            }

            // Scan schema with statistics using decrypted connection string
            var schema = await _schemaScanner.ScanWithStatisticsAsync(
                decryptedConnectionString,
                includeStatistics: true,
                cancellationToken);

            // Check if fingerprint changed
            if (_cache.ShouldInvalidate(connectionId, schema.Fingerprint))
            {
                _cache.InvalidateCache(connectionId);
            }

            // Cache schema
            _cache.CacheSchema(connectionId, schema);

            // Run AI analysis (will be faster if Qdrant data exists)
            var analysis = await _analyzer.AnalyzeAsync(schema, cancellationToken);

            // Cache analysis
            _cache.CacheAnalysis(connectionId, analysis);

            // Build and cache graph
            var graph = _graphBuilder.BuildGraph(schema);
            _cache.CacheGraph(connectionId, graph);

            _logger.LogInformation(
                "[DbExplorer] ✅ Analysis complete for {ConnectionId}: {Tables} tables, {Issues} issues, Qdrant: {HasQdrant}",
                connectionId, schema.EnhancedTables.Count, analysis.HealthIssues.Count, hasQdrantData);

            return Ok(new
            {
                message = "Analysis complete",
                tables = schema.EnhancedTables.Count,
                issues = analysis.HealthIssues.Count,
                domain = analysis.Domain,
                cached = false,
                usedQdrant = hasQdrantData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error analyzing database for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to analyze database", details = ex.Message });
        }
    }

    /// <summary>
    /// Get database overview (summary card data)
    /// </summary>
    [HttpGet("{connectionId}/overview")]
    public async Task<IActionResult> GetOverview(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached data
            var schema = _cache.GetCachedSchema(connectionId);
            var analysis = _cache.GetCachedAnalysis(connectionId);

            if (schema == null || analysis == null)
            {
                return NotFound(new
                {
                    error = "No analysis data available",
                    message = "Please run analysis first by calling POST /api/db-explorer/{connectionId}/analyze"
                });
            }

            // Build response
            var response = new DatabaseOverviewResponse
            {
                Domain = analysis.Domain,
                Summary = analysis.Summary,
                TableCount = schema.EnhancedTables.Count,
                ColumnCount = schema.EnhancedTables.Sum(t => t.ColumnCount),
                TotalRows = schema.EnhancedTables.Sum(t => t.RowCount),
                Modules = analysis.Modules.Select(m => new ModuleDto
                {
                    Name = m.Name,
                    Description = m.Description,
                    Tables = m.Tables
                }).ToList(),
                IssueCount = analysis.HealthIssues.Count,
                ScannedAt = schema.ScannedAt,
                Confidence = analysis.Confidence
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting overview for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get overview", details = ex.Message });
        }
    }

    /// <summary>
    /// Get table list with filtering
    /// </summary>
    [HttpGet("{connectionId}/tables")]
    public async Task<IActionResult> GetTables(
        string connectionId,
        [FromQuery] string? role = null,
        [FromQuery] string? module = null,
        [FromQuery] string? search = null)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached data
            var schema = _cache.GetCachedSchema(connectionId);
            var analysis = _cache.GetCachedAnalysis(connectionId);

            if (schema == null)
            {
                return NotFound(new { error = "No schema data available" });
            }

            // Filter tables
            var tables = schema.EnhancedTables.AsEnumerable();

            if (!string.IsNullOrEmpty(role) && Enum.TryParse<TableRole>(role, true, out var roleEnum))
            {
                tables = tables.Where(t => t.Role == roleEnum);
            }

            if (!string.IsNullOrEmpty(module))
            {
                tables = tables.Where(t => t.Module == module);
            }

            if (!string.IsNullOrEmpty(search))
            {
                tables = tables.Where(t => t.TableName.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            // Build response
            var response = new TableListResponse
            {
                Tables = tables.Select(t =>
                {
                    var roleInfo = analysis?.TableRoles.GetValueOrDefault(t.TableName);
                    return new TableSummaryDto
                    {
                        TableName = t.TableName,
                        Schema = t.Schema,
                        Role = t.Role ?? TableRole.Unknown,
                        Module = t.Module,
                        RowCount = t.RowCount,
                        ColumnCount = t.ColumnCount,
                        ForeignKeyCount = t.ForeignKeys.Count,
                        Description = roleInfo?.Description ?? ""
                    };
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting tables for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get tables", details = ex.Message });
        }
    }

    /// <summary>
    /// Get table detail
    /// </summary>
    [HttpGet("{connectionId}/tables/{tableName}")]
    public async Task<IActionResult> GetTableDetail(string connectionId, string tableName)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached data
            var schema = _cache.GetCachedSchema(connectionId);
            var analysis = _cache.GetCachedAnalysis(connectionId);

            if (schema == null)
            {
                return NotFound(new { error = "No schema data available" });
            }

            // Find table
            var table = schema.EnhancedTables.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (table == null)
            {
                return NotFound(new { error = $"Table '{tableName}' not found" });
            }

            // Get role info
            var roleInfo = analysis?.TableRoles.GetValueOrDefault(table.TableName);

            // Build relationships
            var relationships = new List<RelationshipDto>();

            // Outgoing relationships (this table → other tables)
            foreach (var rel in schema.BaseSchema.Relationships.Where(r => r.FromTable == tableName))
            {
                relationships.Add(new RelationshipDto
                {
                    Direction = "outgoing",
                    RelatedTable = rel.ToTable,
                    ViaColumn = rel.FromColumn,
                    Type = "references"
                });
            }

            // Incoming relationships (other tables → this table)
            foreach (var rel in schema.BaseSchema.Relationships.Where(r => r.ToTable == tableName))
            {
                relationships.Add(new RelationshipDto
                {
                    Direction = "incoming",
                    RelatedTable = rel.FromTable,
                    ViaColumn = rel.ToColumn,
                    Type = "referenced_by"
                });
            }

            // Build response
            var response = new TableDetailResponse
            {
                TableName = table.TableName,
                Schema = table.Schema,
                Role = table.Role ?? TableRole.Unknown,
                Module = table.Module,
                RowCount = table.RowCount,
                Description = roleInfo?.Description ?? "",
                Columns = table.Columns.Select(c => new ColumnDetailDto
                {
                    ColumnName = c.ColumnName,
                    DataType = c.DataType,
                    IsNullable = c.IsNullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey,
                    MaxLength = c.MaxLength,
                    Statistics = table.ColumnStats.TryGetValue(c.ColumnName, out var stats)
                        ? new ColumnStatsDto
                        {
                            NullRate = stats.NullRate,
                            DistinctCount = stats.DistinctCount,
                            MinValue = stats.MinValue,
                            MaxValue = stats.MaxValue,
                            AvgValue = stats.AvgValue
                        }
                        : null
                }).ToList(),
                Relationships = relationships,
                Indexes = table.Indexes.Select(i => new IndexDto
                {
                    IndexName = i.IndexName,
                    Columns = i.Columns,
                    IsUnique = i.IsUnique,
                    IsPrimaryKey = i.IsPrimaryKey
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting table detail for {Table}", tableName);
            return StatusCode(500, new { error = "Failed to get table detail", details = ex.Message });
        }
    }

    /// <summary>
    /// Get health check report
    /// </summary>
    [HttpGet("{connectionId}/health")]
    public async Task<IActionResult> GetHealthCheck(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached analysis
            var analysis = _cache.GetCachedAnalysis(connectionId);

            if (analysis == null)
            {
                return NotFound(new { error = "No analysis data available" });
            }

            // Build response
            var response = new HealthCheckResponse
            {
                TotalIssues = analysis.HealthIssues.Count,
                CriticalCount = analysis.HealthIssues.Count(i => i.Severity == IssueSeverity.Critical),
                WarningCount = analysis.HealthIssues.Count(i => i.Severity == IssueSeverity.Warning),
                InfoCount = analysis.HealthIssues.Count(i => i.Severity == IssueSeverity.Info),
                Issues = analysis.HealthIssues.Select(i => new HealthIssueDto
                {
                    Severity = i.Severity.ToString().ToLower(),
                    Type = i.Type.ToString().ToLower(),
                    Table = i.Table,
                    Column = i.Column,
                    Description = i.Description,
                    Recommendation = i.Recommendation
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting health check for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get health check", details = ex.Message });
        }
    }

    /// <summary>
    /// Get graph data for ER diagram
    /// </summary>
    [HttpGet("{connectionId}/graph")]
    public async Task<IActionResult> GetGraph(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached graph
            var graph = _cache.GetCachedGraph(connectionId);

            if (graph == null)
            {
                return NotFound(new { error = "No graph data available" });
            }

            // Build response
            var response = new GraphDataResponse
            {
                Nodes = graph.Nodes.Select(n => new GraphNodeDto
                {
                    Id = n.Id,
                    Label = n.Label,
                    Role = n.Role.ToString().ToLower(),
                    RowCount = n.RowCount,
                    ColumnCount = n.ColumnCount,
                    Module = n.Module,
                    PrimaryKeys = n.PrimaryKeys,
                    ForeignKeys = n.ForeignKeys,
                    Columns = n.Columns.Select(c => new GraphColumnDto
                    {
                        Name = c.Name,
                        Type = c.Type,
                        IsPrimaryKey = c.IsPrimaryKey,
                        IsForeignKey = c.IsForeignKey,
                        IsNullable = c.IsNullable
                    }).ToList()
                }).ToList(),
                Edges = graph.Edges.Select(e => new GraphEdgeDto
                {
                    Id = e.Id,
                    Source = e.Source,
                    Target = e.Target,
                    Via = e.Via,
                    Type = e.Type.ToString().ToLower(),
                    Strength = e.Strength.ToString().ToLower()
                }).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting graph for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get graph", details = ex.Message });
        }
    }

    /// <summary>
    /// Invalidate cache and force refresh
    /// </summary>
    [HttpDelete("{connectionId}/cache")]
    public async Task<IActionResult> InvalidateCache(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            _cache.InvalidateCache(connectionId);

            return Ok(new { message = "Cache invalidated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error invalidating cache for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to invalidate cache", details = ex.Message });
        }
    }

    /// <summary>
    /// Get sample data from a table (top 5 rows)
    /// </summary>
    [HttpGet("{connectionId}/tables/{tableName}/sample")]
    public async Task<IActionResult> GetSampleData(string connectionId, string tableName)
    {
        try
        {
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            var decryptedConnectionString = _encryptionService.DecryptPassword(
                connection!.ConnectionString,
                connection.Id);

            if (string.IsNullOrEmpty(decryptedConnectionString))
            {
                return BadRequest(new { error = "Failed to decrypt connection string" });
            }

            // Get schema to validate table exists
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return BadRequest(new { error = "Schema not cached. Please analyze database first." });
            }

            var table = schema.EnhancedTables.FirstOrDefault(t => t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                return NotFound(new { error = $"Table '{tableName}' not found" });
            }

            // Execute sample query
            using var sqlConnection = new SqlConnection(decryptedConnectionString);
            await sqlConnection.OpenAsync();

            var query = $"SELECT TOP 5 * FROM [{table.Schema}].[{table.TableName}]";
            using var command = new SqlCommand(query, sqlConnection);
            command.CommandTimeout = 30;

            var result = new
            {
                tableName = table.TableName,
                schema = table.Schema,
                columns = new List<string>(),
                rows = new List<Dictionary<string, object>>()
            };

            using var reader = await command.ExecuteReaderAsync();

            // Get column names
            for (int i = 0; i < reader.FieldCount; i++)
            {
                result.columns.Add(reader.GetName(i));
            }

            // Read rows
            while (await reader.ReadAsync() && result.rows.Count < 5)
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                result.rows.Add(row);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting sample data for table {TableName}", tableName);
            return StatusCode(500, new
            {
                error = "Failed to get sample data",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get schema changes by comparing current schema with cached version
    /// </summary>
    [HttpGet("{connectionId}/changes")]
    public async Task<IActionResult> GetSchemaChanges(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached schema
            var cachedSchema = _cache.GetCachedSchema(connectionId);
            if (cachedSchema == null)
            {
                return NotFound(new { error = "No cached schema available for comparison" });
            }

            // Decrypt connection string and scan current schema
            var decryptedConnectionString = _encryptionService.DecryptPassword(
                connection!.ConnectionString,
                connection.Id);

            if (string.IsNullOrEmpty(decryptedConnectionString))
            {
                return BadRequest(new { error = "Failed to decrypt connection string" });
            }

            var currentSchema = await _schemaScanner.ScanWithStatisticsAsync(
                decryptedConnectionString,
                includeStatistics: false); // Don't need stats for comparison

            // Detect changes
            var changeDetector = HttpContext.RequestServices
                .GetRequiredService<SchemaChangeDetector>();
            var changes = changeDetector.DetectChanges(cachedSchema, currentSchema);

            return Ok(new
            {
                hasChanges = changes.HasChanges,
                comparedAt = changes.ComparedAt,
                newTables = changes.NewTables.Select(t => new
                {
                    tableName = t.TableName,
                    schema = t.Schema,
                    description = t.NewDescription
                }),
                deletedTables = changes.DeletedTables.Select(t => new
                {
                    tableName = t.TableName,
                    schema = t.Schema,
                    description = t.OldDescription
                }),
                modifiedTables = changes.ModifiedTables.Select(t => new
                {
                    tableName = t.TableName,
                    schema = t.Schema,
                    columnChanges = t.ColumnChanges.Select(c => new
                    {
                        columnName = c.ColumnName,
                        type = c.Type.ToString().ToLower(),
                        oldDataType = c.OldDataType,
                        newDataType = c.NewDataType,
                        oldIsNullable = c.OldIsNullable,
                        newIsNullable = c.NewIsNullable
                    }),
                    indexChanges = t.IndexChanges.Select(i => new
                    {
                        indexName = i.IndexName,
                        type = i.Type.ToString().ToLower(),
                        columns = i.Columns
                    })
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error detecting schema changes for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to detect schema changes", details = ex.Message });
        }
    }
    /// <summary>
    /// Get smart query suggestions for a table
    /// </summary>
    [HttpGet("{connectionId}/tables/{tableName}/suggestions")]
    public async Task<IActionResult> GetQuerySuggestions(
        string connectionId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get schema
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return BadRequest(new { error = "Schema not cached. Please analyze database first." });
            }

            var table = schema.EnhancedTables.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (table == null)
            {
                return NotFound(new { error = $"Table '{tableName}' not found" });
            }

            // Get related tables (via foreign keys)
            var relatedTableNames = schema.BaseSchema.Relationships
                .Where(r => r.FromTable == tableName || r.ToTable == tableName)
                .SelectMany(r => new[] { r.FromTable, r.ToTable })
                .Distinct()
                .Where(t => t != tableName)
                .ToList();

            var relatedTables = schema.EnhancedTables
                .Where(t => relatedTableNames.Contains(t.TableName))
                .ToList();

            // Generate suggestions
            var suggestions = await _suggestionService.GenerateSuggestionsAsync(
                table,
                relatedTables,
                cancellationToken);

            return Ok(new { suggestions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error generating suggestions for {TableName}", tableName);
            return StatusCode(500, new
            {
                error = "Failed to generate suggestions",
                details = ex.Message
            });
        }
    }
}
