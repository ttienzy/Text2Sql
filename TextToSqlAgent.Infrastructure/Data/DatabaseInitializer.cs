using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Data;

/// <summary>
/// Database initialization and health check service
/// </summary>
public class DatabaseInitializer
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IHostEnvironment _environment;
    private readonly ILoggerFactory _loggerFactory;

    public DatabaseInitializer(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<DatabaseInitializer> logger,
        IHostEnvironment environment,
        ILoggerFactory loggerFactory)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _environment = environment;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Initialize database and run migrations
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Starting database initialization...");

            // Apply pending migrations
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                _logger.LogInformation("Applying {Count} pending migrations: {Migrations}",
                    pendingMigrations.Count(), string.Join(", ", pendingMigrations));

                await _context.Database.MigrateAsync();
                _logger.LogInformation("Database migrations applied successfully");
            }
            else
            {
                _logger.LogInformation("No pending migrations found");
            }

            // Verify database connectivity
            await VerifyDatabaseConnectivityAsync();

            // Seed data in development environment
            if (_environment.IsDevelopment())
            {
                var seederLogger = _loggerFactory.CreateLogger<DatabaseSeeder>();
                var seeder = new DatabaseSeeder(_context, _userManager, seederLogger);
                await seeder.SeedAsync();
            }

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed");
            throw;
        }
    }

    /// <summary>
    /// Verify database connectivity and basic operations
    /// </summary>
    public async Task<bool> VerifyDatabaseConnectivityAsync()
    {
        try
        {
            // Test basic connectivity
            await _context.Database.CanConnectAsync();

            // Test basic query
            var userCount = await _context.Users.CountAsync();
            _logger.LogInformation("Database connectivity verified. User count: {UserCount}", userCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connectivity verification failed");
            return false;
        }
    }

    /// <summary>
    /// Get database health status
    /// </summary>
    public async Task<DatabaseHealthStatus> GetHealthStatusAsync()
    {
        var status = new DatabaseHealthStatus
        {
            IsHealthy = false,
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // Check connectivity
            var canConnect = await _context.Database.CanConnectAsync();
            if (!canConnect)
            {
                status.Issues.Add("Cannot connect to database");
                return status;
            }

            // Check pending migrations
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                status.Issues.Add($"Pending migrations: {string.Join(", ", pendingMigrations)}");
            }

            // Check basic operations
            var userCount = await _context.Users.CountAsync();
            var connectionCount = await _context.Connections.CountAsync();
            var conversationCount = await _context.Conversations.CountAsync();

            status.Statistics = new Dictionary<string, object>
            {
                ["UserCount"] = userCount,
                ["ConnectionCount"] = connectionCount,
                ["ConversationCount"] = conversationCount,
                ["PendingMigrations"] = pendingMigrations.Count()
            };

            status.IsHealthy = !status.Issues.Any();
        }
        catch (Exception ex)
        {
            status.Issues.Add($"Health check failed: {ex.Message}");
        }

        return status;
    }
}

/// <summary>
/// Database health status information
/// </summary>
public class DatabaseHealthStatus
{
    public bool IsHealthy { get; set; }
    public DateTime CheckedAt { get; set; }
    public List<string> Issues { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
}
