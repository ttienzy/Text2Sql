namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Redis cache configuration
/// </summary>
public class RedisConfig
{
    /// <summary>
    /// Redis server host (default: 127.0.0.1)
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Redis server port (default: 6379)
    /// </summary>
    public int Port { get; set; } = 6379;

    /// <summary>
    /// Redis password (empty for no authentication)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Redis instance name for key prefix
    /// </summary>
    public string? InstanceName { get; set; } = "TextToSqlAgent";
}
