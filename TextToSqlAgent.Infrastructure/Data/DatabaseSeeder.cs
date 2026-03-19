using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Data;

/// <summary>
/// Database seeder for development environment
/// </summary>
public class DatabaseSeeder
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Seed development data
    /// </summary>
    public async Task SeedAsync()
    {
        try
        {
            // Ensure database is created
            await _context.Database.EnsureCreatedAsync();

            // Seed users
            await SeedUsersAsync();

            // Seed sample connections (only in development)
            await SeedSampleConnectionsAsync();

            await _context.SaveChangesAsync();
            _logger.LogInformation("Database seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while seeding database");
            throw;
        }
    }

    private async Task SeedUsersAsync()
    {
        // Check if any users exist
        if (await _userManager.Users.AnyAsync())
        {
            _logger.LogInformation("Users already exist, skipping user seeding");
            return;
        }

        // Create development user
        var devUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "dev@texttosql.com",
            Email = "dev@texttosql.com",
            EmailConfirmed = true,
            FullName = "Development User"
        };

        var result = await _userManager.CreateAsync(devUser, "DevPassword123!");
        if (result.Succeeded)
        {
            _logger.LogInformation("Development user created successfully");
        }
        else
        {
            _logger.LogWarning("Failed to create development user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task SeedSampleConnectionsAsync()
    {
        // Only seed in development environment
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment != "Development")
        {
            return;
        }

        // Check if any connections exist
        if (await _context.Connections.AnyAsync())
        {
            _logger.LogInformation("Connections already exist, skipping connection seeding");
            return;
        }

        var devUser = await _userManager.FindByEmailAsync("dev@texttosql.com");
        if (devUser == null)
        {
            _logger.LogWarning("Development user not found, skipping connection seeding");
            return;
        }

        // Sample SQLite connection for development
        var sampleConnection = new Connection
        {
            Id = Guid.NewGuid().ToString(),
            UserId = devUser.Id,
            Name = "Sample SQLite Database",
            Provider = "SQLite",
            Host = "localhost",
            Port = 0,
            Database = "sample.db",
            Username = "",
            EncryptedPassword = "", // Empty for SQLite
            ConnectionString = "Data Source=sample.db",
            Description = "Sample SQLite database for development and testing",
            IsDefault = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Connections.Add(sampleConnection);
        _logger.LogInformation("Sample connection created for development");
    }
}
