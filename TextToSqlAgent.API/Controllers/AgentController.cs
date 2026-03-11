using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly IAgent _agent;
    private readonly TextToSqlAgentOrchestrator _legacyOrchestrator;
    private readonly SchemaScanner _schemaScanner;
    private readonly QueryValidatorPlugin _queryValidator;
    private readonly IQueryRouter _queryRouter;
    private readonly ILogger<AgentController> _logger;
    private readonly bool _useLegacyMode;
    private readonly bool _enableRouting;
    private readonly bool _enableDevQueryEndpoint;

    public AgentController(
        IAgent agent,
        TextToSqlAgentOrchestrator legacyOrchestrator,
        SchemaScanner schemaScanner,
        QueryValidatorPlugin queryValidator,
        IQueryRouter queryRouter,
        ILogger<AgentController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _agent = agent;
        _legacyOrchestrator = legacyOrchestrator;
        _schemaScanner = schemaScanner;
        _queryValidator = queryValidator;
        _queryRouter = queryRouter;
        _logger = logger;

        // Allow switching between legacy and new agent via config
        _useLegacyMode = configuration.GetValue<bool>("Agent:UseLegacyMode", false);
        _enableRouting = configuration.GetValue<bool>("Agent:EnableRouting", true);
        _enableDevQueryEndpoint = environment.IsDevelopment()
            && configuration.GetValue<bool>("Agent:EnableDevQueryEndpoint", false);
    }

    [HttpPost("query")]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteQuery([FromBody] QueryRequest? request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new QueryResponse
            {
                Success = false,
                ErrorMessage = "Request body is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new QueryResponse
            {
                Success = false,
                ErrorMessage = "Question cannot be empty"
            });
        }

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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            // ✅ NEW: Query Routing (if enabled)
            if (_enableRouting && !_useLegacyMode)
            {
                _logger.LogInformation("Using intelligent query routing");
                return await ExecuteWithRoutingAsync(request, cts.Token);
            }

            // Use legacy mode if configured
            if (_useLegacyMode)
            {
                _logger.LogInformation("Using legacy orchestrator mode");
                return await ExecuteWithLegacyOrchestrator(request, cts.Token);
            }

            // Use new ReAct Agent
            _logger.LogInformation("Using ReAct Agent mode");
            return await ExecuteWithReActAgent(request, cts.Token);
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

    private async Task<IActionResult> ExecuteWithRoutingAsync(QueryRequest request, CancellationToken ct)
    {
        try
        {
            // Step 1: Validate query
            var schema = await _schemaScanner.ScanAsync(ct);
            var tableNames = schema.Tables.Select(t => t.TableName).ToList();
            var validation = await _queryValidator.ValidateQueryAsync(request.Question, tableNames, ct);

            _logger.LogInformation(
                "[Routing] Query type: {Type}, Relevant: {Relevant}, Confidence: {Confidence:P0}",
                validation.QueryType,
                validation.IsRelevant,
                validation.Confidence);

            // Step 2: Route query
            var route = await _queryRouter.RouteAsync(request.Question, validation, ct);

            _logger.LogInformation(
                "[Routing] Route decision: {Type} - {Reasoning}",
                route.Type,
                route.Reasoning);

            // Step 3: Execute based on route
            return route.Type switch
            {
                QueryRouteType.DirectResponse => HandleDirectResponse(route),
                QueryRouteType.NeedsClarification => HandleClarification(route),
                QueryRouteType.UseTool => await HandleToolExecution(route, ct),
                QueryRouteType.UseAgent => await ExecuteWithReActAgent(request, ct),
                _ => await ExecuteWithReActAgent(request, ct)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing] Error in routing execution");
            return StatusCode(500, new QueryResponse
            {
                Success = false,
                ErrorMessage = $"Routing error: {ex.Message}"
            });
        }
    }

    private IActionResult HandleDirectResponse(QueryRoute route)
    {
        return Ok(new QueryResponse
        {
            Success = true,
            Answer = route.DirectResponse,
            ProcessingSteps = new List<string> { "Direct response (no agent execution)" },
            Metadata = new Dictionary<string, object>
            {
                ["route_type"] = "DirectResponse",
                ["reasoning"] = route.Reasoning ?? ""
            }
        });
    }

    private IActionResult HandleClarification(QueryRoute route)
    {
        return Ok(new QueryResponse
        {
            Success = false,
            Answer = route.DirectResponse,
            ProcessingSteps = new List<string> { "Clarification needed" },
            Metadata = new Dictionary<string, object>
            {
                ["route_type"] = "NeedsClarification",
                ["reasoning"] = route.Reasoning ?? ""
            }
        });
    }

    private async Task<IActionResult> HandleToolExecution(QueryRoute route, CancellationToken ct)
    {
        // For now, delegate to agent
        // In future, could execute tool directly
        _logger.LogInformation("[Routing] Tool execution delegated to agent: {Tool}", route.ToolName);

        // P1-04: Consistent parameter key handling (prefer 'question' over 'query')
        var routedQuestion = route.Parameters.TryGetValue("question", out var questionValue)
            ? questionValue?.ToString()
            : route.Parameters.TryGetValue("query", out var queryValue)
                ? queryValue?.ToString()
                : null;

        if (string.IsNullOrWhiteSpace(routedQuestion))
        {
            return BadRequest(new QueryResponse
            {
                Success = false,
                ErrorMessage = "Routing error: missing 'question' parameter in route"
            });
        }

        var agentRequest = new AgentRequest(routedQuestion, null);
        var result = await _agent.RunAsync(agentRequest, ct);

        // P1-04: Consistent result mapping
        var executionResult = TryExtractExecutionResult(result.QueryResult);
        object? resultPayload = executionResult?.Rows ?? result.QueryResult;
        int rowCount = executionResult?.RowCount ?? CountRows(result.QueryResult);

        return Ok(new QueryResponse
        {
            Success = result.Success,
            Answer = result.Answer,
            SqlGenerated = result.SqlGenerated,
            Result = resultPayload,
            RowCount = rowCount,
            ProcessingSteps = result.Steps.Select(s => $"Step {s.StepNumber}: {s.Thought}").ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["route_type"] = "UseTool",
                ["tool_name"] = route.ToolName ?? "",
                ["reasoning"] = route.Reasoning ?? "",
                ["has_execution_result"] = executionResult != null
            }
        });
    }

    private async Task<IActionResult> ExecuteWithReActAgent(QueryRequest request, CancellationToken ct)
    {
        var agentRequest = new AgentRequest(request.Question, null);
        var result = await _agent.RunAsync(agentRequest, ct);

        // P1-04: Safe extraction of execution result
        var executionResult = TryExtractExecutionResult(result.QueryResult);

        // P1-04: Consistent result mapping
        object? resultPayload = null;
        int rowCount = 0;

        if (executionResult != null)
        {
            // Use SqlExecutionResult structure
            resultPayload = executionResult.Rows;
            rowCount = executionResult.RowCount;
        }
        else if (result.QueryResult != null)
        {
            // Fallback: use raw QueryResult
            resultPayload = result.QueryResult;
            rowCount = CountRows(result.QueryResult);
        }

        var response = new QueryResponse
        {
            Success = result.Success,
            SqlGenerated = result.SqlGenerated,
            Result = resultPayload,
            RowCount = rowCount,
            ErrorMessage = result.ErrorMessage,
            ProcessingSteps = result.ProcessingSteps.Count > 0
                ? result.ProcessingSteps
                : result.Steps.Select(s => $"Step {s.StepNumber}: {s.Thought}").ToList(),
            Answer = result.Answer,
            WasCorrected = false, // ReAct agent handles this differently
            CorrectionAttempts = 0,
            Metadata = new Dictionary<string, object>
            {
                ["agent_type"] = "ReAct",
                ["total_steps"] = result.TotalSteps,
                ["tokens_used"] = result.TotalTokensUsed,
                ["latency_ms"] = result.TotalLatencyMs,
                ["from_cache"] = result.FromCache,
                ["has_execution_result"] = executionResult != null,
                ["execution_time_ms"] = executionResult?.ExecutionTimeMs ?? 0
            }
        };

        if (!result.Success)
        {
            _logger.LogWarning("ReAct Agent processing failed: {Error}", result.ErrorMessage);
            return StatusCode(500, response);
        }

        return Ok(response);
    }

    private async Task<IActionResult> ExecuteWithLegacyOrchestrator(QueryRequest request, CancellationToken ct)
    {
        var result = await _legacyOrchestrator.ProcessQueryAsync(request.Question, ct);

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
            CorrectionAttempts = result.CorrectionAttempts,
            Metadata = new Dictionary<string, object>
            {
                ["agent_type"] = "Legacy Pipeline"
            }
        };

        if (!result.Success)
        {
            _logger.LogWarning("Legacy orchestrator processing failed: {Error}", result.ErrorMessage);
            return StatusCode(500, response);
        }

        return Ok(response);
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
                    Schema = t.Schema,
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
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefreshSchema(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Manual schema refresh triggered");

            // Clear cache
            _legacyOrchestrator.ClearSchemaCache();

            // Re-scan database schema
            var schema = await _schemaScanner.ScanAsync(cancellationToken);

            // Re-index to vector store
            var schemaIndexer = HttpContext.RequestServices.GetRequiredService<SchemaIndexer>();
            await schemaIndexer.IndexSchemaAsync(schema, cancellationToken);

            _logger.LogInformation("Schema refreshed and re-indexed successfully");

            return Ok(new
            {
                Message = "Schema refreshed and re-indexed successfully",
                TablesCount = schema.Tables.Count,
                RelationshipsCount = schema.Relationships.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing schema");
            return StatusCode(500, new { Message = $"Error refreshing schema: {ex.Message}" });
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
            Version = "2.0.0",
            AgentType = _useLegacyMode ? "Legacy Pipeline" : "ReAct Agent"
        });
    }

    [HttpGet("mode")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetMode()
    {
        return Ok(new
        {
            Mode = _useLegacyMode ? "Legacy" : "ReAct",
            Description = _useLegacyMode
                ? "Using legacy pipeline orchestrator (fixed steps)"
                : "Using ReAct Agent (autonomous reasoning and acting)"
        });
    }
    [AllowAnonymous]
    [HttpPost("dev/query")]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DevQuery([FromBody] QueryRequest? request, CancellationToken cancellationToken)
    {
        // P1-05: Security hardening - gate dev endpoint
        if (!_enableDevQueryEndpoint)
        {
            _logger.LogWarning("Attempt to access disabled dev endpoint from {IP}", HttpContext.Connection.RemoteIpAddress);
            return NotFound(new QueryResponse
            {
                Success = false,
                ErrorMessage = "Endpoint not available"
            });
        }

        // P1-05: Log security warning
        _logger.LogWarning(
            "[SECURITY] Using development endpoint without authentication! IP: {IP}, Question: {Question}",
            HttpContext.Connection.RemoteIpAddress,
            request?.Question?.Substring(0, Math.Min(50, request.Question?.Length ?? 0)));

        return await ExecuteQuery(request, cancellationToken);
    }

    /// <summary>
    /// P1-04: Safe extraction of SqlExecutionResult from QueryResult
    /// Handles both typed objects and JsonElement
    /// </summary>
    private static SqlExecutionResult? TryExtractExecutionResult(object? queryResult)
    {
        if (queryResult == null)
            return null;

        // Direct type match
        if (queryResult is SqlExecutionResult typedResult)
        {
            return typedResult;
        }

        // JsonElement from LLM responses
        if (queryResult is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            try
            {
                return jsonElement.Deserialize<SqlExecutionResult>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                // Log but don't throw - fallback to raw result
                System.Diagnostics.Debug.WriteLine($"Failed to deserialize SqlExecutionResult: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// P1-04: Safe row counting for various result types
    /// </summary>
    private static int CountRows(object? resultPayload)
    {
        if (resultPayload == null)
            return 0;

        // ICollection (List, Array, etc.)
        if (resultPayload is System.Collections.ICollection collection)
        {
            return collection.Count;
        }

        // JsonElement array
        if (resultPayload is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            return jsonElement.GetArrayLength();
        }

        // Single object = 1 row
        return 1;
    }
}
