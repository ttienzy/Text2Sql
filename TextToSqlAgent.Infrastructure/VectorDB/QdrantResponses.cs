using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextToSqlAgent.Infrastructure.VectorDB;

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

public class PointResponse
{
    [JsonPropertyName("result")]
    public PointResult? Result { get; set; }
}

public class PointResult
{
    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

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
