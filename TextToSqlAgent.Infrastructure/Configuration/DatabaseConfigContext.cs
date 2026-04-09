namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Provides async-local storage for per-request database configuration overrides.
/// This prevents race conditions when multiple requests need different connection strings.
/// 
/// Usage in controllers:
/// using (DatabaseConfigContext.SetConnectionString(connectionString))
/// {
///     // All database operations in this async context will use the override
///     await ProcessQuery(...);
/// }
/// </summary>
public static class DatabaseConfigContext
{
    private static readonly AsyncLocal<string?> _connectionString = new();

    /// <summary>
    /// Gets the current async-local connection string override, or null if not set
    /// </summary>
    public static string? CurrentConnectionString => _connectionString.Value;

    /// <summary>
    /// Sets the connection string for the current async context.
    /// Returns an IDisposable that restores the previous value when disposed.
    /// </summary>
    public static IDisposable SetConnectionString(string connectionString)
    {
        var previous = _connectionString.Value;
        _connectionString.Value = connectionString;
        return new ConnectionStringScope(previous);
    }

    private class ConnectionStringScope : IDisposable
    {
        private readonly string? _previousValue;
        private bool _disposed;

        public ConnectionStringScope(string? previousValue)
        {
            _previousValue = previousValue;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connectionString.Value = _previousValue;
                _disposed = true;
            }
        }
    }
}
