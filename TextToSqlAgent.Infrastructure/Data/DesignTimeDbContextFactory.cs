using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DotNetEnv;

namespace TextToSqlAgent.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF tools can generate migrations from the Infrastructure project.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Load .env from API project (one level up from Infrastructure)
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TextToSqlAgent.API", ".env");
        if (File.Exists(envPath)) Env.Load(envPath);

        var connectionString =
            Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ??
            Environment.GetEnvironmentVariable("IDENTITY_CONNECTION_STRING") ??
            "Server=.,1433;Database=TextToSqlAgentDB;User Id=sa;Password=123;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
