using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Builder for creating unified pipeline responses
/// Centralizes response creation logic for all pipelines
/// </summary>
public class PipelineResponseBuilder
{
    private readonly ILogger<PipelineResponseBuilder> _logger;
    private readonly bool _isDevelopment;

    public PipelineResponseBuilder(
        ILogger<PipelineResponseBuilder> logger,
        bool isDevelopment = false)
    {
        _logger = logger;
        _isDevelopment = isDevelopment;
    }

    /// <summary>
    /// Build response for QUERY pipeline
    /// </summary>
    public UnifiedPipelineResponse BuildQueryResponse(
        AgentResponse agentResponse,
        IntentClassificationResult? intentResult,
        Stopwatch? stopwatch = null,
        string? resultId = null,
        bool hasMore = false)
    {
        var duration = stopwatch?.Elapsed ?? TimeSpan.Zero;

        return new UnifiedPipelineResponse
        {
            Success = agentResponse.Success,
            Pipeline = PipelineType.Query,
            Intent = intentResult?.ToIntentSummary() ?? IntentClassificationExtensions.CreateDefaultQueryIntent(),
            Message = agentResponse.Answer,
            Data = new QueryPipelineData
            {
                Answer = agentResponse.Answer,
                QueryResult = agentResponse.QueryResult,
                QueryExplanation = agentResponse.QueryExplanation,
                SuggestedQueries = agentResponse.SuggestedQueries,
                ContextEntities = agentResponse.ContextEntities,
                PrimaryEntity = agentResponse.PrimaryEntity,
                PronounsResolved = agentResponse.PronounsResolved,
                ChartImageBase64 = agentResponse.ChartImageBase64,
                ChartType = agentResponse.ChartType,
                ResultId = resultId,  // ✅ For pagination
                HasMore = hasMore     // ✅ For lazy loading
            },
            SqlGenerated = agentResponse.SqlGenerated,
            RequiresConfirmation = false,
            Warnings = new List<string>(),
            Suggestions = agentResponse.SuggestedQueries,
            Error = !agentResponse.Success && !string.IsNullOrEmpty(agentResponse.ErrorMessage)
                ? new ErrorDetails
                {
                    Code = "QUERY_ERROR",
                    Message = agentResponse.ErrorMessage
                }
                : null,
            Execution = new ExecutionMetadata
            {
                Duration = duration,
                ProcessingSteps = agentResponse.ProcessingSteps,
                CorrectionAttempts = agentResponse.CorrectionAttempts,
                WasCorrected = agentResponse.WasCorrected
            }
        };
    }

    /// <summary>
    /// Build response for WRITE pipeline preview
    /// </summary>
    public UnifiedPipelineResponse BuildWritePreviewResponse(
        WriteOperationPreview preview,
        IntentClassificationResult intentResult,
        Stopwatch? stopwatch = null)
    {
        var duration = stopwatch?.Elapsed ?? TimeSpan.Zero;
        var hasError = !string.IsNullOrEmpty(preview.ValidationError);

        return new UnifiedPipelineResponse
        {
            Success = !hasError,
            Pipeline = PipelineType.Write,
            Intent = intentResult.ToIntentSummary(),
            Message = hasError
                ? preview.ValidationError!
                : "Please review and confirm this write operation",
            Data = new WritePipelineData
            {
                Preview = preview
            },
            SqlGenerated = preview.SqlStatement,
            RequiresConfirmation = preview.RequiresConfirmation,
            Warnings = preview.Warnings,
            Suggestions = new List<string>(),
            Error = hasError
                ? new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = preview.ValidationError!
                }
                : null,
            Execution = new ExecutionMetadata
            {
                Duration = duration,
                ProcessingSteps = new List<string>
                {
                    "Identify target table",
                    "Generate SQL",
                    "Validate statement",
                    "Estimate impact"
                }
            }
        };
    }

    /// <summary>
    /// Build response for WRITE pipeline execution result
    /// </summary>
    public UnifiedPipelineResponse BuildWriteResultResponse(
        WriteOperationResult result,
        IntentClassificationResult intentResult)
    {
        return new UnifiedPipelineResponse
        {
            Success = result.Success,
            Pipeline = PipelineType.Write,
            Intent = intentResult.ToIntentSummary(),
            Message = result.Success
                ? $"Successfully {result.OperationType.ToString().ToLower()}ed {result.ActualAffectedRows} row(s) in {result.TargetTable}"
                : result.ErrorMessage ?? "Write operation failed",
            Data = new WritePipelineData
            {
                Result = result
            },
            SqlGenerated = result.SqlExecuted,
            RequiresConfirmation = false,
            Warnings = new List<string>(),
            Suggestions = result.Suggestions,
            Error = !result.Success && !string.IsNullOrEmpty(result.ErrorMessage)
                ? new ErrorDetails
                {
                    Code = "EXECUTION_ERROR",
                    Message = result.ErrorMessage
                }
                : null,
            Execution = new ExecutionMetadata
            {
                Duration = result.ExecutionTime,
                ProcessingSteps = result.ProcessingSteps
            }
        };
    }

    /// <summary>
    /// Build response for DDL pipeline preview
    /// </summary>
    public UnifiedPipelineResponse BuildDdlPreviewResponse(
        DDLOperationPreview preview,
        IntentClassificationResult intentResult,
        Stopwatch? stopwatch = null)
    {
        var duration = stopwatch?.Elapsed ?? TimeSpan.Zero;
        var hasError = !string.IsNullOrEmpty(preview.ValidationError);

        return new UnifiedPipelineResponse
        {
            Success = !hasError,
            Pipeline = PipelineType.Ddl,
            Intent = intentResult.ToIntentSummary(),
            Message = hasError
                ? preview.ValidationError!
                : "Please review the impact analysis and confirm this DDL operation",
            Data = new DdlPipelineData
            {
                Preview = preview
            },
            SqlGenerated = preview.DDLScript,
            RequiresConfirmation = preview.RequiresConfirmation,
            Warnings = preview.Impact?.Warnings ?? new List<string>(),
            Suggestions = new List<string>(),
            Error = hasError
                ? new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = preview.ValidationError!
                }
                : null,
            Execution = new ExecutionMetadata
            {
                Duration = duration,
                ProcessingSteps = new List<string>
                {
                    "Classify DDL type",
                    "Load schema context",
                    "Generate DDL script",
                    "Analyze impact"
                }
            }
        };
    }

    /// <summary>
    /// Build response for DDL pipeline execution result
    /// </summary>
    public UnifiedPipelineResponse BuildDdlResultResponse(
        DDLOperationResult result,
        IntentClassificationResult intentResult)
    {
        return new UnifiedPipelineResponse
        {
            Success = result.Success,
            Pipeline = PipelineType.Ddl,
            Intent = intentResult.ToIntentSummary(),
            Message = result.Success
                ? $"Successfully executed {result.OperationType} on {result.TargetObject}"
                : result.ErrorMessage ?? "DDL operation failed",
            Data = new DdlPipelineData
            {
                Result = result
            },
            SqlGenerated = result.DDLExecuted,
            RequiresConfirmation = false,
            Warnings = new List<string>(),
            Suggestions = new List<string>(),
            Error = !result.Success && !string.IsNullOrEmpty(result.ErrorMessage)
                ? new ErrorDetails
                {
                    Code = "EXECUTION_ERROR",
                    Message = result.ErrorMessage
                }
                : null,
            Execution = new ExecutionMetadata
            {
                Duration = result.ExecutionTime,
                ProcessingSteps = result.ProcessingSteps
            }
        };
    }

    /// <summary>
    /// Build response for FORBIDDEN pipeline
    /// </summary>
    public UnifiedPipelineResponse BuildForbiddenResponse(
        ForbiddenOperationResult forbiddenResult,
        IntentClassificationResult intentResult,
        Stopwatch? stopwatch = null)
    {
        var duration = stopwatch?.Elapsed ?? TimeSpan.Zero;

        return new UnifiedPipelineResponse
        {
            Success = false,
            Pipeline = PipelineType.Forbidden,
            Intent = intentResult.ToIntentSummary(),
            Message = forbiddenResult.UserFacingMessage,
            Data = new ForbiddenPipelineData
            {
                Result = forbiddenResult
            },
            SqlGenerated = null, // No SQL for forbidden operations
            RequiresConfirmation = false,
            Warnings = new List<string> { "Destructive operation detected and blocked" },
            Suggestions = forbiddenResult.SafeAlternatives
                .Select(alt => alt.Title)
                .ToList(),
            Error = new ErrorDetails
            {
                Code = "FORBIDDEN_OPERATION",
                Message = forbiddenResult.RejectionReason,
                AdditionalInfo = new Dictionary<string, object>
                {
                    ["detectedPatterns"] = forbiddenResult.DetectedPatterns,
                    ["safeAlternatives"] = forbiddenResult.SafeAlternatives
                }
            },
            Execution = new ExecutionMetadata
            {
                Duration = duration,
                ProcessingSteps = new List<string>
                {
                    "Detect forbidden pattern",
                    "Generate safe alternatives",
                    "Build rejection message"
                }
            }
        };
    }

    /// <summary>
    /// Build response for REJECT pipeline (off-topic or unknown)
    /// </summary>
    public UnifiedPipelineResponse BuildRejectionResponse(
        IntentClassificationResult intentResult,
        string message,
        Stopwatch? stopwatch = null)
    {
        var duration = stopwatch?.Elapsed ?? TimeSpan.Zero;

        // Detect language
        var isVietnamese = !string.IsNullOrEmpty(intentResult.NormalizedQuery) &&
            intentResult.NormalizedQuery.Any(c =>
                "àáảãạăắằẳẵặâấầẩẫậèéẻẽẹêếềểễệìíỉĩịòóỏõọôốồổỗộơớờởỡợùúủũụưứừửữựỳýỷỹỵđ".Contains(c));

        return new UnifiedPipelineResponse
        {
            Success = false,
            Pipeline = PipelineType.Reject,
            Intent = intentResult.ToIntentSummary(),
            Message = message,
            Data = new RejectionPipelineData
            {
                Reason = intentResult.Reasoning,
                Language = isVietnamese ? "vi" : "en",
                RejectedIntent = intentResult.Intent
            },
            SqlGenerated = null,
            RequiresConfirmation = false,
            Warnings = new List<string>(),
            Suggestions = new List<string>(),
            Error = new ErrorDetails
            {
                Code = intentResult.Intent == IntentCategory.OffTopic ? "OFF_TOPIC" : "UNKNOWN_INTENT",
                Message = message
            },
            Execution = new ExecutionMetadata
            {
                Duration = duration,
                ProcessingSteps = new List<string>
                {
                    "Classify intent",
                    "Detect rejection reason"
                }
            }
        };
    }

    /// <summary>
    /// Build error response for unexpected exceptions
    /// </summary>
    public UnifiedPipelineResponse BuildErrorResponse(
        Exception exception,
        PipelineType pipeline,
        IntentClassificationResult? intentResult = null)
    {
        _logger.LogError(exception, "[ResponseBuilder] Building error response for {Pipeline}", pipeline);

        return new UnifiedPipelineResponse
        {
            Success = false,
            Pipeline = pipeline,
            Intent = intentResult?.ToIntentSummary() ?? IntentClassificationExtensions.CreateDefaultQueryIntent(),
            Message = "An error occurred while processing your request",
            Data = new RejectionPipelineData
            {
                Reason = exception.Message,
                Language = "en",
                RejectedIntent = IntentCategory.Unknown
            },
            SqlGenerated = null,
            RequiresConfirmation = false,
            Warnings = new List<string>(),
            Suggestions = new List<string>(),
            Error = new ErrorDetails
            {
                Code = "INTERNAL_ERROR",
                Message = exception.Message,
                // ✅ SERIOUS-8 FIX: Only expose StackTrace in Development environment (security)
                StackTrace = _isDevelopment ? exception.StackTrace : null,
                AdditionalInfo = new Dictionary<string, object>
                {
                    ["exceptionType"] = exception.GetType().Name
                }
            },
            Execution = new ExecutionMetadata
            {
                Duration = TimeSpan.Zero,
                ProcessingSteps = new List<string> { "Error occurred" }
            }
        };
    }
}
