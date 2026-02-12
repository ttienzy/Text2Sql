using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.VectorDB;

public class QdrantService
{
    private readonly HttpClient _httpClient;
    private readonly QdrantConfig _config;
    private readonly ILogger<QdrantService> _logger;
    private readonly string _baseUrl;
    private string _currentCollectionName;

    public QdrantService(QdrantConfig config, ILogger<QdrantService> logger)
    {
        _config = config;
        _logger = logger;
        _currentCollectionName = config.CollectionName;

        // âœ… FIX: Always use REST API port 6333 (not gRPC 6334)
        _baseUrl = $"http://{config.Host}:6333";
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _logger.LogInformation(
            "[Qdrant] Initialized - URL: {BaseUrl}, VectorSize: {VectorSize}",
            _baseUrl, config.VectorSize);
    }

    public void SetCollectionName(string databaseName)
    {
        var sanitized = new string(databaseName
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray())
            .ToLowerInvariant();

        _currentCollectionName = $"{_config.CollectionName}_{sanitized}";
        _logger.LogInformation("[Qdrant] Collection name: {CollectionName}", _currentCollectionName);
    }

    public string GetCurrentCollectionName() => _currentCollectionName;

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
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

    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
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
            // âœ… Check before upsert
            var countBefore = await GetPointCountAsync(cancellationToken);

            _logger.LogInformation(
                "[Qdrant] Upserting {Count} points (current: {Current})",
                points.Count, countBefore);

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

            // âœ… Validate first point
            var first = pointsList.FirstOrDefault();
            if (first != null)
            {
                var vectorDim = first.vector?.Count ?? 0;

                _logger.LogDebug(
                    "[Qdrant] Sample point - ID: {Id}, VectorDim: {VectorDim}, Payload: {Payload}",
                    first.id, vectorDim,
                    string.Join(", ", first.payload?.Keys ?? Enumerable.Empty<string>()));


                // âœ… CRITICAL: Dimension check
                if (vectorDim != _config.VectorSize)
                {
                    throw new VectorDBException(
                        $"âŒ Vector dimension mismatch!\n" +
                        $"Point vector: {vectorDim} dims\n" +
                        $"Collection expects: {_config.VectorSize} dims\n" +
                        $"Check your embedding model configuration!");
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

            // âœ… Wait for indexing + verify
            await Task.Delay(200, cancellationToken);
            var countAfter = await GetPointCountAsync(cancellationToken);

            _logger.LogInformation(
                "[Qdrant] âœ“ Upsert complete - Before: {Before}, After: {After}, Delta: {Delta}",
                countBefore, countAfter, countAfter - countBefore);

            if (countAfter == 0)
            {
                _logger.LogError(
                    "[Qdrant] âŒ CRITICAL: Point count is 0 after upsert! " +
                    "Data may not be persisted. Check Qdrant logs.");
            }
            else if (countAfter == countBefore)
            {
                _logger.LogWarning(
                    "[Qdrant] âš ï¸ Point count unchanged. Points may have been replaced, not added.");
            }
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

    public async Task<List<ScoredPoint>> SearchAsync(
        float[] queryVector,
        ulong limit = 5,
        double scoreThreshold = 0.7,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be null or empty");
        }

        // âœ… Validate vector dimension
        if (queryVector.Length != _config.VectorSize)
        {
            throw new ArgumentException(
                $"Query vector dimension ({queryVector.Length}) " +
                $"doesn't match collection ({_config.VectorSize})");
        }

        try
        {
            _logger.LogDebug(
                "[Qdrant] Search - Limit: {Limit}, Threshold: {Threshold}, VectorDim: {VectorDim}",
                limit, scoreThreshold, queryVector.Length);

            var request = new
            {
                vector = queryVector,
                limit = (int)limit,
                score_threshold = scoreThreshold,
                with_payload = true
            };

            var json = JsonSerializer.Serialize(request);
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

            _logger.LogInformation("[Qdrant] Found {Count} results", scoredPoints.Count);

            if (scoredPoints.Count == 0)
            {
                var pointCount = await GetPointCountAsync(cancellationToken);
                _logger.LogWarning(
                    "[Qdrant] âš ï¸ No results found. Collection has {Count} points. " +
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

    public async Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
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

    // âœ… FIXED DTOs with correct JSON property names
    public class SearchResponse
    {
        [JsonPropertyName("result")]
        public List<SearchResult>? Result { get; set; }
    }

    public class SearchResult
    {
        [JsonPropertyName("id")]
        public JsonElement Id { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, object>? Payload { get; set; }
    }

    public class CollectionInfoResponse
    {
        [JsonPropertyName("result")]
        public CollectionInfo? Result { get; set; }
    }

    public class CollectionInfo
    {
        [JsonPropertyName("points_count")]
        public long PointsCount { get; set; }

        [JsonPropertyName("config")]
        public CollectionConfig? Config { get; set; }
    }

    public class CollectionConfig
    {
        [JsonPropertyName("params")]
        public CollectionParams? Params { get; set; }
    }

    public class CollectionParams
    {
        [JsonPropertyName("vectors")]
        public VectorConfig? Vectors { get; set; }
    }

    public class VectorConfig
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("distance")]
        public string? Distance { get; set; }
    }
}

