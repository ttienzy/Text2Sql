namespace TextToSqlAgent.API.DTOs.QueryOptimizer;

/// <summary>
/// Response for query optimization with execution plan comparison
/// </summary>
public class OptimizeQueryWithPlanResponse : OptimizeQueryResponse
{
    public PlanComparisonDto? PlanComparison { get; set; }
}
