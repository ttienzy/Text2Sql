namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Infrastructure.Database;

/// <summary>
/// Adapter: Wraps SqlExecutor to implement ISqlExecutor port
/// </summary>
public class SqlExecutorAdapter : ISqlExecutor
{
    private readonly SqlExecutor _executor;

    public SqlExecutorAdapter(SqlExecutor executor)
    {
        _executor = executor;
    }

    public Task<SqlExecutionResult> ExecuteAsync(
        string sql,
        CancellationToken ct = default)
    {
        return _executor.ExecuteAsync(sql, ct);
    }

    public Task<bool> ValidateConnectionAsync(CancellationToken ct = default)
    {
        return _executor.ValidateConnectionAsync(ct);
    }
}
