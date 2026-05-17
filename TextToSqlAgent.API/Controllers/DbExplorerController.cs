using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services.DbExplorer;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
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
    private readonly ISchemaSemanticProfileStore _semanticProfileStore;
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
        ISchemaSemanticProfileStore semanticProfileStore,
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
        _semanticProfileStore = semanticProfileStore;
        _logger = logger;
    }

    /// <summary>
    /// Test endpoint to verify controller is registered
    /// </summary>
    [HttpGet("test")]
    [AllowAnonymous]
    public IActionResult Test()
    {
        return Ok(new
        {
            message = "DbExplorer controller is working!",
            timestamp = DateTime.UtcNow,
            route = "api/db-explorer/test"
        });
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
    /// Build system context from connection settings
    /// </summary>
    private string BuildSystemContext(Connection connection)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(connection.SystemDomain))
        {
            parts.Add($"Domain: {connection.SystemDomain}");
        }

        if (!string.IsNullOrEmpty(connection.NamingConventionNotes))
        {
            parts.Add($"Naming Convention: {connection.NamingConventionNotes}");
        }

        if (!string.IsNullOrEmpty(connection.BusinessContext))
        {
            parts.Add($"Business Context: {connection.BusinessContext}");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "No specific context provided.";
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

    /// <summary>
    /// Get the Redis-backed semantic profile for a connection.
    /// </summary>
    [HttpGet("{connectionId}/semantic-profile")]
    public async Task<IActionResult> GetSemanticProfile(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            var profile = await _semanticProfileStore.GetAsync(connectionId, cancellationToken)
                          ?? new SchemaSemanticProfile { ConnectionId = connectionId };

            return Ok(new
            {
                success = true,
                profile
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting semantic profile for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to get semantic profile", details = ex.Message });
        }
    }

    /// <summary>
    /// Save the Redis-backed semantic profile for a connection.
    /// This changes AI metadata only; it does not mutate the target database.
    /// </summary>
    [HttpPut("{connectionId}/semantic-profile")]
    public async Task<IActionResult> SaveSemanticProfile(
        string connectionId,
        [FromBody] SchemaSemanticProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            var userId = GetRequiredUserId();
            profile.ConnectionId = connectionId;
            profile.UpdatedBy = userId;

            NormalizeSemanticProfile(profile);

            await _semanticProfileStore.SetAsync(connectionId, profile, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Semantic profile saved to Redis. New chat turns will use this metadata automatically.",
                requiresReindex = true,
                reindexReason = "Qdrant schema embeddings should be refreshed so semantic search reflects the new descriptions and synonyms.",
                profile
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error saving semantic profile for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to save semantic profile", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete the Redis-backed semantic profile for a connection.
    /// </summary>
    [HttpDelete("{connectionId}/semantic-profile")]
    public async Task<IActionResult> DeleteSemanticProfile(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            await _semanticProfileStore.DeleteAsync(connectionId, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Semantic profile deleted from Redis.",
                requiresReindex = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error deleting semantic profile for {ConnectionId}", connectionId);
            return StatusCode(500, new { error = "Failed to delete semantic profile", details = ex.Message });
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

    private static void NormalizeSemanticProfile(SchemaSemanticProfile profile)
    {
        profile.GlobalSynonyms = NormalizeSynonyms(profile.GlobalSynonyms);

        profile.Tables = profile.Tables
            .Where(t => !string.IsNullOrWhiteSpace(t.TableName))
            .GroupBy(t => t.TableName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var table = g.Last();
                table.TableName = table.TableName.Trim();
                table.Synonyms = NormalizeSynonyms(table.Synonyms);
                table.Columns = table.Columns
                    .Where(c => !string.IsNullOrWhiteSpace(c.ColumnName))
                    .GroupBy(c => c.ColumnName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(cg =>
                    {
                        var column = cg.Last();
                        column.ColumnName = column.ColumnName.Trim();
                        column.Synonyms = NormalizeSynonyms(column.Synonyms);
                        return column;
                    })
                    .ToList();
                return table;
            })
            .ToList();
    }

    private static List<string> NormalizeSynonyms(IEnumerable<string>? synonyms)
    {
        return synonyms?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>
    /// Analyze database schema - LIGHTWEIGHT overview (lazy loading strategy)
    /// </summary>
    [HttpPost("{connectionId}/analyze")]
    public async Task<IActionResult> AnalyzeDatabase(
        string connectionId,
        [FromQuery] bool forceRefresh = false,
        [FromQuery] string mode = "overview",
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

                _logger.LogInformation(
                    "[DbExplorer] 🔍 ANALYZE DEBUG - Database name extracted: '{DatabaseName}'",
                    databaseName);

                if (!string.IsNullOrEmpty(databaseName))
                {
                    var qdrantService = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
                    qdrantService.SetCollectionName(databaseName);

                    _logger.LogInformation(
                        "[DbExplorer] 🔍 ANALYZE DEBUG - Collection name set for indexing");

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

            // Get system context from connection
            var systemContext = BuildSystemContext(connection);

            // Run AI analysis based on mode
            DatabaseAnalysis analysis;
            if (mode == "overview")
            {
                // Lightweight overview only (fast)
                analysis = await _analyzer.AnalyzeOverviewAsync(schema, systemContext, cancellationToken);
            }
            else
            {
                // Full analysis (legacy, slower)
#pragma warning disable CS0618 // Type or member is obsolete
                analysis = await _analyzer.AnalyzeAsync(schema, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
            }

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
                mode,
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
        _logger.LogInformation("[DbExplorer] GetTables called for connectionId: {ConnectionId}, role: {Role}, module: {Module}, search: {Search}",
            connectionId, role, module, search);

        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null)
            {
                _logger.LogWarning("[DbExplorer] Connection validation failed for {ConnectionId}", connectionId);
                return errorResult;
            }

            // Get cached data
            var schema = _cache.GetCachedSchema(connectionId);
            var analysis = _cache.GetCachedAnalysis(connectionId);

            if (schema == null)
            {
                _logger.LogWarning("[DbExplorer] No schema data available for {ConnectionId}", connectionId);
                return NotFound(new { error = "No schema data available" });
            }

            _logger.LogInformation("[DbExplorer] Found {TableCount} tables in schema for {ConnectionId}",
                schema.EnhancedTables.Count, connectionId);

            // Filter tables
            var tables = schema.EnhancedTables.AsEnumerable();
            var semanticProfile = await _semanticProfileStore.GetAsync(connectionId);

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
                    var tableProfile = semanticProfile?.FindTable(t.TableName);
                    return new TableSummaryDto
                    {
                        TableName = t.TableName,
                        Schema = t.Schema,
                        Role = t.Role ?? TableRole.Unknown,
                        Module = t.Module,
                        RowCount = t.RowCount,
                        ColumnCount = t.ColumnCount,
                        ForeignKeyCount = t.ForeignKeys.Count,
                        Description = FirstNonEmpty(tableProfile?.BusinessMeaning, tableProfile?.Description, roleInfo?.Description) ?? "",
                        BusinessMeaning = tableProfile?.BusinessMeaning,
                        Synonyms = tableProfile?.Synonyms ?? new List<string>(),
                        HasSemanticOverride = tableProfile != null
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
            var semanticProfile = await _semanticProfileStore.GetAsync(connectionId);
            var tableProfile = semanticProfile?.FindTable(table.TableName);

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
                Description = FirstNonEmpty(tableProfile?.BusinessMeaning, tableProfile?.Description, roleInfo?.Description) ?? "",
                BusinessMeaning = tableProfile?.BusinessMeaning,
                Synonyms = tableProfile?.Synonyms ?? new List<string>(),
                HasSemanticOverride = tableProfile != null,
                Columns = table.Columns.Select(c =>
                {
                    var columnProfile = tableProfile?.FindColumn(c.ColumnName);
                    return new ColumnDetailDto
                    {
                        Description = FirstNonEmpty(columnProfile?.BusinessMeaning, columnProfile?.Description, c.Description),
                        BusinessMeaning = columnProfile?.BusinessMeaning,
                        Role = columnProfile?.Role ?? c.Role,
                        DisplayPriority = columnProfile?.DisplayPriority ?? c.DisplayPriority,
                        PreferredForReports = columnProfile?.PreferredForReports ?? c.PreferredForReports,
                        Synonyms = columnProfile?.Synonyms ?? new List<string>(),
                        HasSemanticOverride = columnProfile != null,
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
                    };
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
    /// Analyze table detail on-demand (lazy loading)
    /// </summary>
    [HttpPost("{connectionId}/tables/{tableName}/analyze")]
    public async Task<IActionResult> AnalyzeTableDetail(
        string connectionId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DbExplorer] Analyzing table detail for {TableName}", tableName);

            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            // Get cached schema
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return NotFound(new { error = "No schema data available. Please analyze database first." });
            }

            // Find table
            var table = schema.EnhancedTables.FirstOrDefault(t =>
                t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

            if (table == null)
            {
                return NotFound(new { error = $"Table '{tableName}' not found" });
            }

            // Build system context
            var systemContext = BuildSystemContext(connection!);

            // Analyze table detail
            var tableDetail = await _analyzer.AnalyzeTableDetailAsync(
                table,
                schema,
                systemContext,
                connection.NamingConventionNotes,
                cancellationToken);

            // Build response
            var response = new
            {
                tableName = tableDetail.TableName,
                analyzedAt = tableDetail.AnalyzedAt,
                columnInterpretations = tableDetail.ColumnInterpretations.Select(kvp => new
                {
                    columnName = kvp.Key,
                    vietnamese = kvp.Value.Vietnamese,
                    english = kvp.Value.English,
                    description = kvp.Value.Description,
                    confidence = kvp.Value.Confidence
                }),
                implicitRelationships = tableDetail.ImplicitRelationships.Select(r => new
                {
                    fromTable = r.FromTable,
                    fromColumn = r.FromColumn,
                    toTable = r.ToTable,
                    toColumn = r.ToColumn,
                    confidence = r.Confidence,
                    detectionMethod = r.DetectionMethod,
                    reason = r.Reason,
                    requiresDataValidation = r.RequiresDataValidation
                }),
                healthIssues = tableDetail.HealthIssues.Select(i => new
                {
                    severity = i.Severity.ToString().ToLower(),
                    type = i.Type.ToString().ToLower(),
                    table = i.Table,
                    column = i.Column,
                    description = i.Description,
                    recommendation = i.Recommendation
                })
            };

            _logger.LogInformation(
                "[DbExplorer] ✅ Table detail analysis complete: {Columns} columns, {ImplicitFKs} implicit FKs",
                tableDetail.ColumnInterpretations.Count,
                tableDetail.ImplicitRelationships.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error analyzing table detail for {TableName}", tableName);
            return StatusCode(500, new { error = "Failed to analyze table detail", details = ex.Message });
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



    /// <summary>
    /// Export database documentation
    /// </summary>
    [HttpGet("{connectionId}/export")]
    public async Task<IActionResult> ExportDocumentation(
        string connectionId,
        [FromQuery] string format = "markdown")
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            _logger.LogInformation("[DbExplorer] Exporting documentation for connection {ConnectionId}, format={Format}",
                connectionId, format);

            // Get cached schema
            var cacheKey = $"dbexplorer:schema:{connectionId}";
            var schema = _cache.GetCachedSchema(connectionId);

            if (schema == null)
            {
                return NotFound(new { error = "Schema not found. Please analyze the database first." });
            }

            // Get cached analysis
            var analysisCacheKey = $"dbexplorer:analysis:{connectionId}";
            var analysis = _cache.GetCachedAnalysis(connectionId);

            // Extract database name and server name
            var databaseName = ExtractDatabaseNameFromConnectionString(connection!.ConnectionString);
            var serverName = connection.Name;

            // Generate documentation
            var generator = new DocumentationGenerator(HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<DocumentationGenerator>());

            if (format.ToLower() == "markdown")
            {
                var markdown = generator.GenerateMarkdown(schema, analysis, databaseName, serverName);
                var fileName = $"{databaseName ?? "Database"}_Documentation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md";

                return File(
                    System.Text.Encoding.UTF8.GetBytes(markdown),
                    "text/markdown",
                    fileName);
            }
            else if (format.ToLower() == "summary")
            {
                var summary = generator.GenerateSummary(schema, analysis, databaseName, serverName);
                return Ok(summary);
            }
            else
            {
                return BadRequest(new { error = $"Unsupported format: {format}. Supported formats: markdown, summary" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Failed to export documentation for connection {ConnectionId}",
                connectionId);
            return StatusCode(500, new { error = "Failed to export documentation", details = ex.Message });
        }
    }


    /// <summary>
    /// Analyze naming conventions
    /// </summary>
    [HttpGet("{connectionId}/naming-analysis")]
    public async Task<IActionResult> AnalyzeNamingConventions(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            _logger.LogInformation("[DbExplorer] Analyzing naming conventions for connection {ConnectionId}",
                connectionId);

            // Get cached schema
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return NotFound(new { error = "No schema data available. Please analyze database first." });
            }

            // Analyze naming conventions
            var analyzer = new NamingConventionAnalyzer(
                HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<NamingConventionAnalyzer>());

            var report = analyzer.AnalyzeSchema(schema);

            // Build response
            var response = new
            {
                analyzedAt = report.AnalyzedAt,
                totalTables = report.TotalTables,
                totalColumns = report.TotalColumns,
                dominantTablePattern = report.DominantTablePattern.ToString(),
                dominantColumnPattern = report.DominantColumnPattern.ToString(),
                tablePatternStatistics = report.TablePatternStatistics,
                columnPatternStatistics = report.ColumnPatternStatistics,
                inconsistencies = report.Inconsistencies.Select(i => new
                {
                    type = i.Type.ToString().ToLower(),
                    table = i.Table,
                    column = i.Column,
                    currentName = i.CurrentName,
                    suggestedName = i.SuggestedName,
                    currentPattern = i.CurrentPattern.ToString(),
                    expectedPattern = i.ExpectedPattern.ToString(),
                    severity = i.Severity.ToString().ToLower(),
                    description = i.Description
                }),
                recommendations = report.Recommendations.Select(r => new
                {
                    title = r.Title,
                    description = r.Description,
                    priority = r.Priority.ToString().ToLower(),
                    affectedTables = r.AffectedTables,
                    sqlScript = r.SqlScript
                })
            };

            _logger.LogInformation(
                "[DbExplorer] ✅ Naming analysis complete: Dominant={Dominant}, Inconsistencies={Count}",
                report.DominantTablePattern,
                report.Inconsistencies.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error analyzing naming conventions for {ConnectionId}",
                connectionId);
            return StatusCode(500, new { error = "Failed to analyze naming conventions", details = ex.Message });
        }
    }

    /// <summary>
    /// Get index recommendations
    /// </summary>
    [HttpGet("{connectionId}/index-recommendations")]
    public async Task<IActionResult> GetIndexRecommendations(string connectionId)
    {
        try
        {
            // Validate connection access
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            _logger.LogInformation("[DbExplorer] Getting index recommendations for connection {ConnectionId}",
                connectionId);

            // Get cached schema
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return NotFound(new { error = "No schema data available. Please analyze database first." });
            }

            // Analyze indexes
            var engine = new IndexRecommendationEngine(
                HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<IndexRecommendationEngine>());

            var report = engine.AnalyzeIndexes(schema);

            // Build response
            var response = new
            {
                analyzedAt = report.AnalyzedAt,
                totalTables = report.TotalTables,
                totalIndexes = report.TotalIndexes,
                missingIndexCount = report.MissingIndexCount,
                redundantIndexCount = report.RedundantIndexCount,
                optimizationCount = report.OptimizationCount,
                recommendations = report.Recommendations.Select(r => new
                {
                    type = r.Type.ToString().ToLower(),
                    table = r.Table,
                    columns = r.Columns,
                    indexName = r.IndexName,
                    reason = r.Reason,
                    impact = r.Impact.ToString().ToLower(),
                    estimatedImprovement = r.EstimatedImprovement,
                    sqlScript = r.SqlScript
                })
            };

            _logger.LogInformation(
                "[DbExplorer] ✅ Index analysis complete: {Missing} missing, {Redundant} redundant, {Optimize} optimizations",
                report.MissingIndexCount,
                report.RedundantIndexCount,
                report.OptimizationCount);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error getting index recommendations for {ConnectionId}",
                connectionId);
            return StatusCode(500, new { error = "Failed to get index recommendations", details = ex.Message });
        }
    }

    /// <summary>
    /// Search tables using semantic search (Qdrant)
    /// </summary>
    [HttpGet("{connectionId}/search")]
    public async Task<IActionResult> SearchTables(
        string connectionId,
        [FromQuery] string query,
        [FromQuery] int limit = 10,
        [FromQuery] double scoreThreshold = 0.5)
    {
        try
        {
            var (connection, errorResult) = await ValidateConnectionAccessAsync(connectionId);
            if (errorResult != null) return errorResult;

            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { error = "Query parameter is required" });
            }

            // Get cached schema
            var schema = _cache.GetCachedSchema(connectionId);
            if (schema == null)
            {
                return BadRequest(new
                {
                    error = "Schema not analyzed",
                    message = "Please analyze the database first before searching"
                });
            }

            // Get Qdrant indexer
            var qdrantIndexer = HttpContext.RequestServices.GetService<DbExplorerQdrantIndexer>();
            if (qdrantIndexer == null)
            {
                return StatusCode(500, new
                {
                    error = "Semantic search not available",
                    message = "Qdrant indexer service is not configured"
                });
            }

            // Set collection name
            var connectionString = _encryptionService.DecryptPassword(connection!.ConnectionString, connection.Id);
            var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);

            _logger.LogInformation(
                "[DbExplorer] 🔍 SEARCH DEBUG - Database name extracted: '{DatabaseName}'",
                databaseName);

            var qdrantService = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
            qdrantService.SetCollectionName(databaseName);

            _logger.LogInformation(
                "[DbExplorer] 🔍 SEARCH DEBUG - Collection name set, now searching with query: '{Query}', limit: {Limit}, threshold: {Threshold}",
                query, limit, scoreThreshold);

            // Search tables
            var finalScoreThreshold = Math.Clamp(scoreThreshold, 0.0, 1.0);
            var results = await qdrantIndexer.SearchTablesAsync(
                query,
                limit,
                finalScoreThreshold,
                cancellationToken: HttpContext.RequestAborted);

            // AUTO-RETRY with lower threshold if no results found and original was high
            var usedThreshold = finalScoreThreshold;
            if (results.Count == 0 && finalScoreThreshold > 0.45)
            {
                _logger.LogInformation("[DbExplorer] No results at {Threshold}, retrying with 0.4", finalScoreThreshold);
                usedThreshold = 0.4;
                results = await qdrantIndexer.SearchTablesAsync(
                    query,
                    limit,
                    0.4,
                    cancellationToken: HttpContext.RequestAborted);
            }

            // KEYWORD FALLBACK: if still 0 results, do a simple string search
            if (results.Count == 0 && schema != null)
            {
                _logger.LogInformation("[DbExplorer] Vector search failed, falling back to keyword search for '{Query}'", query);
                var lowerQuery = query.ToLower();
                var keywordResults = schema.EnhancedTables
                    .Where(t => t.TableName.ToLower().Contains(lowerQuery) || 
                                (t.Module != null && t.Module.ToLower().Contains(lowerQuery)))
                    .Take(limit)
                    .Select(t => new TableSearchResult
                    {
                        TableName = t.TableName,
                        Role = t.Role?.ToString() ?? "Unknown",
                        Module = t.Module ?? "Unknown",
                        Score = 0.5f, // Dummy score for keyword match
                        IsSemanticMatch = false
                    })
                    .ToList();
                
                if (keywordResults.Any())
                {
                    results = keywordResults;
                    usedThreshold = 0.0; // Indicate keyword match
                }
            }

            _logger.LogInformation(
                "[DbExplorer] 🔍 SEARCH DEBUG - Search completed. Query: '{Query}', Results: {Count}",
                query,
                results.Count);

            return Ok(new
            {
                query,
                resultCount = results.Count,
                results = results.Select(r => new
                {
                    tableName = r.TableName,
                    role = r.Role,
                    module = r.Module,
                    score = r.Score,
                    isSemanticMatch = r.IsSemanticMatch ?? true
                }),
                usedThreshold
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorer] Error searching tables for {ConnectionId} with query '{Query}'",
                connectionId, query);
            return StatusCode(500, new { error = "Failed to search tables", details = ex.Message });
        }
    }
}
