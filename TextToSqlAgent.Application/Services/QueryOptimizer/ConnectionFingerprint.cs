using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

internal static class ConnectionFingerprint
{
    public static string Compute(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "unknown";
        }

        string normalized;
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            normalized = string.Join("|", new[]
            {
                builder.DataSource ?? string.Empty,
                builder.InitialCatalog ?? string.Empty,
                builder.UserID ?? string.Empty,
                builder.IntegratedSecurity.ToString()
            });
        }
        catch
        {
            normalized = connectionString;
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
