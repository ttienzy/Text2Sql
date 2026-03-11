namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Unified request model for Console and API
/// </summary>
public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public QueryOptions? Options { get; set; }
}

public class QueryOptions
{
    public bool ExplainQuery { get; set; }
    public bool ShowSteps { get; set; }
    public int MaxRows { get; set; } = 1000;
}

/// <summary>
/// Unified response model for Console and API
/// </summary>
public class QueryResponse
{
    public bool Success { get; set; }
    public string? Answer { get; set; }
    public string? SqlGenerated { get; set; }
    public SqlExecutionResult? QueryResult { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ProcessingSteps { get; set; } = new();
    public List<CorrectionAttempt> CorrectionHistory { get; set; } = new();
    public string? QueryExplanation { get; set; }
    public int CorrectionAttempts => CorrectionHistory.Count;
    public bool WasCorrected => CorrectionHistory.Any();
}

/// <summary>
/// Enhanced intent analysis result with rich structure
/// </summary>
public class IntentAnalysisResult
{
    public QueryIntent Intent { get; set; }
    public QueryComplexity Complexity { get; set; }
    public string Target { get; set; } = string.Empty;
    public List<string> RelatedEntities { get; set; } = new();
    public List<MetricDefinition> Metrics { get; set; } = new();
    public List<FilterDefinition> Filters { get; set; } = new();
    public List<string> GroupBy { get; set; } = new();
    public List<OrderByDefinition> OrderBy { get; set; } = new();
    public int? Limit { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
}

public class FilterDefinition
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public FilterValueType ValueType { get; set; }
    public string LogicalOperator { get; set; } = "AND";
}

public enum FilterValueType
{
    Literal,
    Expression,
    Parameter
}

public class OrderByDefinition
{
    public string Field { get; set; } = string.Empty;
    public string Direction { get; set; } = "ASC";
}

public enum QueryComplexity
{
    Simple,
    Medium,
    Complex,
    Advanced
}

/// <summary>
/// SQL correction result
/// </summary>
public class CorrectionResult
{
    public bool Success { get; set; }
    public string CorrectedSql { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
