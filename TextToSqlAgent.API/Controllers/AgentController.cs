using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteQuery([FromBody] QueryRequest? request, CancellationToken cancellationToken)
    {
        // ✅ FIX: Validate request is not null
        if (request == null)
        {
            return BadRequest(new QueryResponse 
            { 
                Success = false, 
                ErrorMessage = "Request body is required" 
            });
        }

        // ✅ FIX: Validate question
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new QueryResponse 
            { 
                Success = false, 
                ErrorMessage = "Question cannot be empty" 
            });
        }

        // ✅ FIX: Validate question length to prevent buffer overflow
        if (request.Question.Length > 10000)
        {
            return BadRequest(new QueryResponse 
            { 
                Success = false, 
                ErrorMessage = "Question is too long. Maximum 10,000 characters allowed." 
            });
        }

        try
        {
            _logger.LogInformation("Received query: {Question}", 
                request.Question.Length > 100 ? request.Question.Substring(0, 100) + "..." : request.Question);

            // ✅ FIX: Add timeout handling
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // 5 minute timeout

            var result = await _agent.ProcessQueryAsync(request.Question, cts.Token);

            var response = new QueryResponse
            {
                Success = result.Success,
                SqlGenerated = result.SqlGenerated,
                Result = result.QueryResult?.Rows,
                RowCount = result.QueryResult?.RowCount ?? 0,
                ErrorMessage = result.ErrorMessage,
                ProcessingSteps = result.ProcessingSteps,
                Answer = result.Answer,
                WasCorrected = result.WasCorrected,
                CorrectionAttempts = result.CorrectionAttempts
            };

            // ✅ FIX: Return appropriate status based on result
            if (!result.Success)
            {
                _logger.LogWarning("Query processing failed: {Error}", result.ErrorMessage);
                return StatusCode(500, response);
            }

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Query timeout for question: {Question}", request.Question);
            return StatusCode(504, new QueryResponse 
            { 
                Success = false, 
                ErrorMessage = "Query processing timed out. Please try again or simplify your question." 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing query: {Question}", request.Question);
            return StatusCode(500, new QueryResponse 
            { 
                Success = false, 
                ErrorMessage = $"Error processing query: {ex.Message}" 
            });
        }
    }

    [HttpGet("schema")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSchema(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting database schema");
            
            var schema = await _schemaScanner.ScanAsync(cancellationToken);
            
            return Ok(new 
            { 
                TablesCount = schema.Tables.Count,
                Tables = schema.Tables.Select(t => new 
                {
                    t.TableName,
                    t.SchemaName,
                    ColumnCount = t.Columns.Count,
                    Columns = t.Columns.Select(c => new 
                    {
                        c.ColumnName,
                        c.DataType,
                        c.IsNullable,
                        c.IsPrimaryKey,
                        c.IsForeignKey
                    })
                }),
                RelationshipsCount = schema.Relationships.Count
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Schema scan cancelled");
            return StatusCode(504, new { Message = "Schema scan timed out" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schema");
            return StatusCode(500, new { Message = $"Error getting schema: {ex.Message}" });
        }
    }

    [HttpPost("schema/refresh")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult RefreshSchema()
    {
        try
        {
            _agent.ClearSchemaCache();
            _logger.LogInformation("Schema cache cleared");
            return Ok(new { Message = "Schema cache cleared. Next query will trigger a re-scan." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing schema cache");
            return StatusCode(500, new { Message = $"Error clearing cache: {ex.Message}" });
        }
    }

    [AllowAnonymous]
    [HttpGet("health")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new 
        { 
            Status = "Healthy", 
            Timestamp = DateTime.UtcNow,
            Service = "TextToSqlAgent API",
            Version = "1.0.0"
        });
    }
}
