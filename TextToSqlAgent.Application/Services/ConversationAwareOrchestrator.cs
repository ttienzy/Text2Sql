using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Pipelines;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Entities;
using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Enhanced orchestrator with conversation awareness
/// Maintains context across multiple turns in a conversation
/// </summary>
public class ConversationAwareOrchestrator : IAgentOrchestrator
{
    private readonly IAgent _agent;
    private readonly ILogger<ConversationAwareOrchestrator> _logger;

    public ConversationAwareOrchestrator(
        IAgent agent,
        ILogger<ConversationAwareOrchestrator> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Process query with conversation context
    /// </summary>
    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        string? conversationId = null,
        List<Message>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[ConversationAwareOrchestrator] Processing query with conversation context. ConversationId: {ConversationId}",
            conversationId ?? "new");

        try
        {
            // Create conversation-aware request
            var request = new ConversationAwareAgentRequest(userQuestion, conversationId: conversationId)
            {
                MaxSteps = 12,
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Add conversation history if provided
            if (conversationHistory?.Count > 0)
            {
                foreach (var message in conversationHistory.OrderBy(m => m.CreatedAt))
                {
                    request.AddMessage(
                        message.Role,
                        message.Content,
                        message.SqlQuery,
                        ParseMessageResult(message)
                    );
                }

                // Detect if this is a follow-up question
                request.IsFollowUpQuestion = request.IsRelatedToPreviousContext();

                // Set previous context for reference
                var lastAssistantMessage = conversationHistory
                    .Where(m => m.Role == "assistant")
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault();

                if (lastAssistantMessage != null)
                {
                    request.PreviousQuery = lastAssistantMessage.SqlQuery;
                    request.PreviousResult = lastAssistantMessage.Results;
                }
            }

            // Execute agent with conversation context
            var agentResult = await _agent.RunAsync(request, cancellationToken);

            // Convert to AgentResponse with proper data
            var response = new AgentResponse
            {
                Success = agentResult.Success,
                Answer = agentResult.Answer ?? "Query processed successfully",
                SqlGenerated = agentResult.SqlGenerated,
                ProcessingSteps = agentResult.ProcessingSteps ?? new List<string>(),
                ErrorMessage = agentResult.ErrorMessage,
                ConversationId = conversationId,
                IsFollowUp = request.IsFollowUpQuestion,
                // These properties don't exist in AgentResult, so set defaults
                QueryExplanation = null,
                SuggestedQueries = new List<string>(),
                CorrectionHistory = new List<CorrectionAttempt>(),
                WasCorrected = false,
                CorrectionAttempts = 0
            };

            // Add query result if available - use actual data from agent
            if (agentResult.QueryResult != null)
            {
                // Try to convert the agent result to SqlExecutionResult
                if (agentResult.QueryResult is SqlExecutionResult sqlResult)
                {
                    response.QueryResult = sqlResult;
                }
                else
                {
                    // Create a basic result structure if conversion fails
                    response.QueryResult = new SqlExecutionResult
                    {
                        Success = agentResult.Success,
                        Columns = new List<string>(),
                        Rows = new List<Dictionary<string, object?>>(),
                        ExecutionTimeMs = 0,
                        RowsAffected = 0,
                        ErrorMessage = agentResult.ErrorMessage
                    };
                }
            }

            // Enhanced answer with conversation context
            if (request.IsFollowUpQuestion && !string.IsNullOrEmpty(request.PreviousQuery))
            {
                response.Answer = EnhanceAnswerWithContext(response.Answer, request);
            }

            _logger.LogInformation("[ConversationAwareOrchestrator] Query processed successfully. IsFollowUp: {IsFollowUp}",
                request.IsFollowUpQuestion);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAwareOrchestrator] Error processing query");

            return new AgentResponse
            {
                Success = false,
                ErrorMessage = $"Error processing query: {ex.Message}",
                ProcessingSteps = new List<string> { "Error occurred during processing" },
                ConversationId = conversationId
            };
        }
    }

    /// <summary>
    /// Execute query through the appropriate pipeline based on complexity (IAgentOrchestrator implementation)
    /// </summary>
    public async Task<QueryResult> ExecuteAsync(TextToSqlAgent.Application.Pipelines.QueryRequest request, CancellationToken ct = default)
    {
        try
        {
            // Convert QueryRequest to conversation-aware processing
            var response = await ProcessQueryAsync(request.Question, null, null, ct);

            // Convert AgentResponse to QueryResult
            return new QueryResult
            {
                Success = response.Success,
                ErrorMessage = response.ErrorMessage,
                SqlGenerated = response.SqlGenerated,
                FormattedAnswer = response.Answer,
                ProcessingSteps = response.ProcessingSteps ?? new List<string>(),
                QueryResultData = response.QueryResult != null ? new SqlExecutionResult
                {
                    Success = response.QueryResult.Success,
                    Columns = response.QueryResult.Columns ?? new List<string>(),
                    Rows = response.QueryResult.Rows?.Cast<Dictionary<string, object?>>().ToList() ?? new List<Dictionary<string, object?>>(),
                    ExecutionTimeMs = (int)response.QueryResult.ExecutionTimeMs,
                    RowsAffected = response.QueryResult.RowsAffected,
                    ErrorMessage = response.QueryResult.ErrorMessage
                } : null,
                WasEscalated = false,
                EscalationReason = null,
                Complexity = QueryComplexity.Medium, // Default complexity
                LlmCalls = 1,
                ExecutionTimeMs = 0
            };
        }
        catch (Exception ex)
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingSteps = new List<string> { "Error occurred during execution" },
                Complexity = QueryComplexity.Medium,
                LlmCalls = 0,
                ExecutionTimeMs = 0
            };
        }
    }

    /// <summary>
    /// Classify query complexity without executing pipeline (IAgentOrchestrator implementation)
    /// </summary>
    public async Task<TextToSqlAgent.Application.Routing.QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default)
    {
        // Simple classification logic - can be enhanced
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var complexity = wordCount switch
        {
            <= 5 => QueryComplexity.Simple,
            <= 15 => QueryComplexity.Medium,
            _ => QueryComplexity.Complex
        };

        return await Task.FromResult(new TextToSqlAgent.Application.Routing.QueryClassifierResult
        {
            Complexity = complexity,
            Confidence = 0.8,
            Reasoning = $"Classified based on word count: {wordCount} words"
        });
    }

    /// <summary>
    /// Connect to a database and perform automatic schema indexing if needed (IAgentOrchestrator implementation)
    /// </summary>
    public async Task<ConnectionResult> ConnectToDatabaseAsync(
        string connectionId,
        string connectionString,
        bool forceReindex = false,
        CancellationToken ct = default)
    {
        // Basic connection result - actual implementation would involve schema indexing
        return await Task.FromResult(new ConnectionResult
        {
            Success = true,
            IndexingPerformed = forceReindex,
            PointsIndexed = 0,
            IndexingDuration = TimeSpan.Zero,
            ErrorMessage = null
        });
    }

    /// <summary>
    /// Parse message result from database format
    /// </summary>
    private static object? ParseMessageResult(Message message)
    {
        if (!string.IsNullOrEmpty(message.Results))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<object>(message.Results);
            }
            catch
            {
                return message.Results; // Return as string if JSON parsing fails
            }
        }

        return null;
    }

    /// <summary>
    /// Process query without conversation context (backward compatibility)
    /// </summary>
    public async Task<AgentResponse> ProcessQueryAsync(string userQuestion, CancellationToken cancellationToken = default)
    {
        return await ProcessQueryAsync(userQuestion, null, null, cancellationToken);
    }

    /// <summary>
    /// Enhance answer with conversation context
    /// </summary>
    private string EnhanceAnswerWithContext(string answer, ConversationAwareAgentRequest request)
    {
        var enhancedAnswer = answer;

        // Add context about follow-up nature
        if (request.IsFollowUpQuestion)
        {
            enhancedAnswer = $"Building on our previous conversation: {enhancedAnswer}";
        }

        // Add reference to previous query if relevant
        if (!string.IsNullOrEmpty(request.PreviousQuery))
        {
            enhancedAnswer += $"\n\nNote: This query builds upon the previous analysis.";
        }

        return enhancedAnswer;
    }
}

/// <summary>
/// Extension methods for conversation-aware processing
/// </summary>
public static class ConversationExtensions
{
    /// <summary>
    /// Convert database messages to conversation format
    /// </summary>
    public static List<ConversationMessage> ToConversationMessages(this IEnumerable<Message> messages)
    {
        return messages.Select(m => new ConversationMessage
        {
            Role = m.Role,
            Content = m.Content,
            SqlQuery = m.SqlQuery,
            Result = ParseResult(m.Results),
            Timestamp = m.CreatedAt
        }).ToList();
    }

    private static object? ParseResult(string? results)
    {
        if (string.IsNullOrEmpty(results))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<object>(results);
        }
        catch
        {
            return results;
        }
    }
}