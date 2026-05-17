using TextToSqlAgent.Core.Enums;

namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Provides async-local storage for per-request database configuration overrides.
/// This prevents race conditions when multiple requests need different connection strings.
/// 
/// Usage in controllers:
/// using (DatabaseConfigContext.SetDatabaseContext(connectionString, DatabaseProvider.PostgreSql))
/// {
///     // All database operations in this async context will use the override
///     await ProcessQuery(...);
/// }
/// 
/// Legacy usage (backward-compatible, defaults to no provider override):
/// using (DatabaseConfigContext.SetConnectionString(connectionString))
/// {
///     await ProcessQuery(...);
/// }
/// </summary>
public static class DatabaseConfigContext
{
    private static readonly AsyncLocal<string?> _connectionString = new();
    private static readonly AsyncLocal<DatabaseProvider?> _provider = new();

    /// <summary>
    /// Gets the current async-local connection string override, or null if not set
    /// </summary>
    public static string? CurrentConnectionString => _connectionString.Value;

    /// <summary>
    /// Gets the current async-local provider override, or null if not set
    /// </summary>
    public static DatabaseProvider? CurrentProvider => _provider.Value;

    /// <summary>
    /// Sets both connection string and provider for the current async context.
    /// Returns an IDisposable that restores the previous values when disposed.
    /// This is the preferred method for multi-database support.
    /// </summary>
    public static IDisposable SetDatabaseContext(string connectionString, DatabaseProvider provider)
    {
        var previousCs = _connectionString.Value;
        var previousProvider = _provider.Value;
        _connectionString.Value = connectionString;
        _provider.Value = provider;
        return new DatabaseContextScope(previousCs, previousProvider);
    }

    /// <summary>
    /// Sets the connection string for the current async context (legacy, backward-compatible).
    /// Does NOT override provider — existing callers continue working as before.
    /// Returns an IDisposable that restores the previous value when disposed.
    /// </summary>
    public static IDisposable SetConnectionString(string connectionString)
    {
        var previous = _connectionString.Value;
        _connectionString.Value = connectionString;
        return new ConnectionStringScope(previous);
    }

    private class DatabaseContextScope : IDisposable
    {
        private readonly string? _previousConnectionString;
        private readonly DatabaseProvider? _previousProvider;
        private bool _disposed;

        public DatabaseContextScope(string? previousConnectionString, DatabaseProvider? previousProvider)
        {
            _previousConnectionString = previousConnectionString;
            _previousProvider = previousProvider;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connectionString.Value = _previousConnectionString;
                _provider.Value = _previousProvider;
                _disposed = true;
            }
        }
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
