using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TextToSqlAgent.Console.Configuration;

/// <summary>
/// Secure storage for sensitive configuration like API keys
/// Uses Windows DPAPI for encryption on Windows, basic encryption on other platforms
/// </summary>
public class SecureConfigStore
{
    private readonly string _configDirectory;
    private readonly string _configFilePath;

    public SecureConfigStore()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextToSqlAgent");

        _configFilePath = Path.Combine(_configDirectory, "secure-config.dat");

        Directory.CreateDirectory(_configDirectory);
    }

    public class SecureConfig
    {
        public string? OpenAIApiKey { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsConfigured { get; set; }
    }

    /// <summary>
    /// Save configuration securely
    /// </summary>
    public void SaveConfig(SecureConfig config)
    {
        try
        {
            config.LastUpdated = DateTime.Now;
            config.IsConfigured = !string.IsNullOrEmpty(config.OpenAIApiKey);

            var json = JsonSerializer.Serialize(config);
            var encrypted = EncryptString(json);

            File.WriteAllBytes(_configFilePath, encrypted);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save secure configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Load configuration securely
    /// </summary>
    public SecureConfig LoadConfig()
    {
        if (!File.Exists(_configFilePath))
        {
            return new SecureConfig { IsConfigured = false };
        }

        try
        {
            var encrypted = File.ReadAllBytes(_configFilePath);
            var json = DecryptString(encrypted);

            return JsonSerializer.Deserialize<SecureConfig>(json)
                ?? new SecureConfig { IsConfigured = false };
        }
        catch
        {
            // If decryption fails, return empty config
            return new SecureConfig { IsConfigured = false };
        }
    }

    /// <summary>
    /// Check if configuration exists and is valid
    /// </summary>
    public bool IsConfigured()
    {
        var config = LoadConfig();
        return config.IsConfigured && !string.IsNullOrEmpty(config.OpenAIApiKey);
    }

    /// <summary>
    /// Clear all stored configuration
    /// </summary>
    public void ClearConfig()
    {
        if (File.Exists(_configFilePath))
        {
            File.Delete(_configFilePath);
        }
    }

    /// <summary>
    /// Get configuration directory path
    /// </summary>
    public string GetConfigDirectory() => _configDirectory;

    #region Encryption/Decryption

    private byte[] EncryptString(string plainText)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use Windows DPAPI for better security
            return ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                null,
                DataProtectionScope.CurrentUser);
        }
        else
        {
            // Basic encryption for non-Windows platforms
            // Note: This is not as secure as DPAPI, but better than plain text
            return BasicEncrypt(plainText);
        }
    }

    private string DecryptString(byte[] cipherText)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use Windows DPAPI
            var decrypted = ProtectedData.Unprotect(
                cipherText,
                null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        else
        {
            // Basic decryption for non-Windows platforms
            return BasicDecrypt(cipherText);
        }
    }

    private byte[] BasicEncrypt(string plainText)
    {
        // Simple XOR encryption with machine-specific key
        var key = GetMachineKey();
        var data = Encoding.UTF8.GetBytes(plainText);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= key[i % key.Length];
        }

        return data;
    }

    private string BasicDecrypt(byte[] cipherText)
    {
        // Simple XOR decryption
        var key = GetMachineKey();

        for (int i = 0; i < cipherText.Length; i++)
        {
            cipherText[i] ^= key[i % key.Length];
        }

        return Encoding.UTF8.GetString(cipherText);
    }

    private byte[] GetMachineKey()
    {
        // Generate a machine-specific key
        var machineId = Environment.MachineName + Environment.UserName;
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineId));
    }

    #endregion
}
