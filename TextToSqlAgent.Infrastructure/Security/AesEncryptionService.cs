using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TextToSqlAgent.Infrastructure.Security;

/// <summary>
/// AES-256 encryption service for securing sensitive data like database passwords
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts a plain text string using AES-256
    /// </summary>
    /// <param name="plainText">The text to encrypt</param>
    /// <returns>Base64 encoded encrypted string</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts an encrypted string using AES-256
    /// </summary>
    /// <param name="cipherText">Base64 encoded encrypted string</param>
    /// <returns>Decrypted plain text</returns>
    string Decrypt(string cipherText);
}

/// <summary>
/// AES-256-CBC encryption implementation
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private const int KeySize = 256;
    private const int IvSize = 16;

    /// <summary>
    /// Creates a new AES encryption service with the specified key and IV
    /// </summary>
    /// <param name="key">32-byte encryption key (will be hashed if longer)</param>
    /// <param name="iv">16-byte IV (optional, will be generated if null)</param>
    public AesEncryptionService(string key, byte[]? iv = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key), "Encryption key cannot be null or empty");
        }

        // Hash the key to get exactly 32 bytes for AES-256
        using var sha256 = SHA256.Create();
        _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));

        // Use provided IV or generate a new one
        _iv = iv ?? GenerateRandomIv();
    }

    /// <summary>
    /// Creates AES encryption service using a configuration key
    /// </summary>
    public AesEncryptionService()
    {
        // Use a machine-specific key based on environment
        var machineKey = Environment.MachineName + "_TextToSqlAgent_SecureKey_2024";

        using var sha256 = SHA256.Create();
        _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineKey));
        _iv = GenerateRandomIv();
    }

    private static byte[] GenerateRandomIv()
    {
        var iv = new byte[IvSize];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(iv);
        return iv;
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _key;
            aes.IV = _iv;

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();

            // Prepend IV to the encrypted data for later decryption
            msEncrypt.Write(_iv, 0, _iv.Length);

            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }

            return Convert.ToBase64String(msEncrypt.ToArray());
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Failed to encrypt data: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            // Extract IV from the beginning of the cipher
            var iv = new byte[IvSize];
            var cipher = new byte[fullCipher.Length - IvSize];

            Array.Copy(fullCipher, 0, iv, 0, IvSize);
            Array.Copy(fullCipher, IvSize, cipher, 0, cipher.Length);

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(cipher);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Failed to decrypt data: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Static helper for quick encryption operations
/// </summary>
public static class EncryptionHelper
{
    private static readonly Lazy<IEncryptionService> _defaultService =
        new(() => new AesEncryptionService());

    /// <summary>
    /// Gets the default encryption service
    /// </summary>
    public static IEncryptionService Default => _defaultService.Value;

    /// <summary>
    /// Encrypts a string using the default service
    /// </summary>
    public static string Encrypt(string plainText) => Default.Encrypt(plainText);

    /// <summary>
    /// Decrypts a string using the default service
    /// </summary>
    public static string Decrypt(string cipherText) => Default.Decrypt(cipherText);
}
