using System.Diagnostics;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service for processing natural language queries to SQL
/// </summary>
public class QueryProcessingService : IQueryProcessingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TextToSqlAgentOrchestrator _orchestrator;
    private readonly IAgent _reactAgent;
    private readonly ILogger<QueryProcessingService> _logger;

    public QueryProcessingService(
        IUnitOfWork unitOfWork,
        TextToSqlAgentOrchestrator orchestrator,
        IAgent reactAgent,
        ILogger<QueryProcessingService> logger)
    {
        _unitOfWork = unitOfWork;
        _orchestrator = orchestrator;
        _reactAgent = reactAgent;
        _logger = logger;
    }

    /// <summary>
    /// Process a natural language query and return SQL with results
    /// </summary>
    public async Task<QueryProcessingResult> ProcessQueryAsync(string question, string connectionId, string userId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(connectionId, userId);
            if (connection == null)
            {
                return new QueryProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Connection not found or access denied",
                    ProcessingTime = stopwatch.Elapsed
                };
            }

            // Use ReAct Agent for processing
            var request = new AgentRequest(question, connectionId);

            var result = await _reactAgent.RunAsync(request);

            stopwatch.Stop();

            return new QueryProcessingResult
            {
                Success = result.Success,
                SqlQuery = result.SqlGenerated,
                Results = result.QueryResult?.ToString(),
                RowCount = null, // Not available in AgentResult
                ErrorMessage = result.ErrorMessage,
                Explanation = result.Answer,
                InputTokens = null, // Not available in AgentResult
                OutputTokens = null, // Not available in AgentResult
                TotalTokens = result.TotalTokensUsed,
                Cost = null, // Not available in AgentResult
                Model = null, // Not available in AgentResult
                ProcessingTime = TimeSpan.FromMilliseconds(result.TotalLatencyMs)
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing query for user {UserId}", userId);

            return new QueryProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Process a query asynchronously (background job)
    /// </summary>
    public async Task<string> ProcessQueryAsyncJob(string question, string connectionId, string userId)
    {
        try
        {
            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(connectionId, userId);
            if (connection == null)
            {
                throw new UnauthorizedAccessException("Connection not found or access denied");
            }

            // Create job record
            var job = new AgentJob
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Question = question,
                ConnectionId = connectionId,
                Status = JobStatus.Queued,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.AgentJobs.AddAsync(job);
            await _unitOfWork.SaveChangesAsync();

            // TODO: Enqueue with Hangfire
            // var hangfireJobId = BackgroundJob.Enqueue(() => ProcessJobAsync(job.Id));
            // job.HangfireJobId = hangfireJobId;
            // await _unitOfWork.AgentJobs.UpdateAsync(job);
            // await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Created async job {JobId} for user {UserId}", job.Id, userId);
            return job.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating async job for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Get processing status for an async job
    /// </summary>
    public async Task<AgentJob?> GetJobStatusAsync(string jobId, string userId)
    {
        try
        {
            var job = await _unitOfWork.AgentJobs.GetByIdAsync(jobId);
            if (job == null || job.UserId != userId)
            {
                return null;
            }

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job status {JobId} for user {UserId}", jobId, userId);
            throw;
        }
    }

    /// <summary>
    /// Validate a SQL query without executing it
    /// </summary>
    public async Task<QueryValidationResult> ValidateQueryAsync(string sqlQuery, string connectionId, string userId)
    {
        try
        {
            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(connectionId, userId);
            if (connection == null)
            {
                return new QueryValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Connection not found or access denied"
                };
            }

            // TODO: Implement SQL validation logic
            // - Check syntax
            // - Check for dangerous operations (DROP, DELETE without WHERE, etc.)
            // - Check permissions

            return new QueryValidationResult
            {
                IsValid = true,
                IsSafe = true,
                Warnings = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating query for user {UserId}", userId);

            return new QueryValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Execute a pre-validated SQL query
    /// </summary>
    public async Task<QueryExecutionResult> ExecuteQueryAsync(string sqlQuery, string connectionId, string userId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(connectionId, userId);
            if (connection == null)
            {
                return new QueryExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Connection not found or access denied",
                    ExecutionTime = stopwatch.Elapsed
                };
            }

            // TODO: Execute query using database adapter
            // var adapter = _databaseAdapterFactory.CreateAdapter(connection.Provider);
            // var result = await adapter.ExecuteQueryAsync(connection.ConnectionString, sqlQuery);

            stopwatch.Stop();

            return new QueryExecutionResult
            {
                Success = true,
                Results = "[]", // Placeholder
                RowCount = 0,
                ExecutionTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error executing query for user {UserId}", userId);

            return new QueryExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Explain a SQL query in natural language
    /// </summary>
    public async Task<string> ExplainQueryAsync(string sqlQuery, string connectionId, string userId)
    {
        try
        {
            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(connectionId, userId);
            if (connection == null)
            {
                throw new UnauthorizedAccessException("Connection not found or access denied");
            }

            // TODO: Use LLM to explain the query
            // var explanation = await _llmClient.ExplainSqlAsync(sqlQuery);

            return "Query explanation will be implemented with LLM integration.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error explaining query for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Background job processing method
    /// </summary>
    public async Task ProcessJobAsync(string jobId)
    {
        try
        {
            var job = await _unitOfWork.AgentJobs.GetByIdAsync(jobId);
            if (job == null)
            {
                _logger.LogWarning("Job {JobId} not found", jobId);
                return;
            }

            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await _unitOfWork.AgentJobs.UpdateAsync(job);
            await _unitOfWork.SaveChangesAsync();

            var result = await ProcessQueryAsync(job.Question, job.ConnectionId, job.UserId);

            job.Status = result.Success ? JobStatus.Completed : JobStatus.Failed;
            job.SqlQuery = result.SqlQuery;
            job.Result = result.Results;
            job.RowCount = result.RowCount;
            job.ErrorMessage = result.ErrorMessage;
            job.Explanation = result.Explanation;
            job.InputTokens = result.InputTokens;
            job.OutputTokens = result.OutputTokens;
            job.TotalTokens = result.TotalTokens;
            job.Cost = result.Cost;
            job.Model = result.Model;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingTimeSeconds = (int)result.ProcessingTime.TotalSeconds;

            await _unitOfWork.AgentJobs.UpdateAsync(job);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Completed job {JobId} with status {Status}", jobId, job.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", jobId);

            // Update job status to failed
            var job = await _unitOfWork.AgentJobs.GetByIdAsync(jobId);
            if (job != null)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                await _unitOfWork.AgentJobs.UpdateAsync(job);
                await _unitOfWork.SaveChangesAsync();
            }
        }
    }
}