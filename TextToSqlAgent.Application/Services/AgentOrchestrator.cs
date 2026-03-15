namespace TextToSqlAgent.Application.Services;

using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Metrics;
using TextToSqlAgent.Application.Pipelines;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;
using QueryRequest = TextToSqlAgent.Application.Pipelines.QueryRequest;

/// <summary>
/// Core orchestration layer that routes queries to appropriate pipelines based on complexity.
/// 
/// Flow:
/// 1. Load schema from cache (if not already loaded)
/// 2. Call QueryClassifier to determine complexity
/// 3. Route to appropriate pipeline based on complexity
/// 4. Handle auto-escalation (Simple→Medium→Complex)
/// 5. Aggregate metrics (LLM calls, latency, etc.)
/// </summary>
public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly ISchemaCache _schemaCache;
    private readonly IQueryClassifier _queryClassifier;
    private readonly ISimpleQueryPipeline _simplePipeline;
    private readonly IMediumQueryPipeline _mediumPipeline;
    private readonly IComplexQueryPipeline _complexPipeline;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly QdrantService _qdrantService;
    private readonly SchemaScanner _schemaScanner;
    private readonly SchemaIndexer _schemaIndexer;

    // Maximum escalation attempts to prevent infinite loops
    private const int MaxEscalationAttempts = 2;

    public AgentOrchestrator(
        ISchemaCache schemaCache,
        IQueryClassifier queryClassifier,
        ISimpleQueryPipeline simplePipeline,
        IMediumQueryPipeline mediumPipeline,
        IComplexQueryPipeline complexPipeline,
        IMetricsCollector metricsCollector,
        ILogger<AgentOrchestrator> logger,
        QdrantService qdrantService,
        SchemaScanner schemaScanner,
        SchemaIndexer schemaIndexer)
    {
        _schemaCache = schemaCache;
        _queryClassifier = queryClassifier;
        _simplePipeline = simplePipeline;
        _mediumPipeline = mediumPipeline;
        _complexPipeline = complexPipeline;
        _metricsCollector = metricsCollector;
        _logger = logger;
        _qdrantService = qdrantService;
        _schemaScanner = schemaScanner;
        _schemaIndexer = schemaIndexer;
    }

    /// <summary>
    /// Execute query through the appropriate pipeline based on complexity classification
    /// </summary>
    public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("[Orchestrator] Starting query execution: {Query}", request.Question);

        try
        {
            // Step 1: Load schema from cache
            _logger.LogDebug("[Orchestrator] Loading schema for connection: {ConnectionId}", request.ConnectionId);
            var schema = await _schemaCache.GetOrSetAsync(
                request.ConnectionId ?? "default",
                () => throw new InvalidOperationException("Schema not available. Please scan database first."),
                ct);

            if (schema == null)
            {
                return CreateErrorResult("Schema not found. Please reconnect to the database.", startTime);
            }

            _logger.LogInformation("[Orchestrator] Schema loaded: {TableCount} tables", schema.Tables.Count);

            // Step 2: Classify query complexity
            var classification = await _queryClassifier.ClassifyAsync(request.Question, ct);
            _logger.LogInformation("[Orchestrator] Query classified as {Complexity} (confidence: {Confidence})",
                classification.Complexity, classification.Confidence);

            // Step 3: Route to appropriate pipeline with escalation handling
            var result = await RouteToPipelineAsync(request, classification, ct);

            // Step 4: Log metrics
            var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation(
                "[Orchestrator] Query completed: Complexity={Complexity}, Pipeline used={Pipeline}, " +
                "LLM calls={LlmCalls}, Time={Time}ms, Success={Success}",
                result.Complexity,
                GetPipelineName(result.Complexity),
                result.LlmCalls,
                totalTime,
                result.Success);

            // Record query metrics for monitoring
            var queryMetrics = new QueryMetrics
            {
                QueryComplexity = result.Complexity.ToString(),
                LlmCallCount = result.LlmCalls,
                LatencyMs = (long)totalTime,
                WasEscalated = result.WasEscalated,
                EscalationReason = result.EscalationReason,
                IsSuccess = result.Success,
                ErrorMessage = result.ErrorMessage,
                PipelineUsed = GetPipelineName(result.Complexity),
                ClassificationMethod = classification.Method.ToString(),
                Timestamp = DateTime.UtcNow
            };
            _metricsCollector.Record(queryMetrics);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orchestrator] Error executing query");
            return CreateErrorResult(ex.Message, startTime);
        }
    }

    /// <summary>
    /// Classify query complexity without executing pipeline
    /// </summary>
    public async Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default)
    {
        return await _queryClassifier.ClassifyAsync(query, ct);
    }

    /// <summary>
    /// Connect to a database and perform automatic schema indexing if needed.
    /// Checks if collection exists, gets point count, and returns initial status.
    /// </summary>
    /// <param name="connectionId">Unique identifier for the connection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="forceReindex">Force re-indexing regardless of fingerprint comparison</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Connection result with indexing status</returns>
    public async Task<ConnectionResult> ConnectToDatabaseAsync(
        string connectionId,
        string connectionString,
        bool forceReindex = false,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("[Orchestrator] Connecting to database: {ConnectionId}", connectionId);

        try
        {
            // 1. Extract database name from connection string
            var dbName = ExtractDatabaseName(connectionString);
            if (string.IsNullOrEmpty(dbName))
            {
                _logger.LogError("[Orchestrator] Failed to extract database name from connection string");
                return new ConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to extract database name from connection string"
                };
            }

            _logger.LogDebug("[Orchestrator] Extracted database name: {DatabaseName}", dbName);

            // 2. Set collection name using normalized database name
            _qdrantService.SetCollectionName(dbName);
            var collectionName = _qdrantService.GetCurrentCollectionName();
            _logger.LogDebug("[Orchestrator] Using collection name: {CollectionName}", collectionName);

            // 3. Check if collection exists
            var collectionExists = await _qdrantService.CollectionExistsAsync(ct);
            _logger.LogDebug("[Orchestrator] Collection exists: {Exists}", collectionExists);

            if (collectionExists)
            {
                // 4. Get point count if collection exists
                var pointCount = await _qdrantService.GetPointCountAsync(ct);
                _logger.LogInformation(
                    "[Orchestrator] Collection exists with {PointCount} points",
                    pointCount);

                if (pointCount > 0)
                {
                    // 5. Check schema fingerprint for changes (unless force re-index is requested)
                    _logger.LogDebug("[Orchestrator] Scanning current schema for fingerprint comparison");
                    var currentSchema = await _schemaScanner.ScanAsync(ct);
                    var currentFingerprint = ComputeSchemaFingerprint(currentSchema);

                    // Force re-index if requested
                    if (forceReindex)
                    {
                        _logger.LogInformation(
                            "[Orchestrator] Force re-index requested for {Database} - triggering re-indexing",
                            dbName);

                        var reindexedCount = await ReindexSchemaAsync(currentSchema, currentFingerprint, dbName, ct);
                        var duration = DateTime.UtcNow - startTime;

                        return new ConnectionResult
                        {
                            Success = true,
                            IndexingPerformed = true,
                            PointsIndexed = reindexedCount,
                            IndexingDuration = duration
                        };
                    }

                    _logger.LogDebug("[Orchestrator] Retrieving stored fingerprint from collection");
                    var storedFingerprint = await _qdrantService.GetStoredFingerprintAsync(ct);

                    if (storedFingerprint != null && currentFingerprint.Hash == storedFingerprint.Hash)
                    {
                        // Fingerprints match - skip indexing
                        _logger.LogInformation(
                            "[Orchestrator] Skipping indexing for {Database} - using existing {Count} embeddings (schema unchanged)",
                            dbName, pointCount);

                        return new ConnectionResult
                        {
                            Success = true,
                            IndexingPerformed = false,
                            PointsIndexed = (int)pointCount,
                            IndexingDuration = DateTime.UtcNow - startTime
                        };
                    }

                    // Fingerprints don't match - trigger re-indexing
                    LogSchemaChanges(dbName, storedFingerprint, currentFingerprint);

                    var newPointCount = await ReindexSchemaAsync(currentSchema, currentFingerprint, dbName, ct);
                    var reindexDuration = DateTime.UtcNow - startTime;

                    return new ConnectionResult
                    {
                        Success = true,
                        IndexingPerformed = true,
                        PointsIndexed = newPointCount,
                        IndexingDuration = reindexDuration
                    };
                }

                // Point count is zero - trigger indexing
                _logger.LogInformation(
                    "[Orchestrator] Collection exists but is empty - triggering indexing");
            }
            else
            {
                // Collection doesn't exist - trigger indexing
                _logger.LogInformation(
                    "[Orchestrator] Collection does not exist - triggering initial indexing");
            }

            // Perform initial indexing (collection missing or empty)
            _logger.LogDebug("[Orchestrator] Scanning schema for initial indexing");
            var schema = await _schemaScanner.ScanAsync(ct);
            var fingerprint = ComputeSchemaFingerprint(schema);

            _logger.LogInformation(
                "[Orchestrator] Starting schema indexing for {Database}",
                dbName);

            await _schemaIndexer.IndexSchemaAsync(schema, fingerprint, ct);

            var finalPointCount = await _qdrantService.GetPointCountAsync(ct);
            var finalDuration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "[Orchestrator] Initial indexing complete for {Database}: {Count} embeddings created in {Duration:F2}s",
                dbName, finalPointCount, finalDuration.TotalSeconds);

            return new ConnectionResult
            {
                Success = true,
                IndexingPerformed = true,
                PointsIndexed = (int)finalPointCount,
                IndexingDuration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orchestrator] Error connecting to database");
            return new ConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                IndexingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Re-indexes the schema by deleting the existing collection and creating new embeddings.
    /// </summary>
    private async Task<int> ReindexSchemaAsync(
        DatabaseSchema schema,
        SchemaFingerprint fingerprint,
        string dbName,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Orchestrator] Starting re-indexing for {Database} - deleting existing collection",
            dbName);

        // Delete existing collection before re-indexing
        await _qdrantService.DeleteCollectionAsync(ct);

        _logger.LogDebug("[Orchestrator] Existing collection deleted, creating new embeddings");

        // Call IndexSchemaAsync with new schema and fingerprint
        await _schemaIndexer.IndexSchemaAsync(schema, fingerprint, ct);

        // Get the new point count
        var newPointCount = await _qdrantService.GetPointCountAsync(ct);

        _logger.LogInformation(
            "[Orchestrator] Re-indexing complete for {Database}: {Count} embeddings created",
            dbName, newPointCount);

        return (int)newPointCount;
    }


    /// <summary>
    /// Extract database name from connection string.
    /// Supports SQL Server connection string formats.
    /// </summary>
    private static string? ExtractDatabaseName(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        static string? GetValue(string[] items, params string[] keys)
        {
            foreach (var key in keys)
            {
                var match = items.FirstOrDefault(p =>
                    p.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    var kv = match.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        return kv[1].Trim();
                    }
                }
            }

            return null;
        }

        // SQL Server: use database name from connection string
        var database = GetValue(parts, "Database", "Initial Catalog");
        if (!string.IsNullOrWhiteSpace(database))
        {
            return database;
        }

        // Fallback: try to infer from data source/host
        var host = GetValue(parts, "Server", "Host", "Data Source", "DataSource");
        return host;
    }

    /// <summary>
    /// Route query to appropriate pipeline with auto-escalation handling
    /// </summary>
    private async Task<QueryResult> RouteToPipelineAsync(
        QueryRequest request,
        QueryClassifierResult classification,
        CancellationToken ct,
        int escalationLevel = 0)
    {
        // Prevent infinite escalation loops
        if (escalationLevel >= MaxEscalationAttempts)
        {
            _logger.LogWarning("[Orchestrator] Max escalation level reached");
            return CreateErrorResult(
                "Query is too complex to process. Please simplify your question.",
                DateTime.UtcNow);
        }

        var complexity = classification.Complexity;
        _logger.LogInformation(
            "[Orchestrator] Routing to {Complexity} pipeline (escalation level: {Level})",
            complexity, escalationLevel);

        QueryResult result;

        switch (complexity)
        {
            case QueryComplexity.Simple:
                result = await ExecuteSimplePipelineAsync(request, ct);
                break;

            case QueryComplexity.Medium:
                result = await ExecuteMediumPipelineAsync(request, ct);
                break;

            case QueryComplexity.Complex:
            default:
                result = await ExecuteComplexPipelineAsync(request, ct);
                break;
        }

        // Handle auto-escalation if needed
        if (result.WasEscalated || (!result.Success && ShouldEscalate(result, complexity)))
        {
            _logger.LogWarning(
                "[Orchestrator] Escalating from {From} to {To}. Reason: {Reason}",
                complexity,
                GetNextComplexity(complexity),
                result.EscalationReason ?? "Execution failed");

            // Create new classification for escalation
            var escalatedClassification = new QueryClassifierResult
            {
                Complexity = GetNextComplexity(complexity),
                Confidence = 0.5,
                Reasoning = $"Escalated from {complexity}: {result.EscalationReason ?? result.ErrorMessage}",
                Method = classification.Method,
                UsedLLM = classification.UsedLLM
            };

            // Recursively escalate with incremented level
            return await RouteToPipelineAsync(request, escalatedClassification, ct, escalationLevel + 1);
        }

        return result;
    }

    /// <summary>
    /// Execute Simple pipeline with request conversion
    /// </summary>
    private async Task<QueryResult> ExecuteSimplePipelineAsync(QueryRequest request, CancellationToken ct)
    {
        var simpleRequest = new SimpleQueryRequest
        {
            Query = request.Question,
            ConnectionId = request.ConnectionId ?? "default",
            UserId = request.ConversationId, // Using conversation as user context
            ConversationId = request.ConversationId,
            MaxRows = request.Options?.MaxRows ?? 100,
            UseLlmFormatting = request.Options?.UseLlmFormatting ?? false
        };

        return await _simplePipeline.ExecuteAsync(simpleRequest, ct);
    }

    /// <summary>
    /// Execute Medium pipeline with request conversion
    /// </summary>
    private async Task<QueryResult> ExecuteMediumPipelineAsync(QueryRequest request, CancellationToken ct)
    {
        var mediumRequest = new MediumQueryRequest
        {
            Query = request.Question,
            ConnectionId = request.ConnectionId ?? "default",
            UserId = request.ConversationId,
            ConversationId = request.ConversationId,
            MaxRows = request.Options?.MaxRows ?? 100,
            UseLlmFormatting = request.Options?.UseLlmFormatting ?? false
        };

        return await _mediumPipeline.ExecuteAsync(mediumRequest, ct);
    }

    /// <summary>
    /// Execute Complex pipeline with request conversion
    /// </summary>
    private async Task<QueryResult> ExecuteComplexPipelineAsync(QueryRequest request, CancellationToken ct)
    {
        var complexRequest = new ComplexQueryRequest
        {
            Query = request.Question,
            ConnectionId = request.ConnectionId ?? "default",
            UserId = request.ConversationId,
            ConversationId = request.ConversationId,
            MaxRows = request.Options?.MaxRows ?? 100,
            UseLlmFormatting = request.Options?.UseLlmFormatting ?? true
        };

        return await _complexPipeline.ExecuteAsync(complexRequest, ct);
    }

    /// <summary>
    /// Determine if result should escalate to next level
    /// </summary>
    private bool ShouldEscalate(QueryResult result, QueryComplexity currentComplexity)
    {
        // Don't escalate if already at max complexity
        if (currentComplexity == QueryComplexity.Complex)
            return false;

        // Escalate on failure
        if (!result.Success)
            return true;

        // Check specific escalation triggers from pipelines
        if (!string.IsNullOrEmpty(result.EscalationReason))
            return true;

        return false;
    }

    /// <summary>
    /// Get next complexity level for escalation
    /// </summary>
    private QueryComplexity GetNextComplexity(QueryComplexity current)
    {
        return current switch
        {
            QueryComplexity.Simple => QueryComplexity.Medium,
            QueryComplexity.Medium => QueryComplexity.Complex,
            QueryComplexity.Complex => QueryComplexity.Complex, // Already at max
            _ => QueryComplexity.Complex
        };
    }

    /// <summary>
    /// Get human-readable pipeline name
    /// </summary>
    private string GetPipelineName(QueryComplexity complexity)
    {
        return complexity switch
        {
            QueryComplexity.Simple => "SimplePipeline",
            QueryComplexity.Medium => "MediumPipeline",
            QueryComplexity.Complex => "ComplexPipeline",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Compute a deterministic fingerprint of the database schema for change detection.
    /// Creates a SHA256 hash from sorted table names, column names with types, and relationships.
    /// </summary>
    /// <param name="schema">The database schema to fingerprint</param>
    /// <returns>SchemaFingerprint object with hash, timestamp, and counts</returns>
    public SchemaFingerprint ComputeSchemaFingerprint(DatabaseSchema schema)
    {
        var elements = new List<string>();

        // Add table elements (sorted by table name)
        foreach (var table in schema.Tables.OrderBy(t => t.TableName))
        {
            elements.Add($"T:{table.TableName}");

            // Add column elements (sorted by column name)
            foreach (var col in table.Columns.OrderBy(c => c.ColumnName))
            {
                elements.Add($"C:{table.TableName}.{col.ColumnName}:{col.DataType}");
            }
        }

        // Add relationship elements (sorted by from table and column)
        foreach (var rel in schema.Relationships.OrderBy(r => $"{r.FromTable}.{r.FromColumn}"))
        {
            elements.Add($"R:{rel.FromTable}.{rel.FromColumn}->{rel.ToTable}.{rel.ToColumn}");
        }

        // Combine all elements into a single string
        var combined = string.Join("|", elements);

        // Compute SHA256 hash
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        var hash = Convert.ToBase64String(hashBytes);

        // Count columns across all tables
        var columnCount = schema.Tables.Sum(t => t.Columns.Count);

        return new SchemaFingerprint
        {
            Hash = hash,
            ComputedAt = DateTime.UtcNow,
            TableCount = schema.Tables.Count,
            ColumnCount = columnCount,
            RelationshipCount = schema.Relationships.Count,
            TableNames = schema.Tables.Select(t => t.TableName).OrderBy(n => n).ToList()
        };
    }

    /// <summary>
    /// Create error result
    /// </summary>
    private QueryResult CreateErrorResult(string error, DateTime startTime)
    {
        return new QueryResult
        {
            Success = false,
            ErrorMessage = error,
            ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
            Complexity = QueryComplexity.Simple, // Default
            LlmCalls = 0,
            ProcessingSteps = new List<string> { $"Error: {error}" }
        };
    }

    /// <summary>
    /// Log detailed information about schema changes detected
    /// </summary>
    private void LogSchemaChanges(string dbName, SchemaFingerprint? stored, SchemaFingerprint current)
    {
        var changes = new List<string>();

        if (stored == null)
        {
            _logger.LogInformation(
                "[Orchestrator] Schema changes detected for {Database} - no previous fingerprint found, triggering re-indexing",
                dbName);
            return;
        }

        // Detect table count changes
        if (stored.TableCount != current.TableCount)
        {
            changes.Add($"tables: {stored.TableCount} → {current.TableCount}");
        }

        // Detect column count changes
        if (stored.ColumnCount != current.ColumnCount)
        {
            changes.Add($"columns: {stored.ColumnCount} → {current.ColumnCount}");
        }

        // Detect relationship count changes
        if (stored.RelationshipCount != current.RelationshipCount)
        {
            changes.Add($"relationships: {stored.RelationshipCount} → {current.RelationshipCount}");
        }

        // Detect specific table changes
        var addedTables = current.TableNames.Except(stored.TableNames).ToList();
        var removedTables = stored.TableNames.Except(current.TableNames).ToList();

        if (addedTables.Any())
        {
            changes.Add($"added tables: {string.Join(", ", addedTables)}");
        }

        if (removedTables.Any())
        {
            changes.Add($"removed tables: {string.Join(", ", removedTables)}");
        }

        // Log the changes
        if (changes.Any())
        {
            _logger.LogInformation(
                "[Orchestrator] Schema changes detected for {Database} - {Changes} - triggering re-indexing",
                dbName, string.Join("; ", changes));
        }
        else
        {
            // Hash changed but counts are the same - likely column type or relationship changes
            _logger.LogInformation(
                "[Orchestrator] Schema changes detected for {Database} - column types or relationship definitions modified - triggering re-indexing",
                dbName);
        }
    }
}
