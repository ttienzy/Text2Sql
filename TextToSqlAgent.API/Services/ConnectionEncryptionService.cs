using System.Security.Cryptography;
using System.Text;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Implementation of connection encryption service using AES-256-GCM
/// </summary>
public class ConnectionEncryptionService : IConnectionEncryptionService
{
    private readonly string _encryptionKey;
    private readonly ILogger<ConnectionEncryptionService> _logger;

    public ConnectionEncryptionService(IConfiguration configuration, ILogger<ConnectionEncryptionService> logger)
    {
        _encryptionKey = configuration["Encryption:Key"] ?? configuration["Jwt:Key"] ?? "DefaultEncryptionKey32CharactersLong!";
        _logger = logger;

        // Ensure key is at least 32 characters for AES-256
        if (_encryptionKey.Length < 32)
        {
            _encryptionKey = _encryptionKey.PadRight(32, '0');
        }
    }

    public string EncryptPassword(string password, string connectionId)
    {
        try
        {
            var key = DeriveKey(_encryptionKey, connectionId);
            return AesGcmEncrypt(password, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt password for connection {ConnectionId}", connectionId);
            throw new InvalidOperationException("Failed to encrypt password", ex);
        }
    }

    public string DecryptPassword(string encryptedPassword, string connectionId)
    {
        try
        {
            var key = DeriveKey(_encryptionKey, connectionId);
            return AesGcmDecrypt(encryptedPassword, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt password for connection {ConnectionId}", connectionId);
            throw new InvalidOperationException("Failed to decrypt password", ex);
        }
    }

    public string BuildConnectionString(string provider, string host, int port, string database, string username, string password)
    {
        return provider.ToLowerInvariant() switch
        {
            "sqlserver" => $"Server={host},{port};Database={database};User Id={username};Password={password};TrustServerCertificate=True;",
            "postgresql" => $"Host={host};Port={port};Database={database};Username={username};Password={password};",
            "mysql" => $"Server={host};Port={port};Database={database};Uid={username};Pwd={password};",
            "sqlite" => $"Data Source={database}",
            _ => throw new NotSupportedException($"Database provider '{provider}' is not supported")
        };
    }

    private static byte[] DeriveKey(string password, string salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, Encoding.UTF8.GetBytes(salt), 10000, HashAlgorithmName.SHA256, 32);
    }

    private static string AesGcmEncrypt(string plaintext, byte[] key)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12]; // 96-bit nonce for GCM
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16]; // 128-bit authentication tag

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine nonce + ciphertext + tag
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

        return Convert.ToBase64String(result);
    }

    private static string AesGcmDecrypt(string encryptedData, byte[] key)
    {
        var data = Convert.FromBase64String(encryptedData);

        var nonce = new byte[12];
        var ciphertext = new byte[data.Length - 12 - 16];
        var tag = new byte[16];

        Buffer.BlockCopy(data, 0, nonce, 0, 12);
        Buffer.BlockCopy(data, 12, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(data, 12 + ciphertext.Length, tag, 0, 16);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}