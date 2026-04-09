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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StreamingAgentController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TextToSqlAgent.Infrastructure.Services.ITokenQuotaService _tokenQuotaService;
    private readonly TextToSqlAgent.API.Services.IConnectionEncryptionService _encryptionService;

    // ✅ CRIT-3 FIX: SemaphoreSlim to serialize SSE writes (prevent race conditions)
    private readonly SemaphoreSlim _sseWriteLock = new(1, 1);

    public StreamingAgentController(
        IUnitOfWork unitOfWork,
        ILogger<StreamingAgentController> logger,
        IServiceProvider serviceProvider,
        TextToSqlAgent.Infrastructure.Services.ITokenQuotaService tokenQuotaService,
        TextToSqlAgent.API.Services.IConnectionEncryptionService encryptionService)
    {
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
            // ✅ DEBUG LOG: Entry point
            _logger.LogInformation(
                "[StreamingAgent] REQUEST RECEIVED - Question: '{Question}', ConnectionId: {ConnectionId}, ConversationId: {ConversationId}, CorrelationId: {CorrelationId}",
                request.Question,
                request.ConnectionId,
                request.ConversationId ?? "null",
                correlationId);

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

            // ✅ Validate question doesn't contain image input (not supported by current model)
            if (!string.IsNullOrEmpty(request.Question))
            {
                var imageValidation = ValidateQuestionInput(request.Question);
                if (!imageValidation.IsValid)
                {
                    await WriteSseEventAsync("error", new
                    {
                        code = "INPUT_NOT_SUPPORTED",
                        message = imageValidation.ErrorMessage,
                        correlationId
                    }, ct);
                    return;
                }
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

            // ✅ CRIT-2 FIX: Use DatabaseConfigContext (AsyncLocal) to safely override connection per-request
            // This prevents race conditions when multiple users query different databases simultaneously
            var connectionString = _encryptionService.GetConnectionString(connection);

            using (TextToSqlAgent.Infrastructure.Configuration.DatabaseConfigContext.SetConnectionString(connectionString))
            {
                // All database operations in this async context will use the override connection string
                // No need for scoped service provider - DatabaseConfig.ConnectionString will automatically
                // return the async-local override for this request only

                // ✅ CRIT-4 FIX: Use Channel for SQL token streaming to prevent race conditions
                // Channel ensures tokens are written in order and thread-safe
                var sqlTokenChannel = System.Threading.Channels.Channel.CreateUnbounded<string>(
                    new System.Threading.Channels.UnboundedChannelOptions
                    {
                        SingleReader = true, // Only one consumer task
                        SingleWriter = false // Multiple LLM callbacks can write
                    });

                // Background task to consume tokens from channel and write to SSE
                var tokenWriterTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var token in sqlTokenChannel.Reader.ReadAllAsync(ct))
                        {
                            try
                            {
                                await WriteSseEventAsync("sql_token", new { token }, ct);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "[StreamingAgent] Failed to write SQL token (client disconnected)");
                                break; // Stop consuming if client disconnected
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("[StreamingAgent] Token writer cancelled");
                    }
                }, ct);

                // SQL token callback - write to channel (non-blocking, thread-safe)
                Action<string> sqlTokenCallback = (token) =>
                {
                    if (ct.IsCancellationRequested) return;

                    // TryWrite is non-blocking and thread-safe
                    if (!sqlTokenChannel.Writer.TryWrite(token))
                    {
                        _logger.LogWarning("[StreamingAgent] Failed to write token to channel (channel full or closed)");
                    }
                };

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
                        _logger.LogDebug(ex, "[StreamingAgent] Failed to write stage update (client may have disconnected)");
                    }
                });

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

                // ✅ PHASE-1 TASK 1.1: Use EnhancedAgentOrchestrator with Intent Routing
                // Agent will automatically use the async-local connection string override
                var agent = _serviceProvider.GetRequiredService<TextToSqlAgent.Application.Services.EnhancedAgentOrchestrator>();

                // Call ProcessMessageWithIntentRoutingAsync for unified pipeline routing
                // Pass progress and sqlTokenCallback for real-time SSE stage updates
                var unifiedResponse = await agent.ProcessMessageWithIntentRoutingAsync(
                    request.Question,
                    request.ConnectionId,
                    request.ConversationId,
                    conversationHistory,
                    progress,
                    sqlTokenCallback,
                    ct);

                // ✅ PHASE-1 TASK 1.1: Save messages with UnifiedPipelineResponse data
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

                    // Extract data from UnifiedPipelineResponse
                    string answer = unifiedResponse.Message;
                    string? sqlGenerated = unifiedResponse.SqlGenerated;
                    string? queryExplanation = null;
                    List<string>? processingSteps = unifiedResponse.Execution?.ProcessingSteps?.ToList();
                    List<string>? suggestedQueries = null;
                    TextToSqlAgent.Core.Models.SqlExecutionResult? queryResult = null;
                    bool success = unifiedResponse.Success;

                    // Extract pipeline-specific data
                    if (unifiedResponse.Data is TextToSqlAgent.Core.Models.QueryPipelineData queryData)
                    {
                        answer = queryData.Answer;
                        queryExplanation = queryData.QueryExplanation;
                        suggestedQueries = queryData.SuggestedQueries?.ToList();
                        queryResult = queryData.QueryResult;
                    }

                    // Estimate token usage
                    var inputTokens = Math.Max(1, request.Question.Length / 4);
                    var outputTokens = Math.Max(1, (answer?.Length ?? 0) / 4 + (sqlGenerated?.Length ?? 0) / 4);
                    var totalTokens = inputTokens + outputTokens;
                    var model = "gpt-4o";

                    // Calculate cost
                    var costPer1KTokens = 0.03m;
                    var cost = (totalTokens / 1000m) * costPer1KTokens;

                    // Save assistant message to database
                    var assistantMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                    {
                        ConversationId = conversationId,
                        Role = "assistant",
                        Content = answer ?? "",
                        SqlQuery = sqlGenerated,
                        Results = queryResult?.Rows != null ?
                            JsonSerializer.Serialize(queryResult.Rows) : null,
                        RowCount = queryResult?.RowCount,
                        ErrorMessage = unifiedResponse.Error?.Message,
                        QueryExplanation = queryExplanation,
                        ProcessingSteps = processingSteps?.Count > 0 ?
                            JsonSerializer.Serialize(processingSteps) : null,
                        SuggestedQueries = suggestedQueries?.Count > 0 ?
                            JsonSerializer.Serialize(suggestedQueries) : null,
                        CorrectionHistory = null, // Not available in UnifiedPipelineResponse
                        WasCorrected = unifiedResponse.Execution?.WasCorrected ?? false,
                        CorrectionAttempts = unifiedResponse.Execution?.CorrectionAttempts ?? 0,
                        Success = success,
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
                            if (!string.IsNullOrEmpty(unifiedResponse.SqlGenerated))
                            {
                                existingContext.UpdateLastSql(unifiedResponse.SqlGenerated,
                                    unifiedResponse.Message?.Length > 200 ? unifiedResponse.Message[..200] : unifiedResponse.Message);
                            }

                            // Extract tables from SQL and add to mentioned tables
                            if (!string.IsNullOrEmpty(unifiedResponse.SqlGenerated))
                            {
                                var (tables, _, _, _) = TextToSqlAgent.Core.Helpers.SqlContextExtractor.ExtractFullContext(unifiedResponse.SqlGenerated);
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

                // ✅ PHASE-1 TASK 1.3: Emit UnifiedPipelineResponse directly (correct format for frontend)
                await WriteSseEventAsync("result", unifiedResponse, ct);

                // ✅ CRIT-4 FIX: Close channel and wait for token writer to finish
                sqlTokenChannel.Writer.Complete();
                await tokenWriterTask; // Wait for all tokens to be written
            } // End of using DatabaseConfigContext
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
                // Ignore secondary errors during error handling
            }
        }
    }

    /// <summary>
    /// Validates question input for unsupported formats (images, files)
    /// Current model (gpt-4o) does not support vision/multimodal input
    /// </summary>
    private static InputValidationResult ValidateQuestionInput(string question)
    {
        if (string.IsNullOrEmpty(question))
        {
            return new InputValidationResult { IsValid = true };
        }

        var lowerQuestion = question.ToLowerInvariant();

        // Check for image URLs (http/https with common image extensions)
        if (question.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg" };

            foreach (var ext in imageExtensions)
            {
                if (lowerQuestion.Contains(ext))
                {
                    return new InputValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Image input is not supported. The current AI model does not support image analysis. Please describe your question in text format."
                    };
                }
            }
        }

        // Check for base64 image data
        if (question.Contains("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return new InputValidationResult
            {
                IsValid = false,
                ErrorMessage = "Image input is not supported. The current AI model does not support image analysis. Please describe your question in text format."
            };
        }

        // Check for common image-related keywords in user request
        var imageKeywords = new[] { "analyze this image", "look at this", "what's in this picture", 
            "describe the image", "what does this show", "read this image", "extract text from image",
            "ocr", "convert image to text" };
        
        foreach (var keyword in imageKeywords)
        {
            if (lowerQuestion.Contains(keyword))
            {
                return new InputValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Image analysis is not supported. The current AI model does not support vision/multimodal input. Please ask a text-based database question."
                };
            }
        }

        return new InputValidationResult { IsValid = true };
    }

    /// <summary>
    /// Input validation result
    /// </summary>
    private class InputValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// Writes a Server-Sent Event to the response stream.
    /// Format: "event: {eventType}\ndata: {json}\n\n"
    /// 
    /// ✅ CRIT-3 FIX: Thread-safe with SemaphoreSlim to prevent concurrent writes
    /// from Progress<T> callbacks and Channel consumer running on different threads
    /// </summary>
    private async Task WriteSseEventAsync(string eventType, object data, CancellationToken ct)
    {
        await _sseWriteLock.WaitAsync(ct);
        try
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
        finally
        {
            _sseWriteLock.Release();
        }
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
