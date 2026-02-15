using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Infrastructure.Database;

namespace TextToSqlAgent.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly TextToSqlAgentOrchestrator _agent;
    private readonly SchemaScanner _schemaScanner;
    private readonly ILogger<AgentController> _logger;

    public AgentController(
        TextToSqlAgentOrchestrator agent,
        SchemaScanner schemaScanner,
        ILogger<AgentController> logger)
    {
        _agent = agent;
        _schemaScanner = schemaScanner;
        _logger = logger;
    }

    [HttpPost("query")]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteQuery([FromBody] QueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("Question cannot be empty");
        }

        try
        {
            _logger.LogInformation("Received query: {Question}", request.Question);

            var result = await _agent.ProcessQueryAsync(request.Question, cancellationToken);

            var response = new QueryResponse
            {
                Success = result.Success,
                SqlGenerated = result.SqlGenerated,
                Result = result.QueryResult?.Rows,
                ErrorMessage = result.ErrorMessage,
                ProcessingSteps = result.ProcessingSteps,
                Answer = result.Answer
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing query");
            return StatusCode(500, new QueryResponse { Success = false, ErrorMessage = ex.Message });
        }
    }

    [HttpGet("schema")]
    public async Task<IActionResult> GetSchema(CancellationToken cancellationToken)
    {
        // This is a simplified version. ideally we should expose a method in SchemaScanner or Agent to get cached schema
        // For now, we trigger a scan if not cached (Orchestrator handles this internally, but we want to inspect it)
        // Since Orchestrator.ProcessQueryAsync handles caching internally and private, we might need to expose it or use Scanner directly.
        // But Scanner scans from DB, it doesn't return cached.
        
        try
        {
            var schema = await _schemaScanner.ScanAsync(cancellationToken);
            return Ok(new 
            { 
                TablesCount = schema.Tables.Count,
                Tables = schema.Tables.Select(t => t.TableName),
                RelationshipsCount = schema.Relationships.Count
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("schema/refresh")]
    public async Task<IActionResult> RefreshSchema()
    {
        _agent.ClearSchemaCache();
        return Ok("Schema cache cleared. Next query will trigger a re-scan.");
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }
}
