using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Infrastructure.Observability;

/// <summary>
/// Health check service for monitoring system components
/// </summary>
public class HealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILLMClient? _llmClient;
    private readonly QdrantService? _qdrantService;
    private readonly string? _connectionString;

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IServiceProvider serviceProvider,
        ILLMClient? llmClient = null,
        QdrantService? qdrantService = null,
        string? connectionString = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _llmClient = llmClient;
        _qdrantService = qdrantService;
        _connectionString = connectionString;
    }

    /// <summary>
    /// Perform comprehensive health check
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting health check...");

        var result = new HealthCheckResult
        {
            Timestamp = DateTime.UtcNow,
            Checks = new List<ComponentHealth>()
        };

        // Check LLM
        if (_llmClient != null)
        {
            result.Checks.Add(await CheckLLMHealthAsync(ct));
        }

        // Check Database
        result.Checks.Add(await CheckDatabaseHealthAsync(ct));

        // Check Qdrant
        if (_qdrantService != null)
        {
            result.Checks.Add(await CheckQdrantHealthAsync(ct));
        }

        // Overall status
        result.OverallStatus = result.Checks.All(c => c.Status == HealthStatus.Healthy)
            ? HealthStatus.Healthy
            : result.Checks.Any(c => c.Status == HealthStatus.Unhealthy)
                ? HealthStatus.Unhealthy
                : HealthStatus.Degraded;

        _logger.LogInformation("Health check completed: {Status}", result.OverallStatus);

        return result;
    }

    private async Task<ComponentHealth> CheckLLMHealthAsync(CancellationToken ct)
    {
        var health = new ComponentHealth
        {
            Component = "LLM",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // Simple ping test
            var response = await _llmClient!.CompleteAsync("ping", ct);

            health.Status = !string.IsNullOrEmpty(response)
                ? HealthStatus.Healthy
                : HealthStatus.Degraded;

            health.Message = health.Status == HealthStatus.Healthy
                ? "LLM responding normally"
                : "LLM response empty";

            health.ResponseTimeMs = 0; // Would need to measure actual time
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"LLM check failed: {ex.Message}";
            _logger.LogError(ex, "LLM health check failed");
        }

        return health;
    }

    private async Task<ComponentHealth> CheckDatabaseHealthAsync(CancellationToken ct)
    {
        var health = new ComponentHealth
        {
            Component = "Database",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // Create a scope to resolve the scoped IDatabaseAdapter
            using var scope = _serviceProvider.CreateScope();
            var databaseAdapter = scope.ServiceProvider.GetService<IDatabaseAdapter>();

            if (databaseAdapter == null || _connectionString == null)
            {
                health.Status = HealthStatus.Degraded;
                health.Message = _connectionString == null
                    ? "No connection string configured"
                    : "Database adapter not available";
                return health;
            }

            // Test connection with simple query
            var startTime = DateTime.UtcNow;
            var canConnect = await databaseAdapter.TestConnectionAsync(_connectionString, ct);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            health.Status = canConnect ? HealthStatus.Healthy : HealthStatus.Unhealthy;
            health.Message = canConnect
                ? "Database connection successful"
                : "Database connection failed";
            health.ResponseTimeMs = (long)responseTime;
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"Database check failed: {ex.Message}";
            _logger.LogError(ex, "Database health check failed");
        }

        return health;
    }

    private async Task<ComponentHealth> CheckQdrantHealthAsync(CancellationToken ct)
    {
        var health = new ComponentHealth
        {
            Component = "Qdrant",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var startTime = DateTime.UtcNow;
            var exists = await _qdrantService!.CollectionExistsAsync(ct);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            health.Status = HealthStatus.Healthy;
            health.Message = exists
                ? "Qdrant collection accessible"
                : "Qdrant accessible but collection not found";
            health.ResponseTimeMs = (long)responseTime;

            if (!exists)
            {
                health.Status = HealthStatus.Degraded;
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"Qdrant check failed: {ex.Message}";
            _logger.LogError(ex, "Qdrant health check failed");
        }

        return health;
    }

    /// <summary>
    /// Quick health check (just status, no details)
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CheckHealthAsync(ct);
            return result.OverallStatus == HealthStatus.Healthy;
        }
        catch
        {
            return false;
        }
    }
}

public class HealthCheckResult
{
    public DateTime Timestamp { get; set; }
    public HealthStatus OverallStatus { get; set; }
    public List<ComponentHealth> Checks { get; set; } = new();

    public string ToFormattedString()
    {
        var lines = new List<string>
        {
            "=".PadRight(60, '='),
            $"Health Check - {Timestamp:yyyy-MM-dd HH:mm:ss}",
            $"Overall Status: {OverallStatus}",
            "=".PadRight(60, '='),
            ""
        };

        foreach (var check in Checks)
        {
            var statusIcon = check.Status switch
            {
                HealthStatus.Healthy => "✓",
                HealthStatus.Degraded => "⚠",
                HealthStatus.Unhealthy => "✗",
                _ => "?"
            };

            lines.Add($"{statusIcon} {check.Component,-15} {check.Status,-10} {check.Message}");

            if (check.ResponseTimeMs > 0)
            {
                lines.Add($"  Response Time: {check.ResponseTimeMs}ms");
            }
        }

        lines.Add("");
        lines.Add("=".PadRight(60, '='));

        return string.Join(Environment.NewLine, lines);
    }
}

public class ComponentHealth
{
    public string Component { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public long ResponseTimeMs { get; set; }
    public DateTime CheckedAt { get; set; }
}

public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}
