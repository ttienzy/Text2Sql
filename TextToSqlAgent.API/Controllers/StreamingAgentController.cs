using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// LARGE-1: SSE Streaming endpoint for agent query processing.
/// Emits real-time stage_update events as the orchestrator progresses through pipeline stages.
/// Final result is emitted as a 'result' event, errors as 'error' events.
/// </summary>
[ApiController]
[Route("api/v2/agent")]
[Authorize]
public class StreamingAgentController : ControllerBase
{
    private readonly TextToSqlAgent.Application.Pipeline.PipelineOrchestrator _pipelineOrchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StreamingAgentController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TextToSqlAgent.Infrastructure.Services.ITokenQuotaService _tokenQuotaService;
    private readonly TextToSqlAgent.API.Services.IConnectionEncryptionService _encryptionService;

    public StreamingAgentController(
        TextToSqlAgent.Application.Pipeline.PipelineOrchestrator pipelineOrchestrator,
        IUnitOfWork unitOfWork,
        ILogger<StreamingAgentController> logger,
        IServiceProvider serviceProvider,
        TextToSqlAgent.Infrastructure.Services.ITokenQuotaService tokenQuotaService,
        TextToSqlAgent.API.Services.IConnectionEncryptionService encryptionService)
    {
        _pipelineOrchestrator = pipelineOrchestrator;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _tokenQuotaService = tokenQuotaService;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Process a query with real-time SSE streaming of pipeline stages.
    /// Content-Type: text/event-stream
    /// 
    /// Events emitted:
    ///   event: stage_update  — { stage, message, progress, detail, timestamp }
    ///   event: result        — { ...AgentResponse }
    ///   event: error         — { code, message }
    /// </summary>
    [HttpPost("process/stream")]
    public async Task ProcessStream([FromBody] StreamQueryRequest request)
    {
        var correlationId = HttpContext.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() : Guid.NewGuid().ToString("N");

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Correlation-Id", correlationId);

        var ct = HttpContext.RequestAborted;

        try
        {
            _logger.LogInformation("[StreamingAgent] SSE stream started for correlationId={CorrelationId}", correlationId);

            // ✅ Extract user ID and validate
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                await WriteSseEventAsync("error", new
                {
                    code = "UNAUTHORIZED",
                    message = "User ID not found in token",
                    correlationId
                }, ct);
                return;
            }

            // ✅ Validate ConnectionId is provided
            if (string.IsNullOrEmpty(request.ConnectionId))
            {
                await WriteSseEventAsync("error", new
                {
                    code = "BAD_REQUEST",
                    message = "ConnectionId is required",
                    correlationId
                }, ct);
                return;
            }

            // ✅ Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                await WriteSseEventAsync("error", new
                {
                    code = "CONNECTION_NOT_FOUND",
                    message = "Connection not found or access denied",
                    correlationId
                }, ct);
                return;
            }

            // ✅ Validate schema is loaded
            var schemaCache = _serviceProvider.GetRequiredService<TextToSqlAgent.Core.Interfaces.ISchemaCache>();
            var schema = await schemaCache.GetAsync(request.ConnectionId);
            if (schema == null)
            {
                _logger.LogWarning("[StreamingAgent] Schema not loaded for connection {ConnectionId}, user {UserId}",
                    request.ConnectionId, userId);

                await WriteSseEventAsync("error", new
                {
                    code = "SCHEMA_NOT_LOADED",
                    message = "Database schema not loaded. Please test connection first to load schema.",
                    action = "TEST_CONNECTION",
                    connectionId = request.ConnectionId,
                    suggestion = "Click 'Test Connection' button to load database schema",
                    correlationId
                }, ct);
                return;
            }

            // ✅ Create scoped service provider with overridden database configuration
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            // Get the database config and temporarily override it for this connection
            var dbConfig = scopedServices.GetRequiredService<TextToSqlAgent.Infrastructure.Configuration.DatabaseConfig>();
            var originalConnectionString = dbConfig.ConnectionString;

            // Build connection string from connection entity (decrypt it)
            var connectionString = _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
            dbConfig.ConnectionString = connectionString;

            try
            {
                // Create progress reporter that writes SSE events
                var progress = new Progress<AgentStageEvent>(async stageEvent =>
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        await WriteSseEventAsync("stage_update", stageEvent, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[StreamingAgent] Failed to write SSE event (client may have disconnected)");
                    }
                });

                // ✅ Create SQL token callback for streaming SQL generation
                Action<string> sqlTokenCallback = (token) =>
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        WriteSseEventAsync("sql_token", new { token }, ct).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[StreamingAgent] Failed to write SQL token (client may have disconnected)");
                    }
                };

                // Load conversation history if provided
                List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null;
                TextToSqlAgent.Infrastructure.Entities.Conversation? dbConversation = null;
                if (!string.IsNullOrEmpty(request.ConversationId))
                {
                    try
                    {
                        var messages = await _unitOfWork.Messages.GetByConversationIdAsync(request.ConversationId);
                        conversationHistory = messages?.OrderBy(m => m.CreatedAt).ToList();

                        // ✅ Phase 3: Load persisted conversation context from DB
                        dbConversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId);
                        if (dbConversation?.ContextJson != null)
                        {
                            try
                            {
                                var persistedContext = JsonSerializer.Deserialize<TextToSqlAgent.Core.Models.SerializableConversationContext>(
                                    dbConversation.ContextJson);
                                if (persistedContext != null)
                                {
                                    _logger.LogDebug(
                                        "[StreamingAgent] ✅ Loaded persisted context — Tables: [{Tables}], LastSQL present: {HasSql}",
                                        string.Join(", ", persistedContext.MentionedTables),
                                        !string.IsNullOrEmpty(persistedContext.LastSql));
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "[StreamingAgent] Failed to deserialize ContextJson, starting fresh");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[StreamingAgent] Failed to load conversation history");
                    }
                }

                // ✅ Phase 5A: Call PipelineOrchestrator directly (no more EnhancedAgentOrchestrator wrapper)
                var pipelineContext = new TextToSqlAgent.Application.Pipeline.PipelineContext
                {
                    UserQuestion = request.Question,
                    EnrichedQuestion = request.Question, // Will be overwritten by ValidationStage
                    ConversationId = request.ConversationId,
                    ConversationHistory = conversationHistory,
                    Progress = progress,
                    SqlTokenCallback = sqlTokenCallback
                };

                var response = await _pipelineOrchestrator.ExecuteAsync(pipelineContext, ct);

                // Save user message to database
                var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

                if (!string.IsNullOrEmpty(userId))
                {
                    var userMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                    {
                        ConversationId = conversationId,
                        Role = "user",
                        Content = request.Question,
                        Success = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _unitOfWork.Messages.AddAsync(userMessage);

                    // Estimate token usage
                    var inputTokens = Math.Max(1, request.Question.Length / 4);
                    var answerLength = (response.Answer?.Length ?? 0) + (response.SqlGenerated?.Length ?? 0);
                    var outputTokens = Math.Max(1, answerLength / 4);
                    var totalTokens = inputTokens + outputTokens;
                    var model = "gpt-4o";

                    // Approximate pricing per 1K tokens
                    var costPer1KTokens = 0.03m;
                    var cost = (totalTokens / 1000m) * costPer1KTokens;

                    // Save assistant message to database
                    var assistantMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                    {
                        ConversationId = conversationId,
                        Role = "assistant",
                        Content = response.Answer ?? "",
                        SqlQuery = response.SqlGenerated,
                        Results = response.QueryResult?.Rows != null ?
                            JsonSerializer.Serialize(response.QueryResult.Rows) : null,
                        RowCount = response.QueryResult?.RowCount,
                        ErrorMessage = response.ErrorMessage,
                        QueryExplanation = response.QueryExplanation,
                        ProcessingSteps = response.ProcessingSteps?.Count > 0 ?
                            JsonSerializer.Serialize(response.ProcessingSteps) : null,
                        SuggestedQueries = response.SuggestedQueries?.Count > 0 ?
                            JsonSerializer.Serialize(response.SuggestedQueries) : null,
                        CorrectionHistory = response.CorrectionHistory?.Count > 0 ?
                            JsonSerializer.Serialize(response.CorrectionHistory) : null,
                        WasCorrected = response.WasCorrected,
                        CorrectionAttempts = response.CorrectionAttempts,
                        Success = response.Success,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        TotalTokens = totalTokens,
                        Model = model,
                        Cost = cost,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _unitOfWork.Messages.AddAsync(assistantMessage);

                    // ✅ Phase 3: Persist conversation context (structured memory) back to DB
                    try
                    {
                        if (dbConversation == null)
                        {
                            dbConversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
                        }

                        if (dbConversation != null)
                        {
                            // Build structured memory snapshot from this interaction
                            var existingContext = new TextToSqlAgent.Core.Models.SerializableConversationContext();
                            if (!string.IsNullOrEmpty(dbConversation.ContextJson))
                            {
                                try
                                {
                                    existingContext = JsonSerializer.Deserialize<TextToSqlAgent.Core.Models.SerializableConversationContext>(
                                        dbConversation.ContextJson) ?? existingContext;
                                }
                                catch { /* start fresh if corrupted */ }
                            }

                            // Enrich context with this turn's data
                            if (!string.IsNullOrEmpty(response.SqlGenerated))
                            {
                                existingContext.UpdateLastSql(response.SqlGenerated,
                                    response.Answer?.Length > 200 ? response.Answer[..200] : response.Answer);
                            }

                            // Extract tables from SQL and add to mentioned tables
                            if (!string.IsNullOrEmpty(response.SqlGenerated))
                            {
                                var (tables, _, _, _) = TextToSqlAgent.Core.Helpers.SqlContextExtractor.ExtractFullContext(response.SqlGenerated);
                                foreach (var table in tables)
                                {
                                    existingContext.AddMentionedTable(table);
                                }
                            }

                            dbConversation.ContextJson = JsonSerializer.Serialize(existingContext);
                            dbConversation.LastActiveAt = DateTime.UtcNow;
                            dbConversation.UpdatedAt = DateTime.UtcNow;
                            await _unitOfWork.Conversations.UpdateAsync(dbConversation);

                            _logger.LogDebug(
                                "[StreamingAgent] ✅ Persisted context — Tables: [{Tables}]",
                                string.Join(", ", existingContext.MentionedTables));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[StreamingAgent] Failed to persist conversation context (non-critical)");
                    }

                    await _unitOfWork.SaveChangesAsync();

                    // Update user quota
                    try
                    {
                        await _tokenQuotaService.ConsumeTokenAsync(userId, inputTokens, outputTokens, model);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[StreamingAgent] Failed to update token quota for user {UserId}", userId);
                    }
                }

                // Emit the final result
                await WriteSseEventAsync("result", new
                {
                    success = response.Success,
                    answer = response.Answer,
                    sql = response.SqlGenerated,
                    data = response.QueryResult?.Rows,
                    suggestedQueries = response.SuggestedQueries,
                    processingSteps = response.ProcessingSteps,
                    metadata = response.Metadata,
                    errorMessage = response.ErrorMessage,
                    conversationId, // Important to pass back newly generated ID
                    correlationId
                }, ct);
            }
            finally
            {
                // ✅ Restore original connection string
                dbConfig.ConnectionString = originalConnectionString;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[StreamingAgent] Client disconnected for correlationId={CorrelationId}", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StreamingAgent] Error processing stream for correlationId={CorrelationId}", correlationId);

            try
            {
                await WriteSseEventAsync("error", new
                {
                    code = "PROCESSING_ERROR",
                    message = ex.Message,
                    correlationId
                }, ct);
            }
            catch
            {
                // Client already disconnected
            }
        }
    }

    /// <summary>
    /// Writes a Server-Sent Event to the response stream.
    /// Format: "event: {eventType}\ndata: {json}\n\n"
    /// </summary>
    private async Task WriteSseEventAsync(string eventType, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var sseMessage = $"event: {eventType}\ndata: {json}\n\n";
        await Response.WriteAsync(sseMessage, ct);
        await Response.Body.FlushAsync(ct);
    }
}

/// <summary>
/// Request model for the streaming agent endpoint.
/// </summary>
public class StreamQueryRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string? ConnectionId { get; set; }
}
