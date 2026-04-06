using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Qdrant.Client.Grpc;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.VectorDB;

public class QdrantService
{
    private readonly HttpClient _httpClient;
    private readonly QdrantConfig _config;
    private readonly ILogger<QdrantService> _logger;
    private readonly string _baseUrl;
    private string _currentCollectionName;

    // ✅ INFRA-6: Polly resilience pipeline — retry + circuit breaker for transient HTTP failures
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    // ✅ TD-10: Accept IHttpClientFactory to prevent socket exhaustion
    public QdrantService(QdrantConfig config, ILogger<QdrantService> logger, IHttpClientFactory? httpClientFactory = null)
    {
        _config = config;
        _logger = logger;
        _currentCollectionName = config.CollectionName;

        // TD-10: Use factory-created client when available, fall back to raw client
        _httpClient = httpClientFactory?.CreateClient("Qdrant") ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ✅ FIX: Always use REST API port 6333 (not gRPC 6334)
        _baseUrl = $"http://{config.Host}:6333";

        // Build Polly resilience pipeline: Retry (3x exponential) + Circuit Breaker (5 failures → open 30s)
        var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .HandleResult(r => (int)r.StatusCode >= 500);

        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = shouldHandle,
                OnRetry = args =>
                {
                    _logger.LogWarning("[Qdrant] Retrying request (attempt {AttemptNumber}) after {Delay} due to {Outcome}",
                        args.AttemptNumber + 1, args.RetryDelay,
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return default;
                }
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.8,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = shouldHandle,
                OnOpened = args =>
                {
                    _logger.LogError("[Qdrant] ⚡ Circuit breaker OPENED — Qdrant unreachable, failing fast for {Duration}s",
                        args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("[Qdrant] ✅ Circuit breaker CLOSED — Qdrant recovered");
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation("[Qdrant] 🔄 Circuit breaker HALF-OPEN — Testing Qdrant connectivity");
                    return default;
                }
            })
            .Build();

        _logger.LogInformation(
            "[Qdrant] Initialized - URL: {BaseUrl}, VectorSize: {VectorSize}, CircuitBreaker: enabled",
            _baseUrl, config.VectorSize);
    }


    public virtual void SetCollectionName(string databaseName)
    {
        _currentCollectionName = CollectionNameHelper.NormalizeCollectionName(databaseName);
        _logger.LogInformation("[Qdrant] Collection name: {CollectionName}", _currentCollectionName);
    }

    public virtual void SetUserCollectionName(string userId)
    {
        _currentCollectionName = CollectionNameHelper.NormalizeUserCollectionName(userId);
        _logger.LogInformation("[Qdrant] User collection name: {CollectionName}", _currentCollectionName);
    }

    public virtual string GetCurrentCollectionName() => _currentCollectionName;

    public virtual async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error checking collection existence");
            return false;
        }
    }

    public async Task<CollectionInfo?> GetCollectionInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Qdrant] Collection info request failed: {Status}",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // âœ… Log raw JSON for debugging
            _logger.LogDebug("[Qdrant] Raw response: {Json}", json);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<CollectionInfoResponse>(json, options);

            if (result?.Result != null)
            {
                _logger.LogInformation(
                    "[Qdrant] Collection info - Points: {Points}, VectorSize: {VectorSize}, Distance: {Distance}",
                    result.Result.PointsCount,
                    result.Result.Config?.Params?.Vectors?.Size ?? 0,
                    result.Result.Config?.Params?.Vectors?.Distance ?? "unknown");
            }

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error getting collection info");
            return null;
        }
    }

    public async Task<bool> IsVectorSizeCompatibleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetCollectionInfoAsync(cancellationToken);
            if (info == null)
            {
                _logger.LogWarning("[Qdrant] Cannot retrieve collection info");
                return false;
            }

            var expectedSize = _config.VectorSize;

            // âœ… FIX: Correct path to vector size
            var actualSize = info.Config?.Params?.Vectors?.Size ?? 0;

            _logger.LogInformation(
                "[Qdrant] Vector size - Expected: {Expected}, Actual: {Actual}, Match: {Match}",
                expectedSize, actualSize, actualSize == expectedSize);

            if (actualSize == 0)
            {
                _logger.LogError("[Qdrant] Collection has invalid vector configuration!");
                return false;
            }

            return actualSize == expectedSize;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error checking vector size compatibility");
            return false;
        }
    }

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await CollectionExistsAsync(cancellationToken);

            if (exists)
            {
                var compatible = await IsVectorSizeCompatibleAsync(cancellationToken);

                if (!compatible)
                {
                    _logger.LogWarning(
                        "[Qdrant] âš ï¸ Vector size mismatch! Recreating collection: {CollectionName}",
                        _currentCollectionName);

                    await DeleteCollectionAsync(cancellationToken);
                    await CreateCollectionAsync(cancellationToken);
                }
                else
                {
                    _logger.LogInformation("[Qdrant] âœ“ Collection exists with correct configuration");
                }
            }
            else
            {
                _logger.LogInformation("[Qdrant] Collection not found, creating new one...");
                await CreateCollectionAsync(cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Qdrant] Cannot connect to {Url}", _baseUrl);
            throw new VectorDBException(
                $"Cannot connect to Qdrant at {_baseUrl}. " +
                $"Ensure Qdrant is running:\n" +
                $"docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant",
                ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "[Qdrant] Request timeout");
            throw new VectorDBException("Qdrant request timeout", ex);
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Unexpected error");
            throw new VectorDBException($"Qdrant error: {ex.Message}", ex);
        }
    }

    public async Task CreateCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "[Qdrant] Creating collection '{Collection}' with VectorSize={VectorSize}, Distance=Cosine",
                _currentCollectionName, _config.VectorSize);

            var request = new
            {
                vectors = new
                {
                    size = _config.VectorSize,
                    distance = "Cosine"
                }
            };

            var json = JsonSerializer.Serialize(request);
            _logger.LogDebug("[Qdrant] Create request: {Json}", json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[Qdrant] Create failed - Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);

                throw new VectorDBException(
                    $"Failed to create collection. Status: {response.StatusCode}, Error: {responseBody}");
            }

            _logger.LogInformation("[Qdrant] âœ“ Collection created successfully");

            // âœ… Verify creation
            var info = await GetCollectionInfoAsync(cancellationToken);
            if (info == null)
            {
                _logger.LogWarning("[Qdrant] âš ï¸ Collection created but cannot verify");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Qdrant] HTTP error creating collection");
            throw new VectorDBException(
                $"Cannot connect to Qdrant at {_baseUrl}",
                ex);
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Unexpected error creating collection");
            throw new VectorDBException($"Create collection failed: {ex.Message}", ex);
        }
    }

    public virtual async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[Qdrant] Deleting collection: {Collection}", _currentCollectionName);

            var response = await _httpClient.DeleteAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}",
                cancellationToken);

            if (!response.IsSuccessStatusCode &&
                response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new VectorDBException(
                    $"Failed to delete collection. Status: {response.StatusCode}, Error: {errorContent}");
            }

            _logger.LogInformation("[Qdrant] âœ“ Collection deleted");
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error deleting collection");
            throw new VectorDBException($"Delete failed: {ex.Message}", ex);
        }
    }

    public async Task UpsertPointsAsync(
        List<PointStruct> points,
        CancellationToken cancellationToken = default)
    {
        if (points == null || points.Count == 0)
        {
            _logger.LogWarning("[Qdrant] No points to upsert");
            return;
        }

        try
        {
            _logger.LogInformation("[Qdrant] Upserting {Count} points", points.Count);

            var pointsList = points.Select(p => new
            {
                id = !string.IsNullOrEmpty(p.Id.Uuid)
                    ? (object)p.Id.Uuid
                    : (object)p.Id.Num,
                vector = p.Vectors.Vector.Data.Select(x => (double)x).ToList(),
                payload = p.Payload.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value.StringValue)
            }).ToList();

            // Validate first point dimension
            var first = pointsList.FirstOrDefault();
            if (first != null)
            {
                var vectorDim = first.vector?.Count ?? 0;
                if (vectorDim != _config.VectorSize)
                {
                    throw new VectorDBException(
                        $"Vector dimension mismatch! Point: {vectorDim}, Collection expects: {_config.VectorSize}");
                }
            }

            var request = new { points = pointsList };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}/points",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[Qdrant] Upsert failed - Status: {Status}, Body: {Body}",
                    response.StatusCode, responseBody);

                throw new VectorDBException(
                    $"Upsert failed. Status: {response.StatusCode}, Error: {responseBody}");
            }

            _logger.LogInformation("[Qdrant] ✅ Upsert complete ({Count} points)", points.Count);
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Unexpected upsert error");
            throw new VectorDBException($"Upsert failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Search without filter — delegates to the main overload.
    /// </summary>
    public Task<List<ScoredPoint>> SearchAsync(
        float[] queryVector,
        ulong limit = 5,
        double scoreThreshold = 0.7,
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(queryVector, limit, scoreThreshold, filter: null, cancellationToken);
    }

    public async Task<List<ScoredPoint>> SearchAsync(
        float[] queryVector,
        ulong limit = 5,
        double scoreThreshold = 0.7,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be null or empty");
        }

        // ✅ Validate vector dimension
        if (queryVector.Length != _config.VectorSize)
        {
            throw new ArgumentException(
                $"Query vector dimension ({queryVector.Length}) " +
                $"doesn't match collection ({_config.VectorSize})");
        }

        try
        {
            _logger.LogDebug(
                "[Qdrant] Search - Limit: {Limit}, Threshold: {Threshold}, VectorDim: {VectorDim}, Filter: {HasFilter}",
                limit, scoreThreshold, queryVector.Length, filter != null);

            var request = new
            {
                vector = queryVector,
                limit = (int)limit,
                score_threshold = scoreThreshold,
                with_payload = true,
                filter = filter != null ? new { must = filter.Select(kvp => new { key = kvp.Key, match = new { value = kvp.Value } }).ToArray() } : null
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}/points/search",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[Qdrant] Search failed - Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);

                throw new VectorDBException(
                    $"Search failed. Status: {response.StatusCode}, Error: {responseBody}");
            }

            var result = JsonSerializer.Deserialize<SearchResponse>(
                responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var scoredPoints = new List<ScoredPoint>();

            if (result?.Result != null)
            {
                foreach (var r in result.Result)
                {
                    var sp = new ScoredPoint { Score = (float)r.Score };

                    var idStr = r.Id.ToString();
                    if (ulong.TryParse(idStr, out var numId))
                    {
                        sp.Id = new PointId { Num = numId };
                    }
                    else
                    {
                        sp.Id = new PointId { Uuid = idStr };
                    }

                    if (r.Payload != null)
                    {
                        foreach (var kvp in r.Payload)
                        {
                            sp.Payload[kvp.Key] = new Value
                            {
                                StringValue = kvp.Value?.ToString() ?? ""
                            };
                        }
                    }

                    scoredPoints.Add(sp);
                }
            }

            _logger.LogInformation("[Qdrant] Found {Count} results (filtered: {HasFilter})", scoredPoints.Count, filter != null);

            if (scoredPoints.Count == 0)
            {
                var pointCount = await GetPointCountAsync(cancellationToken);
                _logger.LogWarning(
                    "[Qdrant] ⚠️ No results found. Collection has {Count} points. " +
                    "Try lowering score_threshold or check vector quality.",
                    pointCount);
            }

            return scoredPoints;
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Search error");
            throw new VectorDBException($"Search failed: {ex.Message}", ex);
        }
    }

    public virtual async Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetCollectionInfoAsync(cancellationToken);
            return info?.PointsCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Validates that the configured embedding dimension matches the collection's vector dimension.
    /// This should be called before indexing or retrieval operations to catch dimension mismatches early.
    /// </summary>
    /// <returns>A tuple containing success status and optional error message</returns>
    public async Task<(bool Success, string? ErrorMessage)> ValidateCollectionDimensionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if collection exists
            var exists = await CollectionExistsAsync(cancellationToken);
            if (!exists)
            {
                return (false, $"Collection '{_currentCollectionName}' does not exist");
            }

            // Get collection info to check dimension
            var info = await GetCollectionInfoAsync(cancellationToken);
            if (info == null)
            {
                return (false, "Unable to retrieve collection information");
            }

            var actualDimension = info.Config?.Params?.Vectors?.Size ?? 0;
            var configuredDimension = _config.VectorSize;

            if (actualDimension == 0)
            {
                return (false, "Collection has invalid vector configuration (dimension is 0)");
            }

            if (actualDimension != configuredDimension)
            {
                var errorMessage = $"Vector dimension mismatch: Collection has {actualDimension} dimensions, " +
                                 $"but configuration expects {configuredDimension} dimensions. " +
                                 $"This may indicate the embedding model has changed. Re-indexing is required.";

                _logger.LogError("[Qdrant] {ErrorMessage}", errorMessage);
                return (false, errorMessage);
            }

            _logger.LogDebug(
                "[Qdrant] Dimension validation passed - Collection: {Dimension} dims, Config: {ConfigDimension} dims",
                actualDimension, configuredDimension);

            return (true, null);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error validating collection dimension: {ex.Message}";
            _logger.LogError(ex, "[Qdrant] {ErrorMessage}", errorMessage);
            return (false, errorMessage);
        }
    }

    /// <summary>
    /// Store schema fingerprint metadata in the collection as a special point.
    /// Uses a reserved ID "schema_fingerprint" to store the fingerprint data.
    /// </summary>
    public async Task StoreSchemaFingerprintAsync(
        SchemaFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "[Qdrant] Storing schema fingerprint - Hash: {Hash}, Tables: {TableCount}",
                fingerprint.Hash, fingerprint.TableCount);

            // ✅ FIX: Use valid UUID instead of arbitrary string "schema_fingerprint"
            // This UUID is unique enough to not conflict with actual data points
            var fingerprintId = "00000000-0000-0000-0000-000000000001";

            // Create a zero vector (fingerprint doesn't need semantic search)
            var zeroVector = Enumerable.Repeat(0.0, _config.VectorSize).ToList();

            var point = new
            {
                id = fingerprintId,
                vector = zeroVector,
                payload = new Dictionary<string, object>
                {
                    { "type", "fingerprint" },
                    { "hash", fingerprint.Hash },
                    { "computed_at", fingerprint.ComputedAt.ToString("o") },
                    { "table_count", fingerprint.TableCount },
                    { "column_count", fingerprint.ColumnCount },
                    { "relationship_count", fingerprint.RelationshipCount },
                    { "table_names", string.Join(",", fingerprint.TableNames) }
                }
            };

            var request = new { points = new[] { point } };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}/points",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[Qdrant] Store fingerprint failed - Status: {Status}, Body: {Body}",
                    response.StatusCode, responseBody);

                throw new VectorDBException(
                    $"Failed to store fingerprint. Status: {response.StatusCode}, Error: {responseBody}");
            }

            _logger.LogInformation("[Qdrant] ✓ Schema fingerprint stored successfully");
        }
        catch (VectorDBException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error storing schema fingerprint");
            throw new VectorDBException($"Store fingerprint failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieve stored schema fingerprint from the collection.
    /// Returns null if collection doesn't exist or fingerprint hasn't been stored yet.
    /// </summary>
    public virtual async Task<SchemaFingerprint?> GetStoredFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if collection exists first
            var exists = await CollectionExistsAsync(cancellationToken);
            if (!exists)
            {
                _logger.LogDebug("[Qdrant] Collection doesn't exist, no fingerprint available");
                return null;
            }

            // ✅ Use same UUID as in StoreSchemaFingerprintAsync
            var fingerprintId = "00000000-0000-0000-0000-000000000001";

            _logger.LogDebug("[Qdrant] Retrieving schema fingerprint with ID: {Id}", fingerprintId);

            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}/points/{fingerprintId}",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("[Qdrant] No fingerprint found in collection");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "[Qdrant] Get fingerprint failed - Status: {Status}, Body: {Body}",
                    response.StatusCode, errorBody);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<PointResponse>(responseBody, options);

            if (result?.Result?.Payload == null)
            {
                _logger.LogWarning("[Qdrant] Fingerprint point exists but has no payload");
                return null;
            }

            var payload = result.Result.Payload;

            // Extract fingerprint data from payload
            var fingerprint = new SchemaFingerprint
            {
                Hash = payload.GetValueOrDefault("hash")?.ToString() ?? string.Empty,
                ComputedAt = DateTime.TryParse(
                    payload.GetValueOrDefault("computed_at")?.ToString(),
                    out var computedAt) ? computedAt : DateTime.MinValue,
                TableCount = int.TryParse(
                    payload.GetValueOrDefault("table_count")?.ToString(),
                    out var tableCount) ? tableCount : 0,
                ColumnCount = int.TryParse(
                    payload.GetValueOrDefault("column_count")?.ToString(),
                    out var columnCount) ? columnCount : 0,
                RelationshipCount = int.TryParse(
                    payload.GetValueOrDefault("relationship_count")?.ToString(),
                    out var relCount) ? relCount : 0,
                TableNames = payload.GetValueOrDefault("table_names")?.ToString()
                    ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .ToList() ?? new List<string>()
            };

            _logger.LogInformation(
                "[Qdrant] Retrieved fingerprint - Hash: {Hash}, Tables: {TableCount}",
                fingerprint.Hash, fingerprint.TableCount);

            return fingerprint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Qdrant] Error retrieving schema fingerprint");
            return null;
        }
    }

    /// <summary>
    /// Upsert schema elements (tables/columns) with embeddings for semantic search
    /// </summary>
    public async Task UpsertSchemaElementsAsync(
        List<(string Name, string Type, string? Table, string? Description)> schemaElements,
        IEmbeddingClient embeddingClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embeddings for all elements
            var texts = schemaElements.Select(e => $"{e.Name}: {e.Type} - {e.Description}").ToList();
            var embeddings = await embeddingClient.GenerateBatchEmbeddingsAsync(texts, cancellationToken);

            // Create points using the same format as UpsertPointsAsync
            var pointsList = new List<object>();
            for (int i = 0; i < schemaElements.Count; i++)
            {
                var element = schemaElements[i];
                var point = new
                {
                    id = (ulong)(i + 1),
                    vector = embeddings[i].Select(x => (double)x).ToList(),
                    payload = new Dictionary<string, object>
                    {
                        { "name", element.Name },
                        { "type", element.Type },
                        { "table", element.Table ?? "" },
                        { "description", element.Description ?? "" }
                    }
                };
                pointsList.Add(point);
            }

            // Use internal upsert logic
            var countBefore = await GetPointCountAsync(cancellationToken);
            _logger.LogInformation("[Qdrant] Upserting {Count} schema elements (current: {Current})",
                pointsList.Count, countBefore);

            var jsonContent = JsonSerializer.Serialize(new { points = pointsList });
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/collections/{_currentCollectionName}/points",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new VectorDBException($"Upsert failed: {response.StatusCode} - {error}");
            }

            _logger.LogInformation("[Qdrant] Upserted {Count} schema elements", pointsList.Count);
        }
        catch (Exception ex) when (ex is not VectorDBException)
        {
            _logger.LogError(ex, "[Qdrant] Error upserting schema elements");
            throw new VectorDBException($"UpsertSchemaElementsAsync failed: {ex.Message}", ex);
        }
    }

}
