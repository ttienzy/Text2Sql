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

    /// <summary>
    /// SMALL-3: Connection health indicator endpoint.
    /// Probes Redis, Qdrant, and the SQL database individually and returns per-service status.
    /// </summary>
    [HttpGet("connections")]
    public async Task<IActionResult> GetConnectionHealth()
    {
        var results = new Dictionary<string, object>();

        // 1. Redis
        try
        {
            var redis = HttpContext.RequestServices.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis != null)
            {
                var sw = Stopwatch.StartNew();
                var db = redis.GetDatabase();
                await db.PingAsync();
                sw.Stop();
                results["redis"] = new { Status = "Healthy", LatencyMs = sw.ElapsedMilliseconds };
            }
            else
            {
                results["redis"] = new { Status = "NotConfigured" };
            }
        }
        catch (Exception ex)
        {
            results["redis"] = new { Status = "Unhealthy", Error = ex.Message };
        }

        // 2. Qdrant
        try
        {
            var qdrantConfig = HttpContext.RequestServices.GetService<TextToSqlAgent.Infrastructure.Configuration.QdrantConfig>();
            if (qdrantConfig != null)
            {
                var sw = Stopwatch.StartNew();
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var resp = await http.GetAsync($"http://{qdrantConfig.Host}:6333/healthz");
                sw.Stop();
                results["qdrant"] = new { Status = resp.IsSuccessStatusCode ? "Healthy" : "Unhealthy", LatencyMs = sw.ElapsedMilliseconds };
            }
            else
            {
                results["qdrant"] = new { Status = "NotConfigured" };
            }
        }
        catch (Exception ex)
        {
            results["qdrant"] = new { Status = "Unhealthy", Error = ex.Message };
        }

        // 3. SQL Database
        try
        {
            var sw = Stopwatch.StartNew();
            var dbHealth = await _databaseInitializer.GetHealthStatusAsync();
            sw.Stop();
            results["database"] = new { Status = dbHealth.IsHealthy ? "Healthy" : "Unhealthy", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            results["database"] = new { Status = "Unhealthy", Error = ex.Message };
        }

        var allHealthy = results.Values.All(v =>
        {
            var status = v.GetType().GetProperty("Status")?.GetValue(v)?.ToString();
            return status == "Healthy";
        });

        return allHealthy ? Ok(results) : StatusCode(503, results);
    }
}