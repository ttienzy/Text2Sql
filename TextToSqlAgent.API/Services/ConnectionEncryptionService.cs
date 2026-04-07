using Microsoft.AspNetCore.DataProtection;
using System.Text;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service for encrypting and decrypting connection passwords using ASP.NET Core Data Protection API.
/// Provides secure, key-rotation-friendly encryption for sensitive connection strings.
/// </summary>
public class ConnectionEncryptionService : IConnectionEncryptionService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<ConnectionEncryptionService> _logger;
    private const string Purpose = "ConnectionString.Protection.v1";

    public ConnectionEncryptionService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ConnectionEncryptionService> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Encrypt a password for storage using connection-specific purpose string.
    /// </summary>
    public string EncryptPassword(string password, string connectionId)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be null or empty", nameof(password));
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            throw new ArgumentException("ConnectionId cannot be null or empty", nameof(connectionId));
        }

        try
        {
            // Create a protector with connection-specific purpose for additional isolation
            var protector = _dataProtectionProvider.CreateProtector($"{Purpose}.{connectionId}");
            var encryptedBytes = protector.Protect(Encoding.UTF8.GetBytes(password));
            var encryptedBase64 = Convert.ToBase64String(encryptedBytes);

            _logger.LogDebug("[ConnectionEncryption] Encrypted password for connection {ConnectionId}", connectionId);
            return encryptedBase64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConnectionEncryption] Failed to encrypt password for connection {ConnectionId}", connectionId);
            throw new InvalidOperationException("Failed to encrypt password", ex);
        }
    }

    /// <summary>
    /// Decrypt a password for use using connection-specific purpose string.
    /// Falls back to plain text if decryption fails (for backward compatibility with unencrypted passwords).
    /// </summary>
    public string DecryptPassword(string encryptedPassword, string connectionId)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
        {
            throw new ArgumentException("Encrypted password cannot be null or empty", nameof(encryptedPassword));
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            throw new ArgumentException("ConnectionId cannot be null or empty", nameof(connectionId));
        }

        try
        {
            // Create a protector with connection-specific purpose
            var protector = _dataProtectionProvider.CreateProtector($"{Purpose}.{connectionId}");
            var encryptedBytes = Convert.FromBase64String(encryptedPassword);
            var decryptedBytes = protector.Unprotect(encryptedBytes);
            var decryptedPassword = Encoding.UTF8.GetString(decryptedBytes);

            _logger.LogDebug("[ConnectionEncryption] Decrypted password for connection {ConnectionId}", connectionId);
            return decryptedPassword;
        }
        catch (FormatException)
        {
            // Not Base64 - assume plain text password (backward compatibility)
            _logger.LogWarning("[ConnectionEncryption] Password for connection {ConnectionId} is not Base64 encoded - assuming plain text", connectionId);
            return encryptedPassword;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Decryption failed - assume plain text password (backward compatibility)
            _logger.LogWarning("[ConnectionEncryption] Failed to decrypt password for connection {ConnectionId} - assuming plain text", connectionId);
            return encryptedPassword;
        }
        catch (Exception ex)
        {
            // Other errors - log and return plain text
            _logger.LogWarning(ex, "[ConnectionEncryption] Unexpected error decrypting password for connection {ConnectionId} - assuming plain text", connectionId);
            return encryptedPassword;
        }
    }

    /// <summary>
    /// Build a connection string from connection parameters.
    /// Supports SQL Server, PostgreSQL, MySQL connection string formats.
    /// </summary>
    public string BuildConnectionString(string provider, string host, int port, string database, string username, string password)
    {
        if (string.IsNullOrEmpty(provider))
        {
            throw new ArgumentException("Provider cannot be null or empty", nameof(provider));
        }

        var normalizedProvider = provider.ToLowerInvariant();

        return normalizedProvider switch
        {
            "sqlserver" or "mssql" => BuildSqlServerConnectionString(host, port, database, username, password),
            "postgresql" or "postgres" => BuildPostgreSqlConnectionString(host, port, database, username, password),
            "mysql" => BuildMySqlConnectionString(host, port, database, username, password),
            _ => throw new NotSupportedException($"Database provider '{provider}' is not supported")
        };
    }

    /// <summary>
    /// Get connection string from Connection entity with backward compatibility.
    /// If ConnectionString field is empty, rebuilds from individual fields.
    /// </summary>
    public string GetConnectionString(TextToSqlAgent.Infrastructure.Entities.Connection connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        // If ConnectionString is populated, decrypt and return it
        if (!string.IsNullOrEmpty(connection.ConnectionString))
        {
            var decrypted = DecryptPassword(connection.ConnectionString, connection.Id);
            if (string.IsNullOrEmpty(decrypted))
            {
                _logger.LogWarning("[ConnectionEncryption] Decrypted ConnectionString is empty for connection {ConnectionId}", connection.Id);
                throw new InvalidOperationException($"Connection string is empty after decryption for connection {connection.Id}");
            }
            return decrypted;
        }

        // Backward compatibility: rebuild from individual fields
        _logger.LogWarning("[ConnectionEncryption] ConnectionString is empty for connection {ConnectionId}, rebuilding from fields", connection.Id);

        // Validate required fields
        if (string.IsNullOrEmpty(connection.EncryptedPassword))
        {
            _logger.LogError("[ConnectionEncryption] EncryptedPassword is empty for connection {ConnectionId}, cannot rebuild connection string", connection.Id);
            throw new InvalidOperationException($"Cannot build connection string for connection {connection.Id}: EncryptedPassword is empty");
        }

        if (string.IsNullOrEmpty(connection.Host) || string.IsNullOrEmpty(connection.Database) || string.IsNullOrEmpty(connection.Username))
        {
            _logger.LogError("[ConnectionEncryption] Missing required fields (Host/Database/Username) for connection {ConnectionId}", connection.Id);
            throw new InvalidOperationException($"Cannot build connection string for connection {connection.Id}: Missing required fields");
        }

        var decryptedPassword = DecryptPassword(connection.EncryptedPassword, connection.Id);
        if (string.IsNullOrEmpty(decryptedPassword))
        {
            _logger.LogError("[ConnectionEncryption] Decrypted password is empty for connection {ConnectionId}", connection.Id);
            throw new InvalidOperationException($"Cannot build connection string for connection {connection.Id}: Password is empty after decryption");
        }

        return BuildConnectionString(
            connection.Provider, connection.Host, connection.Port,
            connection.Database, connection.Username, decryptedPassword);
    }

    private static string BuildSqlServerConnectionString(string host, int port, string database, string username, string password)
    {
        var builder = new StringBuilder();
        builder.Append($"Server={host}");

        if (port > 0 && port != 1433) // 1433 is default SQL Server port
        {
            builder.Append($",{port}");
        }

        builder.Append($";Database={database}");
        builder.Append($";User Id={username}");
        builder.Append($";Password={password}");
        builder.Append(";TrustServerCertificate=True"); // For development
        builder.Append(";Encrypt=True");
        builder.Append(";Connection Timeout=30");

        return builder.ToString();
    }

    private static string BuildPostgreSqlConnectionString(string host, int port, string database, string username, string password)
    {
        var actualPort = port > 0 ? port : 5432; // Default PostgreSQL port
        return $"Host={host};Port={actualPort};Database={database};Username={username};Password={password};SSL Mode=Prefer;Timeout=30";
    }

    private static string BuildMySqlConnectionString(string host, int port, string database, string username, string password)
    {
        var actualPort = port > 0 ? port : 3306; // Default MySQL port
        return $"Server={host};Port={actualPort};Database={database};Uid={username};Pwd={password};SslMode=Preferred;Connection Timeout=30";
    }
}
