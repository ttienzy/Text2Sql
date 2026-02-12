namespace TextToSqlAgent.Infrastructure.Configuration;

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeout { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}