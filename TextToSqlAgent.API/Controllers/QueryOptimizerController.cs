using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextToSqlAgent.API.DTOs.QueryOptimizer;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services.QueryOptimizer;

namespace TextToSqlAgent.API.Controllers;

[ApiController]
[Route("api/query-optimizer")]
[Authorize]
public class QueryOptimizerController : ControllerBase
{
    private readonly QueryOptimizerService _queryOptimizerService;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IConnectionEncryptionService _encryptionService;

    public QueryOptimizerController(
        QueryOptimizerService queryOptimizerService,
        IConnectionRepository connectionRepository,
        IConnectionEncryptionService encryptionService)
    {
        _queryOptimizerService = queryOptimizerService;
        _connectionRepository = connectionRepository;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Analyzes and optimizes SQL query
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult<OptimizeQueryResponse>> AnalyzeQuery(
        [FromBody] OptimizeQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BadRequest(new { error = "SQL query is required" });
        }

        // Get connection string (decrypted)
        var connection = await _connectionRepository.GetByIdAsync(request.ConnectionId);
        if (connection == null)
        {
            return NotFound(new { error = "Connection not found" });
        }

        var connectionString = _encryptionService.GetConnectionString(connection);

        // Optimize query
        var result = await _queryOptimizerService.OptimizeAsync(
            request.Sql,
            connectionString,
            cancellationToken);

        // Map to response DTO
        var response = new OptimizeQueryResponse
        {
            OriginalSql = result.OriginalSql,
            OptimizedSql = result.OptimizedSql,
            IsChanged = result.IsChanged,
            Severity = result.Severity,
            DetectedIssues = result.DetectedIssues.Select(i => new AntiPatternDto
            {
                Code = i.Code,
                Severity = i.Severity.ToString().ToLower(),
                Title = i.Title,
                Description = i.Description,
                Impact = i.Impact,
                Location = i.Location
            }).ToList(),
            IssuesFixed = result.IssuesFixed,
            Explanation = result.Explanation,
            EstimatedImprovement = result.EstimatedImprovement,
            IndexSuggestions = result.IndexSuggestions,
            ComplexityScore = result.ComplexityScore,
            ModelUsed = result.ModelUsed,
            PreFlightAnalysis = result.PreFlightAnalysis
        };

        return Ok(response);
    }

    /// <summary>
    /// Analyzes and optimizes SQL query with execution plan comparison (Sprint 2)
    /// </summary>
    [HttpPost("analyze-with-plan")]
    public async Task<ActionResult<OptimizeQueryWithPlanResponse>> AnalyzeQueryWithPlan(
        [FromBody] OptimizeQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BadRequest(new { error = "SQL query is required" });
        }

        // Get connection string (decrypted)
        var connection = await _connectionRepository.GetByIdAsync(request.ConnectionId);
        if (connection == null)
        {
            return NotFound(new { error = "Connection not found" });
        }

        var connectionString = _encryptionService.GetConnectionString(connection);

        // Optimize query with plan comparison
        var result = await _queryOptimizerService.OptimizeWithPlanComparisonAsync(
            request.Sql,
            connectionString,
            cancellationToken);

        // Map to response DTO
        var response = new OptimizeQueryWithPlanResponse
        {
            OriginalSql = result.OriginalSql,
            OptimizedSql = result.OptimizedSql,
            IsChanged = result.IsChanged,
            Severity = result.Severity,
            DetectedIssues = result.DetectedIssues.Select(i => new AntiPatternDto
            {
                Code = i.Code,
                Severity = i.Severity.ToString().ToLower(),
                Title = i.Title,
                Description = i.Description,
                Impact = i.Impact,
                Location = i.Location
            }).ToList(),
            IssuesFixed = result.IssuesFixed,
            Explanation = result.Explanation,
            EstimatedImprovement = result.EstimatedImprovement,
            IndexSuggestions = result.IndexSuggestions,
            ComplexityScore = result.ComplexityScore,
            ModelUsed = result.ModelUsed,
            PreFlightAnalysis = result.PreFlightAnalysis,
            PlanComparison = result.PlanComparison != null ? new PlanComparisonDto
            {
                OriginalCost = result.PlanComparison.OriginalCost,
                OptimizedCost = result.PlanComparison.OptimizedCost,
                ImprovementFactor = result.PlanComparison.ImprovementFactor,
                ImprovementPercentage = result.PlanComparison.ImprovementPercentage,
                IsImproved = result.PlanComparison.IsImproved,
                ImprovementDescription = result.PlanComparison.ImprovementDescription,
                OriginalOperators = result.PlanComparison.OriginalOperators.Select(o => new PlanOperatorDto
                {
                    Type = o.Type,
                    LogicalOp = o.LogicalOp,
                    EstimatedCost = o.EstimatedCost,
                    EstimatedRows = o.EstimatedRows,
                    EstimatedCPU = o.EstimatedCPU,
                    EstimatedIO = o.EstimatedIO,
                    ObjectName = o.ObjectName,
                    IndexName = o.IndexName
                }).ToList(),
                OptimizedOperators = result.PlanComparison.OptimizedOperators.Select(o => new PlanOperatorDto
                {
                    Type = o.Type,
                    LogicalOp = o.LogicalOp,
                    EstimatedCost = o.EstimatedCost,
                    EstimatedRows = o.EstimatedRows,
                    EstimatedCPU = o.EstimatedCPU,
                    EstimatedIO = o.EstimatedIO,
                    ObjectName = o.ObjectName,
                    IndexName = o.IndexName
                }).ToList(),
                OriginalWarnings = result.PlanComparison.OriginalWarnings,
                OptimizedWarnings = result.PlanComparison.OptimizedWarnings
            } : null
        };

        return Ok(response);
    }
}
