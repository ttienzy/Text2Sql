using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Exceptions;

namespace TextToSqlAgent.Infrastructure.Analysis;

public class SqlErrorAnalyzer
{
    private readonly ILogger<SqlErrorAnalyzer> _logger;

    public SqlErrorAnalyzer(ILogger<SqlErrorAnalyzer> logger)
    {
        _logger = logger;
    }

    public SqlError AnalyzeError(string errorMessage, string sql)
    {
        _logger.LogInformation("[Error Analyzer] Phân tích lỗi SQL...");

        var error = new SqlError
        {
            ErrorMessage = errorMessage,
            IsRecoverable = true,
            Category = ErrorCategory.Database,
            Severity = ErrorSeverity.Medium
        };

        // Try each pattern in order
        if (TryAnalyzeParameterError(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeInvalidColumnName(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeInvalidObjectName(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeSyntaxError(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeAmbiguousColumn(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeTypeMismatch(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzePermissionError(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeConnectionError(errorMessage, error)) return FinalizeError(error);
        if (TryAnalyzeTimeoutError(errorMessage, error)) return FinalizeError(error);

        // Unknown error
        error.Type = SqlErrorType.Unknown;
        error.IsRecoverable = false;
        error.SuggestedFix = "Lỗi không xác định. Vui lòng kiểm tra log chi tiết.";
        _logger.LogWarning("[Error Analyzer] Unknown error type");

        return FinalizeError(error);
    }

    // ==========================================
    // PATTERN: Parameterized query error
    // ==========================================
    private bool TryAnalyzeParameterError(string errorMessage, SqlError error)
    {
        var pattern = @"Must declare the scalar variable ""(@\w+)""";
        var match = Regex.Match(errorMessage, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        error.Type = SqlErrorType.ParameterNotDeclared;
        error.InvalidElement = match.Groups[1].Value;
        error.SuggestedFix = $"SQL đang sử dụng parameter '{error.InvalidElement}'. Cần thay bằng literal value.";
        error.IsRecoverable = true;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_PARAM_001";
        error.RecommendedStrategy = RetryStrategy.ImmediateRetry;

        _logger.LogWarning("[Error Analyzer] Parameterized query detected: {Param}", error.InvalidElement);
        return true;
    }

    // ==========================================
    // PATTERN: Invalid column name
    // ==========================================
    private bool TryAnalyzeInvalidColumnName(string errorMessage, SqlError error)
    {
        var pattern = @"Invalid column name '(.+?)'";
        var match = Regex.Match(errorMessage, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        error.Type = SqlErrorType.InvalidColumnName;
        error.InvalidElement = match.Groups[1].Value;
        error.SuggestedFix = $"Column '{error.InvalidElement}' không tồn tại. Cần tìm column đúng trong schema.";
        error.IsRecoverable = true;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_COL_001";
        error.RecommendedStrategy = RetryStrategy.ImmediateRetry;

        _logger.LogWarning("[Error Analyzer] Invalid column: {Column}", error.InvalidElement);
        return true;
    }

    // ==========================================
    // PATTERN: Invalid object name (table)
    // ==========================================
    private bool TryAnalyzeInvalidObjectName(string errorMessage, SqlError error)
    {
        var pattern = @"Invalid object name '(.+?)'";
        var match = Regex.Match(errorMessage, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        error.Type = SqlErrorType.InvalidObjectName;
        error.InvalidElement = match.Groups[1].Value;
        error.SuggestedFix = $"Table '{error.InvalidElement}' không tồn tại. Kiểm tra lại tên bảng.";
        error.IsRecoverable = true;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_OBJ_001";
        error.RecommendedStrategy = RetryStrategy.ImmediateRetry;

        _logger.LogWarning("[Error Analyzer] Invalid table: {Table}", error.InvalidElement);
        return true;
    }

    // ==========================================
    // PATTERN: Syntax error
    // ==========================================
    private bool TryAnalyzeSyntaxError(string errorMessage, SqlError error)
    {
        if (!errorMessage.Contains("Incorrect syntax", StringComparison.OrdinalIgnoreCase))
            return false;

        error.Type = SqlErrorType.SyntaxError;
        error.SuggestedFix = "Có lỗi cú pháp SQL. Kiểm tra lại cấu trúc câu lệnh.";
        error.IsRecoverable = true;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_SYNTAX_001";
        error.RecommendedStrategy = RetryStrategy.ImmediateRetry;

        // Try to extract problematic keyword
        var nearPattern = @"near '(.+?)'";
        var nearMatch = Regex.Match(errorMessage, nearPattern, RegexOptions.IgnoreCase);
        if (nearMatch.Success)
        {
            error.InvalidElement = nearMatch.Groups[1].Value;
        }

        _logger.LogWarning("[Error Analyzer] Syntax error near: {Near}", error.InvalidElement);
        return true;
    }

    // ==========================================
    // PATTERN: Ambiguous column
    // ==========================================
    private bool TryAnalyzeAmbiguousColumn(string errorMessage, SqlError error)
    {
        var pattern = @"Ambiguous column name '(.+?)'";
        var match = Regex.Match(errorMessage, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        error.Type = SqlErrorType.AmbiguousColumnName;
        error.InvalidElement = match.Groups[1].Value;
        error.SuggestedFix = $"Column '{error.InvalidElement}' có trong nhiều bảng. Thêm alias (table.column).";
        error.IsRecoverable = true;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_AMBIG_001";
        error.RecommendedStrategy = RetryStrategy.ImmediateRetry;

        _logger.LogWarning("[Error Analyzer] Ambiguous column: {Column}", error.InvalidElement);
        return true;
    }

    // ==========================================
    // PATTERN: Type mismatch
    // ==========================================
    private bool TryAnalyzeTypeMismatch(string errorMessage, SqlError error)
    {
        var patterns = new[]
        {
            "conversion failed",
            "type mismatch",
            "arithmetic overflow",
            "invalid cast"
        };

        if (!patterns.Any(p => errorMessage.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        error.Type = SqlErrorType.TypeMismatch;
        error.SuggestedFix = "Lỗi chuyển đổi kiểu dữ liệu. Kiểm tra kiểu dữ liệu của column.";
        error.IsRecoverable = false;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_TYPE_001";
        error.RecommendedStrategy = RetryStrategy.NoRetry;

        _logger.LogWarning("[Error Analyzer] Type mismatch detected");
        return true;
    }

    // ==========================================
    // PATTERN: Permission denied
    // ==========================================
    private bool TryAnalyzePermissionError(string errorMessage, SqlError error)
    {
        var patterns = new[]
        {
            "permission",
            "denied",
            "unauthorized",
            "access is denied"
        };

        if (!patterns.Any(p => errorMessage.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        error.Type = SqlErrorType.PermissionDenied;
        error.IsRecoverable = false;
        error.Severity = ErrorSeverity.High;
        error.Category = ErrorCategory.Configuration;
        error.ErrorCode = "SQL_PERM_001";
        error.RecommendedStrategy = RetryStrategy.NoRetry;
        error.SuggestedFix = "Không có quyền thực thi. Kiểm tra quyền của user database.";

        _logger.LogError("[Error Analyzer] Permission denied");
        return true;
    }

    // ==========================================
    // PATTERN: Connection error
    // ==========================================
    private bool TryAnalyzeConnectionError(string errorMessage, SqlError error)
    {
        var patterns = new Dictionary<string, SqlErrorType>
        {
            { "connection", SqlErrorType.ConnectionFailed },
            { "network", SqlErrorType.NetworkError },
            { "refused", SqlErrorType.ConnectionRefused },
            { "cannot open database", SqlErrorType.DatabaseAccessDenied }
        };

        foreach (var (pattern, errorType) in patterns)
        {
            if (errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                error.Type = errorType;
                error.IsRecoverable = true;
                error.Severity = ErrorSeverity.High;
                error.Category = ErrorCategory.Network;
                error.ErrorCode = "SQL_CONN_001";
                error.RecommendedStrategy = RetryStrategy.ExponentialBackoff;
                error.MaxRetryAttempts = 3;
                error.SuggestedFix = "Không thể kết nối database. Kiểm tra connection string và network.";

                _logger.LogError("[Error Analyzer] Connection error: {Type}", errorType);
                return true;
            }
        }

        return false;
    }

    // ==========================================
    // PATTERN: Timeout
    // ==========================================
    private bool TryAnalyzeTimeoutError(string errorMessage, SqlError error)
    {
        if (!errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return false;

        error.Type = SqlErrorType.QueryTimeout;
        error.IsRecoverable = false;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "SQL_TIMEOUT_001";
        error.RecommendedStrategy = RetryStrategy.NoRetry;
        error.SuggestedFix = "Query chạy quá lâu. Cần tối ưu query hoặc thêm điều kiện lọc.";

        _logger.LogError("[Error Analyzer] Query timeout");
        return true;
    }

    private SqlError FinalizeError(SqlError error)
    {
        error.Timestamp = DateTime.UtcNow;
        return error;
    }

    // ==========================================
    // LLM API ERROR ANALYSIS
    // ==========================================
    public SqlError AnalyzeLLMError(Exception ex)
    {
        _logger.LogInformation("[Error Analyzer] Phân tích LLM API error...");

        var error = new SqlError
        {
            ErrorMessage = ex.Message,
            Category = ErrorCategory.LLM,
            Timestamp = DateTime.UtcNow
        };

        var message = ex.Message.ToLower();

        // Rate limit (429)
        if (message.Contains("rate limit") || message.Contains("too many requests") || message.Contains("429"))
        {
            error.Type = SqlErrorType.LLMRateLimitExceeded;
            error.IsRecoverable = true;
            error.Severity = ErrorSeverity.Medium;
            error.ErrorCode = "LLM_RATE_001";
            error.RecommendedStrategy = RetryStrategy.WaitAndRetry;
            error.MaxRetryAttempts = 5;
            error.SuggestedFix = "API rate limit đạt giới hạn. Đợi và thử lại.";

            _logger.LogWarning("[Error Analyzer] LLM rate limit exceeded");
            return error;
        }

        // Quota exceeded (403)
        if (message.Contains("quota") || message.Contains("exceeded") || message.Contains("403"))
        {
            error.Type = SqlErrorType.LLMQuotaExceeded;
            error.IsRecoverable = false;
            error.Severity = ErrorSeverity.Critical;
            error.ErrorCode = "LLM_QUOTA_001";
            error.RecommendedStrategy = RetryStrategy.NoRetry;
            error.SuggestedFix = "Quota API đã hết. Kiểm tra billing hoặc nâng cấp plan.";

            _logger.LogError("[Error Analyzer] LLM quota exceeded");
            return error;
        }

        // Invalid API key (401)
        if (message.Contains("unauthorized") || message.Contains("invalid") && message.Contains("key") || message.Contains("401"))
        {
            error.Type = SqlErrorType.LLMInvalidApiKey;
            error.IsRecoverable = false;
            error.Severity = ErrorSeverity.Critical;
            error.ErrorCode = "LLM_AUTH_001";
            error.RecommendedStrategy = RetryStrategy.NoRetry;
            error.SuggestedFix = "API key không hợp lệ. Kiểm tra lại config.";

            _logger.LogError("[Error Analyzer] Invalid LLM API key");
            return error;
        }

        // Service unavailable (503)
        if (message.Contains("service unavailable") || message.Contains("503"))
        {
            error.Type = SqlErrorType.LLMServiceUnavailable;
            error.IsRecoverable = true;
            error.Severity = ErrorSeverity.High;
            error.ErrorCode = "LLM_SERVICE_001";
            error.RecommendedStrategy = RetryStrategy.ExponentialBackoff;
            error.MaxRetryAttempts = 3;
            error.SuggestedFix = "LLM service tạm thời không khả dụng. Thử lại sau.";

            _logger.LogWarning("[Error Analyzer] LLM service unavailable");
            return error;
        }

        // Timeout
        if (message.Contains("timeout"))
        {
            error.Type = SqlErrorType.LLMTimeout;
            error.IsRecoverable = true;
            error.Severity = ErrorSeverity.Medium;
            error.ErrorCode = "LLM_TIMEOUT_001";
            error.RecommendedStrategy = RetryStrategy.ImmediateRetry;
            error.MaxRetryAttempts = 2;
            error.SuggestedFix = "LLM request timeout. Thử lại.";

            _logger.LogWarning("[Error Analyzer] LLM timeout");
            return error;
        }

        // Default LLM error
        error.Type = SqlErrorType.LLMBadRequest;
        error.IsRecoverable = false;
        error.Severity = ErrorSeverity.Medium;
        error.ErrorCode = "LLM_ERR_001";
        error.RecommendedStrategy = RetryStrategy.NoRetry;
        error.SuggestedFix = "Lỗi gọi LLM API. Kiểm tra log chi tiết.";

        _logger.LogError("[Error Analyzer] LLM API error");
        return error;
    }
}