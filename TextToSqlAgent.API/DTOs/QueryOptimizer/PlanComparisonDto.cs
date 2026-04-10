namespace TextToSqlAgent.API.DTOs.QueryOptimizer;

/// <summary>
/// Execution plan comparison result
/// </summary>
public class PlanComparisonDto
{
    public double OriginalCost { get; set; }
    public double OptimizedCost { get; set; }
    public double ImprovementFactor { get; set; }
    public double ImprovementPercentage { get; set; }
    public bool IsImproved { get; set; }
    public string ImprovementDescription { get; set; } = string.Empty;
    public List<PlanOperatorDto> OriginalOperators { get; set; } = new();
    public List<PlanOperatorDto> OptimizedOperators { get; set; } = new();
    public List<string> OriginalWarnings { get; set; } = new();
    public List<string> OptimizedWarnings { get; set; } = new();
}
