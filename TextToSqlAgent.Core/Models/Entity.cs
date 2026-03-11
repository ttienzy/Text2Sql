namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents an entity extracted from natural language question
/// </summary>
public class Entity
{
    public string Text { get; set; } = string.Empty;
    public EntityType Type { get; set; }
    public double Confidence { get; set; }
    public string? MappedTo { get; set; } // Mapped schema element
}

public enum EntityType
{
    Table,
    Column,
    Value,
    Operator,
    Aggregation,
    TimeRange
}

/// <summary>
/// Result of entity recognition
/// </summary>
public class EntityRecognitionResult
{
    public List<Entity> Tables { get; set; } = new();
    public List<Entity> Columns { get; set; } = new();
    public List<Entity> Values { get; set; } = new();
    public List<Entity> Operators { get; set; } = new();
    public List<Entity> Aggregations { get; set; } = new();
    public List<Entity> TimeRanges { get; set; } = new();

    public List<Entity> AllEntities =>
        Tables.Concat(Columns).Concat(Values)
              .Concat(Operators).Concat(Aggregations)
              .Concat(TimeRanges).ToList();
}
