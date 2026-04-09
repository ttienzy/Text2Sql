namespace TextToSqlAgent.API.DTOs;

/// <summary>
/// IMP-1: Standardized API envelope for all success responses.
/// Provides consistent structure: { success, data, message, correlationId, timestamp }
/// Error responses continue to use RFC-7807 ProblemDetails.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public PaginationMeta? Pagination { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null, string? correlationId = null) =>
        new() { Success = true, Data = data, Message = message, CorrelationId = correlationId };

    public static ApiResponse<T> Empty(string? message = null, string? correlationId = null) =>
        new() { Success = true, Data = default, Message = message, CorrelationId = correlationId };

    public static ApiResponse<T> WithPagination(T data, int page, int pageSize, int total, string? correlationId = null) =>
        new()
        {
            Success = true,
            Data = data,
            CorrelationId = correlationId,
            Pagination = new PaginationMeta { Page = page, PageSize = pageSize, Total = total }
        };
}

/// <summary>
/// Non-generic variant for responses without a data payload.
/// </summary>
public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse Ok(string? message = null, string? correlationId = null) =>
        new() { Success = true, Message = message, CorrelationId = correlationId };
}

public sealed class PaginationMeta
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
