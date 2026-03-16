using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using TextToSqlAgent.Infrastructure.Observability;
using TextToSqlAgent.API.Repositories;

namespace TextToSqlAgent.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Add authorization to all endpoints
public class ObservabilityController : ControllerBase
{
    private readonly MetricsCollector _metricsCollector;
    private readonly HealthCheckService _healthCheckService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ObservabilityController> _logger;

    public ObservabilityController(
        MetricsCollector metricsCollector,
        HealthCheckService healthCheckService,
        IUnitOfWork unitOfWork,
        ILogger<ObservabilityController> logger)
    {
        _metricsCollector = metricsCollector;
        _healthCheckService = healthCheckService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Get system health status
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var health = await _healthCheckService.CheckHealthAsync();

            var statusCode = health.OverallStatus switch
            {
                HealthStatus.Healthy => 200,
                HealthStatus.Degraded => 200,
                HealthStatus.Unhealthy => 503,
                _ => 500
            };

            return StatusCode(statusCode, new
            {
                status = health.OverallStatus.ToString(),
                timestamp = health.Timestamp,
                checks = health.Checks.Select(c => new
                {
                    component = c.Component,
                    status = c.Status.ToString(),
                    message = c.Message,
                    responseTimeMs = c.ResponseTimeMs
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(500, new { status = "Error", message = ex.Message });
        }
    }

    /// <summary>
    /// Get performance metrics
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        try
        {
            var summary = _metricsCollector.GetSummary();

            return Ok(new
            {
                generatedAt = summary.GeneratedAt,
                metrics = summary.Metrics.Select(m => new
                {
                    name = m.Name,
                    count = m.Count,
                    avgDuration = m.AvgDuration,
                    minDuration = m.MinDuration,
                    maxDuration = m.MaxDuration,
                    p50Duration = m.P50Duration,
                    p95Duration = m.P95Duration,
                    p99Duration = m.P99Duration,
                    lastUpdated = m.LastUpdated
                }),
                errors = summary.Errors.Select(e => new
                {
                    operation = e.Operation,
                    errorType = e.ErrorType,
                    count = e.Count,
                    lastOccurrence = e.LastOccurrence
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get metrics summary as formatted text
    /// </summary>
    [HttpGet("metrics/summary")]
    [Produces("text/plain")]
    public IActionResult GetMetricsSummary()
    {
        try
        {
            var summary = _metricsCollector.GetSummary();
            return Content(summary.ToFormattedString(), "text/plain");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics summary");
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// Get specific metric
    /// </summary>
    [HttpGet("metrics/{operation}")]
    public IActionResult GetMetric(string operation)
    {
        try
        {
            var metric = _metricsCollector.GetMetric(operation);

            if (metric == null)
            {
                return NotFound(new { error = $"Metric '{operation}' not found" });
            }

            return Ok(new
            {
                name = metric.Name,
                count = metric.Count,
                avgDuration = metric.AvgDuration,
                minDuration = metric.MinDuration,
                maxDuration = metric.MaxDuration,
                p50Duration = metric.P50Duration,
                p95Duration = metric.P95Duration,
                p99Duration = metric.P99Duration,
                lastUpdated = metric.LastUpdated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metric for {Operation}", operation);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    [HttpPost("metrics/reset")]
    public IActionResult ResetMetrics()
    {
        try
        {
            _metricsCollector.Reset();
            _logger.LogInformation("Metrics reset by API request");
            return Ok(new { message = "Metrics reset successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset metrics");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Quick health check (just status)
    /// </summary>
    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        var isHealthy = await _healthCheckService.IsHealthyAsync();
        return isHealthy ? Ok("pong") : StatusCode(503, "unhealthy");
    }

    /// <summary>
    /// Get usage history for the current user
    /// </summary>
    [HttpGet("usage-history")]
    public async Task<IActionResult> GetUsageHistory(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Set default date range if not provided
            var end = endDate ?? DateTime.UtcNow;
            var start = startDate ?? end.AddDays(-30);

            // Get messages with token usage for this user
            var messages = await _unitOfWork.Messages.GetByUserIdAsync(userId, 1, limit);

            var usageHistory = messages
                .Where(m => m.CreatedAt >= start && m.CreatedAt <= end)
                .Where(m => m.TotalTokens.HasValue && m.TotalTokens > 0)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    date = m.CreatedAt.Date,
                    tokens = m.TotalTokens ?? 0,
                    inputTokens = m.InputTokens ?? 0,
                    outputTokens = m.OutputTokens ?? 0,
                    cost = m.Cost ?? 0,
                    model = m.Model ?? "unknown",
                    conversationId = m.ConversationId
                })
                .GroupBy(x => x.date)
                .Select(g => new
                {
                    date = g.Key,
                    totalTokens = g.Sum(x => x.tokens),
                    totalCost = g.Sum(x => x.cost),
                    messageCount = g.Count(),
                    models = g.Select(x => x.model).Distinct().ToArray()
                })
                .OrderBy(x => x.date)
                .ToList();

            var totalTokens = usageHistory.Sum(x => x.totalTokens);
            var totalCost = usageHistory.Sum(x => x.totalCost);

            return Ok(new
            {
                totalTokens,
                totalCost,
                period = new { startDate = start, endDate = end },
                usage = usageHistory
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get usage history for user {UserId}",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get usage breakdown by conversation
    /// </summary>
    [HttpGet("usage-by-conversation")]
    public async Task<IActionResult> GetUsageByConversation(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Set default date range if not provided
            var end = endDate ?? DateTime.UtcNow;
            var start = startDate ?? end.AddDays(-30);

            // Get conversations for this user
            var conversations = await _unitOfWork.Conversations.GetByUserIdAsync(userId);

            var usageByConversation = new List<object>();

            foreach (var conversation in conversations)
            {
                var messages = await _unitOfWork.Messages.GetByConversationIdAsync(conversation.Id);
                var relevantMessages = messages
                    .Where(m => m.CreatedAt >= start && m.CreatedAt <= end)
                    .Where(m => m.TotalTokens.HasValue && m.TotalTokens > 0);

                if (relevantMessages.Any())
                {
                    usageByConversation.Add(new
                    {
                        conversationId = conversation.Id,
                        title = conversation.Title,
                        totalTokens = relevantMessages.Sum(m => m.TotalTokens ?? 0),
                        totalCost = relevantMessages.Sum(m => m.Cost ?? 0),
                        messageCount = relevantMessages.Count(),
                        lastActivity = relevantMessages.Max(m => m.CreatedAt),
                        createdAt = conversation.CreatedAt
                    });
                }
            }

            return Ok(new
            {
                totalConversations = usageByConversation.Count,
                conversations = usageByConversation.OrderByDescending(c => ((dynamic)c).lastActivity)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get usage by conversation for user {UserId}",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get usage breakdown by AI model
    /// </summary>
    [HttpGet("usage-by-model")]
    public async Task<IActionResult> GetUsageByModel(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Set default date range if not provided
            var end = endDate ?? DateTime.UtcNow;
            var start = startDate ?? end.AddDays(-30);

            // Get messages with token usage for this user
            var messages = await _unitOfWork.Messages.GetByUserIdAsync(userId, 1, 1000);

            var usageByModel = messages
                .Where(m => m.CreatedAt >= start && m.CreatedAt <= end)
                .Where(m => m.TotalTokens.HasValue && m.TotalTokens > 0)
                .GroupBy(m => m.Model ?? "unknown")
                .Select(g => new
                {
                    model = g.Key,
                    totalTokens = g.Sum(m => m.TotalTokens ?? 0),
                    inputTokens = g.Sum(m => m.InputTokens ?? 0),
                    outputTokens = g.Sum(m => m.OutputTokens ?? 0),
                    totalCost = g.Sum(m => m.Cost ?? 0),
                    messageCount = g.Count(),
                    avgTokensPerMessage = g.Average(m => m.TotalTokens ?? 0),
                    lastUsed = g.Max(m => m.CreatedAt)
                })
                .OrderByDescending(x => x.totalTokens)
                .ToList();

            return Ok(new
            {
                totalModels = usageByModel.Count,
                models = usageByModel
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get usage by model for user {UserId}",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
