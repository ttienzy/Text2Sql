using Microsoft.AspNetCore.Mvc;
using TextToSqlAgent.Infrastructure.Observability;

namespace TextToSqlAgent.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ObservabilityController : ControllerBase
{
    private readonly MetricsCollector _metricsCollector;
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<ObservabilityController> _logger;

    public ObservabilityController(
        MetricsCollector metricsCollector,
        HealthCheckService healthCheckService,
        ILogger<ObservabilityController> logger)
    {
        _metricsCollector = metricsCollector;
        _healthCheckService = healthCheckService;
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
}
