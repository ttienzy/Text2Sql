using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TextToSqlAgent.Infrastructure.Data;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Health check controller for monitoring API and dependencies
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        DatabaseInitializer databaseInitializer,
        ILogger<HealthController> logger)
    {
        _databaseInitializer = databaseInitializer;
        _logger = logger;
    }

    /// <summary>
    /// Basic health check endpoint
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Service = "TextToSqlAgent API"
        });
    }

    /// <summary>
    /// Detailed health check with dependency status
    /// </summary>
    [HttpGet("detailed")]
    public async Task<IActionResult> GetDetailed()
    {
        try
        {
            var databaseHealth = await _databaseInitializer.GetHealthStatusAsync();

            var response = new
            {
                Status = databaseHealth.IsHealthy ? "Healthy" : "Unhealthy",
                Timestamp = DateTime.UtcNow,
                Service = "TextToSqlAgent API",
                Dependencies = new
                {
                    Database = new
                    {
                        Status = databaseHealth.IsHealthy ? "Healthy" : "Unhealthy",
                        CheckedAt = databaseHealth.CheckedAt,
                        Issues = databaseHealth.Issues,
                        Statistics = databaseHealth.Statistics
                    }
                }
            };

            return databaseHealth.IsHealthy ? Ok(response) : StatusCode(503, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");

            return StatusCode(503, new
            {
                Status = "Unhealthy",
                Timestamp = DateTime.UtcNow,
                Service = "TextToSqlAgent API",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Performance metrics endpoint
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        try
        {
            var databaseHealth = await _databaseInitializer.GetHealthStatusAsync();

            return Ok(new
            {
                Timestamp = DateTime.UtcNow,
                Database = databaseHealth.Statistics,
                Performance = new
                {
                    UptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalSeconds,
                    WorkingSetMB = GC.GetTotalMemory(false) / 1024 / 1024
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}