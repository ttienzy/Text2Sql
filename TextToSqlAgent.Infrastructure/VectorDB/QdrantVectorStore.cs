using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Infrastructure.VectorDB;

/// <summary>
/// Qdrant implementation of IVectorStore
/// Wraps existing QdrantService to conform to abstraction
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantService _qdrantService;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        QdrantService qdrantService,
        ILogger<QdrantVectorStore> logger)
    {
        _qdrantService = qdrantService;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get point count as health check
            await _qdrantService.GetPointCountAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[QdrantVectorStore] Health check failed");
            return false;
        }
    }

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        await _qdrantService.EnsureCollectionAsync(cancellationToken);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        var qdrantResults = await _qdrantService.SearchAsync(
            queryVector: queryVector,
            limit: (ulong)limit,
            scoreThreshold: scoreThreshold,
            cancellationToken: cancellationToken);

        // Convert Qdrant results to abstraction
        var results = qdrantResults.Select(r => new VectorSearchResult
        {
            Id = r.Id.Num.ToString(),
            Score = r.Score,
            Payload = r.Payload.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertQdrantValue(kvp.Value))
        }).ToList();

        return results;
    }

    public async Task UpsertPointsAsync(
        List<VectorPoint> points,
        CancellationToken cancellationToken = default)
    {
        // Convert abstraction points to Qdrant format
        var qdrantPoints = points.Select((p, index) => new Qdrant.Client.Grpc.PointStruct
        {
            Id = new Qdrant.Client.Grpc.PointId { Num = (ulong)(index + 1) },
            Vectors = new Qdrant.Client.Grpc.Vectors
            {
                Vector = new Qdrant.Client.Grpc.Vector { Data = { p.Vector } }
            },
            Payload =
            {
                p.Payload.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertToQdrantValue(kvp.Value))
            }
        }).ToList();

        await _qdrantService.UpsertPointsAsync(qdrantPoints, cancellationToken);
    }

    public async Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
    {
        return await _qdrantService.GetPointCountAsync(cancellationToken);
    }

    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        await _qdrantService.DeleteCollectionAsync(cancellationToken);
    }

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return await _qdrantService.CollectionExistsAsync(cancellationToken);
    }

    private static object ConvertQdrantValue(Qdrant.Client.Grpc.Value value)
    {
        return value.KindCase switch
        {
            Qdrant.Client.Grpc.Value.KindOneofCase.StringValue => value.StringValue,
            Qdrant.Client.Grpc.Value.KindOneofCase.IntegerValue => value.IntegerValue,
            Qdrant.Client.Grpc.Value.KindOneofCase.DoubleValue => value.DoubleValue,
            Qdrant.Client.Grpc.Value.KindOneofCase.BoolValue => value.BoolValue,
            _ => value.StringValue
        };
    }

    private static Qdrant.Client.Grpc.Value ConvertToQdrantValue(object value)
    {
        return value switch
        {
            string s => new Qdrant.Client.Grpc.Value { StringValue = s },
            int i => new Qdrant.Client.Grpc.Value { IntegerValue = i },
            long l => new Qdrant.Client.Grpc.Value { IntegerValue = l },
            double d => new Qdrant.Client.Grpc.Value { DoubleValue = d },
            float f => new Qdrant.Client.Grpc.Value { DoubleValue = f },
            bool b => new Qdrant.Client.Grpc.Value { BoolValue = b },
            _ => new Qdrant.Client.Grpc.Value { StringValue = value.ToString() ?? "" }
        };
    }
}
