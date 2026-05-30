using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Infrastructure.VectorDB;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = FirstConfigured(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Configuration["IDENTITY_CONNECTION_STRING"],
    builder.Configuration["DATABASE_CONNECTION_STRING"],
    builder.Configuration["ConnectionStrings:DefaultConnection"],
    builder.Configuration["Database:ConnectionString"]);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string is required. Set ConnectionStrings__DefaultConnection, IDENTITY_CONNECTION_STRING, or DATABASE_CONNECTION_STRING.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(10, TimeSpan.FromSeconds(5), null)));

builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddHttpClient("Qdrant", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton(sp =>
{
    var config = new QdrantConfig();
    builder.Configuration.GetSection("Qdrant").Bind(config);

    var qdrantUrl = FirstConfigured(
        builder.Configuration["Qdrant:Url"],
        builder.Configuration["QDRANT_URL"]);

    if (Uri.TryCreate(qdrantUrl, UriKind.Absolute, out var uri))
    {
        config.Host = uri.Host;
        config.Port = uri.Port > 0 ? uri.Port : 6333;
    }

    return config;
});

builder.Services.AddSingleton<QdrantService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TextToSqlAgent.Migrator");

try
{
    logger.LogInformation("Starting TextToSqlAgent migration workflow");

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await RetryAsync(
            logger,
            "EF Core migrations",
            async ct => await db.Database.MigrateAsync(ct),
            CancellationToken.None);

        if (ShouldSeedDevelopmentData(builder.Configuration, host.Services.GetRequiredService<IHostEnvironment>()))
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await RetryAsync(
                logger,
                "development seed data",
                async _ => await seeder.SeedAsync(),
                CancellationToken.None);
        }
        else
        {
            logger.LogInformation("Development seed data disabled");
        }
    }

    using (var scope = host.Services.CreateScope())
    {
        var qdrant = scope.ServiceProvider.GetRequiredService<QdrantService>();
        await RetryAsync(
            logger,
            "Qdrant collection initialization",
            async ct => await qdrant.EnsureCollectionAsync(ct),
            CancellationToken.None);
    }

    logger.LogInformation("Migration workflow completed successfully");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Migration workflow failed");
    Environment.ExitCode = 1;
}

static string? FirstConfigured(params string?[] values)
{
    return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

static bool ShouldSeedDevelopmentData(IConfiguration configuration, IHostEnvironment environment)
{
    var configuredValue = configuration["Seed:DevelopmentData"];
    if (bool.TryParse(configuredValue, out var shouldSeed))
    {
        return shouldSeed;
    }

    return environment.IsDevelopment();
}

static async Task RetryAsync(
    ILogger logger,
    string operationName,
    Func<CancellationToken, Task> operation,
    CancellationToken cancellationToken)
{
    const int maxAttempts = 8;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            logger.LogInformation("Running {OperationName} (attempt {Attempt}/{MaxAttempts})", operationName, attempt, maxAttempts);
            await operation(cancellationToken);
            logger.LogInformation("{OperationName} completed", operationName);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(30, attempt * 5));
            logger.LogWarning(ex, "{OperationName} failed; retrying in {DelaySeconds}s", operationName, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
        }
    }

    await operation(cancellationToken);
}
