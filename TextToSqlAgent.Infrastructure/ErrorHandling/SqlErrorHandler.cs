using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Handles SQL execution errors (syntax errors, invalid columns, etc.)
/// </summary>
public class SqlErrorHandler : BaseErrorHandler
{
    private readonly SqlErrorAnalyzer _errorAnalyzer;

    public SqlErrorHandler(
        ILogger<SqlErrorHandler> logger,
        SqlErrorAnalyzer errorAnalyzer) 
        : base(logger)
    {
        _errorAnalyzer = errorAnalyzer;
    }

    /// <summary>
    /// Handle SQL execution error
    /// </summary>
    public async Task<T> HandleSqlErrorAsync<T>(
        Func<Task<T>> operation,
        string errorMessage,
        string sql,
        CancellationToken cancellationToken = default)
    {
        // Analyze the SQL error
        var sqlError = _errorAnalyzer.AnalyzeError(errorMessage, sql);

        Logger.LogWarning(
            "[SQL Handler] SQL error detected: {Type} - {Message}",
            sqlError.Type,
            sqlError.ErrorMessage);

        // Some SQL errors should not be retried
        if (!ShouldRetrySqlError(sqlError))
        {
            Logger.LogError(
                "[SQL Handler] SQL error cannot be retried: {Type}",
                sqlError.Type);
            
            throw CreateSqlException(sqlError);
        }

        // Handle based on error type
        return sqlError.Type switch
        {
            SqlErrorType.InvalidColumnName => 
                await HandleInvalidColumnAsync(operation, sqlError, cancellationToken),
            
            SqlErrorType.InvalidObjectName => 
                await HandleInvalidTableAsync(operation, sqlError, cancellationToken),
            
            SqlErrorType.SyntaxError => 
                await HandleSyntaxErrorAsync(operation, sqlError, cancellationToken),
            
            SqlErrorType.AmbiguousColumnName => 
                await HandleAmbiguousColumnAsync(operation, sqlError, cancellationToken),
            
            _ => await HandleAsync(operation, sqlError, cancellationToken)
        };
    }

    private async Task<T> HandleInvalidColumnAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(
            "[SQL Handler] Invalid column detected: {Column}. Needs correction.",
            error.InvalidElement);

        // This should trigger self-correction in the orchestrator
        // For now, just throw with detailed info
        throw new AgentException(
            $"Invalid column name '{error.InvalidElement}'. " +
            $"Suggestion: {error.SuggestedFix}",
            error.ErrorCode ?? "SQL_COL_001",
            ErrorSeverity.Medium);
    }

    private async Task<T> HandleInvalidTableAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(
            "[SQL Handler] Invalid table detected: {Table}. Needs correction.",
            error.InvalidElement);

        // This should trigger self-correction in the orchestrator
        throw new AgentException(
            $"Invalid table name '{error.InvalidElement}'. " +
            $"Suggestion: {error.SuggestedFix}",
            error.ErrorCode ?? "SQL_OBJ_001",
            ErrorSeverity.Medium);
    }

    private async Task<T> HandleSyntaxErrorAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(
            "[SQL Handler] Syntax error detected near: {Near}. Needs correction.",
            error.InvalidElement);

        // Syntax errors need regeneration
        throw new AgentException(
            $"SQL syntax error. {error.SuggestedFix}",
            error.ErrorCode ?? "SQL_SYNTAX_001",
            ErrorSeverity.Medium);
    }

    private async Task<T> HandleAmbiguousColumnAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(
            "[SQL Handler] Ambiguous column detected: {Column}. Needs table qualifier.",
            error.InvalidElement);

        // Ambiguous columns need regeneration with proper aliases
        throw new AgentException(
            $"Ambiguous column '{error.InvalidElement}'. {error.SuggestedFix}",
            error.ErrorCode ?? "SQL_AMBIG_001",
            ErrorSeverity.Medium);
    }

    private bool ShouldRetrySqlError(SqlError error)
    {
        // These errors should NOT be retried - they need SQL regeneration
        var noRetryErrors = new[]
        {
            SqlErrorType.InvalidColumnName,
            SqlErrorType.InvalidObjectName,
            SqlErrorType.SyntaxError,
            SqlErrorType.AmbiguousColumnName,
            SqlErrorType.TypeMismatch,
            SqlErrorType.PermissionDenied,
            SqlErrorType.QueryTimeout
        };

        return !noRetryErrors.Contains(error.Type);
    }

    private Exception CreateSqlException(SqlError error)
    {
        return error.Type switch
        {
            SqlErrorType.PermissionDenied => new DatabasePermissionException(
                error.ErrorMessage),
            
            SqlErrorType.QueryTimeout => new DatabaseTimeoutException(
                error.ErrorMessage),
            
            _ => new AgentException(
                error.ErrorMessage,
                error.ErrorCode ?? "SQL_ERR_001",
                error.Severity)
        };
    }

    /// <summary>
    /// Analyze SQL error and return structured error info
    /// </summary>
    public SqlError AnalyzeError(string errorMessage, string sql)
    {
        return _errorAnalyzer.AnalyzeError(errorMessage, sql);
    }
}
