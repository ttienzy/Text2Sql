namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for encrypting and decrypting connection passwords
/// </summary>
public interface IConnectionEncryptionService
{
    /// <summary>
    /// Encrypt a password for storage
    /// </summary>
    string EncryptPassword(string password, string connectionId);

    /// <summary>
    /// Decrypt a password for use
    /// </summary>
    string DecryptPassword(string encryptedPassword, string connectionId);

    /// <summary>
    /// Build a connection string from connection parameters
    /// </summary>
    string BuildConnectionString(string provider, string host, int port, string database, string username, string password);
}