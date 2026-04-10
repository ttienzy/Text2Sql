namespace TextToSqlAgent.API.DTOs.QueryOptimizer;

/// <summary>
/// Execution plan operator
/// </summary>
public class PlanOperatorDto
{
    public string Type { get; set; } = string.Empty;
    public string LogicalOp { get; set; } = string.Empty;
    public double EstimatedCost { get; set; }
    public double EstimatedRows { get; set; }
    public double EstimatedCPU { get; set; }
    public double EstimatedIO { get; set; }
    public string? ObjectName { get; set; }
    public string? IndexName { get; set; }
}
