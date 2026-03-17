using Microsoft.AspNetCore.Mvc;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Security;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Production-ready agent controller with security features
/// </summary>
[ApiController]
[Route("api/v2/[controller]")]
public class ProductionAgentController : ControllerBase
{
    private readonly IAgent _agent;
    private readonly SqlInjectionPrevention _sqlInjectionPrevention;
    private readonly QueryCostEstimator _costEstimator;
    private readonly ILogger<ProductionAgentController> _logger;

    public ProductionAgentController(
        IAgent agent,
        SqlInjectionPrevention sqlInjectionPrevention,
        QueryCostEstimator costEstimator,
        ILogger<ProductionAgentController> logger)
    {
        _agent = agent;
        _sqlInjectionPrevention = sqlInjectionPrevention;
        _costEstimator = costEstimator;
        _logger = logger;
    }

    /// <summary>
    /// Execute natural language query with security checks
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> ExecuteQuery([FromBody] QueryRequest request)
    {
        try
        {
            // Validate question input
            var questionValidation = _sqlInjectionPrevention.ValidateQuestion(request.Question);
            if (!questionValidation.IsValid)
            {
                _logger.LogWarning(
                    "Invalid question from {IP}: {Errors}",
                    HttpContext.Connection.RemoteIpAddress,
                    string.Join(", ", questionValidation.Errors));

                return BadRequest(new
                {
                    error = "Invalid question",
                    details = questionValidation.Errors
                });
            }

            // Execute agent
            var agentRequest = new AgentRequest(request.Question, null);
            var result = await _agent.RunAsync(agentRequest, HttpContext.RequestAborted);

            if (!result.Success)
            {
                return Ok(new
                {
                    success = false,
                    error = result.ErrorMessage,
                    steps = result.Steps.Count,
                    latency_ms = result.TotalLatencyMs
                });
            }

            // Validate generated SQL
            var sqlValidation = _sqlInjectionPrevention.ValidateSql(result.SqlGenerated ?? "");
            if (!sqlValidation.IsValid)
            {
                _logger.LogError(
                    "Generated SQL failed validation: {Errors}\nSQL: {Sql}",
                    string.Join(", ", sqlValidation.Errors),
                    result.SqlGenerated);

                return Ok(new
                {
                    success = false,
                    error = "Generated SQL failed security validation",
                    details = sqlValidation.Errors
                });
            }

            // Estimate query cost
            var costEstimation = _costEstimator.EstimateCost(result.SqlGenerated ?? "");
            if (!costEstimation.IsAllowed)
            {
                _logger.LogWarning(
                    "Query denied due to cost: {Reason}\nSQL: {Sql}",
                    costEstimation.DenialReason,
                    result.SqlGenerated);

                return Ok(new
                {
                    success = false,
                    error = "Query exceeds cost limits",
                    reason = costEstimation.DenialReason,
                    cost_estimation = new
                    {
                        complexity_score = costEstimation.ComplexityScore,
                        estimated_cost = costEstimation.EstimatedCost,
                        estimated_time_ms = costEstimation.EstimatedExecutionTimeMs
                    }
                });
            }

            // Return successful result
            return Ok(new
            {
                success = true,
                sql = result.SqlGenerated,
                result = result.QueryResult,
                metadata = new
                {
                    steps = result.Steps.Count,
                    tokens_used = result.TotalTokensUsed,
                    latency_ms = result.TotalLatencyMs,
                    from_cache = result.FromCache,
                    complexity = costEstimation.ComplexityLevel.ToString(),
                    complexity_score = costEstimation.ComplexityScore,
                    estimated_cost = costEstimation.EstimatedCost,
                    warnings = costEstimation.Warnings
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query execution failed");
            return StatusCode(500, new
            {
                error = "Internal server error",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Validate SQL query without executing
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidateSql([FromBody] SqlValidationRequest request)
    {
        try
        {
            // Validate SQL
            var sqlValidation = _sqlInjectionPrevention.ValidateSql(request.Sql);

            // Estimate cost
            var costEstimation = _costEstimator.EstimateCost(request.Sql);

            return Ok(new
            {
                is_valid = sqlValidation.IsValid,
                is_allowed = costEstimation.IsAllowed,
                validation = new
                {
                    errors = sqlValidation.Errors
                },
                cost_estimation = new
                {
                    complexity = costEstimation.ComplexityLevel.ToString(),
                    complexity_score = costEstimation.ComplexityScore,
                    estimated_cost = costEstimation.EstimatedCost,
                    estimated_time_ms = costEstimation.EstimatedExecutionTimeMs,
                    denial_reason = costEstimation.DenialReason,
                    warnings = costEstimation.Warnings
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL validation failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get query cost estimation
    /// </summary>
    [HttpPost("estimate-cost")]
    public IActionResult EstimateCost([FromBody] SqlValidationRequest request)
    {
        try
        {
            var estimation = _costEstimator.EstimateCost(request.Sql);

            return Ok(new
            {
                complexity = estimation.ComplexityLevel.ToString(),
                complexity_score = estimation.ComplexityScore,
                estimated_cost = estimation.EstimatedCost,
                estimated_time_ms = estimation.EstimatedExecutionTimeMs,
                is_allowed = estimation.IsAllowed,
                denial_reason = estimation.DenialReason,
                warnings = estimation.Warnings,
                recommended_timeout_ms = _costEstimator.GetRecommendedTimeout(estimation),
                summary = estimation.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost estimation failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class SqlValidationRequest
{
    public string Sql { get; set; } = string.Empty;
}
