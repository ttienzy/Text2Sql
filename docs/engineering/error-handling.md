# Error Handling & Resilience

## 1. Error Handling Architecture

### Base Error Handler

**BaseErrorHandler.cs** - Foundation cho tất cả error handlers:

```csharp
public abstract class BaseErrorHandler
{
    protected async Task<T> HandleAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        if (!error.IsRecoverable)
        {
            throw CreateException(error);
        }
        
        return await ApplyRetryStrategyAsync(operation, error, cancellationToken);
    }
}
```

### Retry Strategies

#### 1. Immediate Retry
**Use case**: Transient network errors, temporary locks
```csharp
protected async Task<T> RetryImmediateAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    CancellationToken cancellationToken)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            if (attempt >= maxRetries) throw;
            // No delay, retry immediately
        }
    }
}
```

#### 2. Exponential Backoff
**Use case**: Rate limits, service overload
```csharp
protected async Task<T> RetryWithExponentialBackoffAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    CancellationToken cancellationToken)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            if (attempt >= maxRetries) throw;
            
            var delaySeconds = Math.Pow(2, attempt);  // 2, 4, 8, 16...
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
    }
}
```

#### 3. Fixed Wait
**Use case**: Database deadlocks, connection pool exhaustion
```csharp
protected async Task<T> RetryWithWaitAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    CancellationToken cancellationToken)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            if (attempt >= maxRetries) throw;
            
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}
```

#### 4. Circuit Breaker
**Use case**: Cascading failures, service unavailability
```csharp
protected async Task<T> RetryWithCircuitBreakerAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    CancellationToken cancellationToken)
{
    // Simplified - production should use Polly library
    if (_circuitState == CircuitState.Open)
    {
        throw new CircuitBreakerOpenException("Circuit breaker is open");
    }
    
    try
    {
        var result = await operation();
        _circuitState = CircuitState.Closed;
        return result;
    }
    catch (Exception)
    {
        _failureCount++;
        if (_failureCount >= _threshold)
        {
            _circuitState = CircuitState.Open;
            _circuitOpenTime = DateTime.UtcNow;
        }
        throw;
    }
}
```

## 2. LLM Error Handler

### Rate Limit Handling

**LLMErrorHandler.cs**:
```csharp
public async Task<T> HandleLLMErrorAsync<T>(
    Func<Task<T>> operation,
    Exception exception,
    CancellationToken cancellationToken)
{
    // Check if still in rate limit cooldown
    if (IsInRateLimitCooldown())
    {
        var waitTime = (_rateLimitResetTime!.Value - DateTime.UtcNow).TotalSeconds;
        throw new RateLimitException($"Rate limit active. Retry after {waitTime:F0} seconds");
    }
    
    var sqlError = _errorAnalyzer.AnalyzeLLMError(exception);
    
    switch (sqlError.Type)
    {
        case SqlErrorType.LLMRateLimitExceeded:
            return await HandleRateLimitAsync(operation, sqlError, cancellationToken);
            
        case SqlErrorType.LLMQuotaExceeded:
            throw new QuotaExceededException(sqlError.ErrorMessage);
            
        case SqlErrorType.LLMInvalidApiKey:
            throw new LLMApiException(sqlError.ErrorMessage, 401, exception);
            
        default:
            return await HandleAsync(operation, sqlError, cancellationToken);
    }
}
```

### Rate Limit Recovery

```csharp
private async Task<T> HandleRateLimitAsync<T>(
    Func<Task<T>> operation,
    SqlError error,
    CancellationToken cancellationToken)
{
    // Set rate limit reset time
    _rateLimitResetTime = DateTime.UtcNow.AddSeconds(_rateLimitRetryAfterSeconds);
    
    _logger.LogWarning(
        "Rate limit hit. Waiting {Seconds}s until {ResetTime}",
        _rateLimitRetryAfterSeconds,
        _rateLimitResetTime);
    
    // Wait for rate limit to reset
    await Task.Delay(TimeSpan.FromSeconds(_rateLimitRetryAfterSeconds), cancellationToken);
    
    // Retry after waiting
    try
    {
        var result = await operation();
        ClearRateLimit();  // Success - clear rate limit
        return result;
    }
    catch (Exception ex)
    {
        throw new RateLimitException("Rate limit retry failed", _rateLimitRetryAfterSeconds);
    }
}
```

### Quota Management

**Behavior**:
- Quota exceeded → No retry (permanent failure)
- Log error với detailed information
- Return user-friendly message
- Suggest alternative actions (use different API key, upgrade plan)

## 3. SQL Error Handler

### Invalid Column/Table Detection

**SqlErrorHandler.cs**:
```csharp
public async Task<T> HandleSqlErrorAsync<T>(
    Func<Task<T>> operation,
    Exception exception,
    DatabaseSchema schema,
    CancellationToken cancellationToken)
{
    var sqlError = _errorAnalyzer.AnalyzeSqlError(exception);
    
    switch (sqlError.Type)
    {
        case SqlErrorType.InvalidColumn:
            return await HandleInvalidColumnAsync(operation, sqlError, schema, cancellationToken);
            
        case SqlErrorType.InvalidTable:
            return await HandleInvalidTableAsync(operation, sqlError, schema, cancellationToken);
            
        case SqlErrorType.SyntaxError:
            return await HandleSyntaxErrorAsync(operation, sqlError, cancellationToken);
            
        default:
            return await HandleAsync(operation, sqlError, cancellationToken);
    }
}
```

### Column Suggestion

```csharp
private async Task<T> HandleInvalidColumnAsync<T>(
    Func<Task<T>> operation,
    SqlError error,
    DatabaseSchema schema,
    CancellationToken cancellationToken)
{
    // Extract invalid column name from error message
    var invalidColumn = ExtractColumnName(error.ErrorMessage);
    
    // Find similar columns using Levenshtein distance
    var suggestions = FindSimilarColumns(invalidColumn, schema);
    
    _logger.LogWarning(
        "Invalid column '{Column}'. Suggestions: {Suggestions}",
        invalidColumn,
        string.Join(", ", suggestions));
    
    // Auto-correct if high confidence match
    if (suggestions.Any() && suggestions.First().Confidence > 0.9)
    {
        var correctedSql = ReplaceSqlColumn(error.Sql, invalidColumn, suggestions.First().Name);
        return await ExecuteCorrectedSqlAsync<T>(correctedSql, cancellationToken);
    }
    
    throw new SqlExecutionException(
        $"Invalid column '{invalidColumn}'. Did you mean: {string.Join(", ", suggestions.Select(s => s.Name))}?");
}
```

## 4. Connection Error Handler

### Connection Pool Management

**ConnectionErrorHandler.cs**:
```csharp
public async Task<T> HandleConnectionErrorAsync<T>(
    Func<Task<T>> operation,
    Exception exception,
    CancellationToken cancellationToken)
{
    var sqlError = _errorAnalyzer.AnalyzeConnectionError(exception);
    
    switch (sqlError.Type)
    {
        case SqlErrorType.ConnectionTimeout:
            return await RetryWithExponentialBackoffAsync(operation, 3, cancellationToken);
            
        case SqlErrorType.ConnectionPoolExhausted:
            return await RetryWithWaitAsync(operation, 3, cancellationToken);
            
        case SqlErrorType.NetworkError:
            return await RetryWithCircuitBreakerAsync(operation, 3, cancellationToken);
            
        default:
            throw CreateException(sqlError);
    }
}
```

### Circuit Breaker Pattern

**States**:
- **Closed**: Normal operation, requests pass through
- **Open**: Too many failures, requests fail immediately
- **Half-Open**: Testing if service recovered

```csharp
private enum CircuitState { Closed, Open, HalfOpen }

private CircuitState _circuitState = CircuitState.Closed;
private int _failureCount = 0;
private DateTime? _circuitOpenTime = null;
private const int FailureThreshold = 5;
private const int CircuitOpenDurationSeconds = 60;

private bool ShouldAttemptReset()
{
    if (_circuitState != CircuitState.Open) return false;
    if (_circuitOpenTime == null) return false;
    
    var elapsed = DateTime.UtcNow - _circuitOpenTime.Value;
    return elapsed.TotalSeconds >= CircuitOpenDurationSeconds;
}
```

## 5. Vector DB Error Handler

### Fallback Strategy

**VectorDBErrorHandler.cs**:
```csharp
protected override async Task<T> RetryWithFallbackAsync<T>(
    Func<Task<T>> operation,
    SqlError error,
    CancellationToken cancellationToken)
{
    try
    {
        // Try Qdrant first
        return await RetryWithExponentialBackoffAsync(operation, 3, cancellationToken);
    }
    catch (VectorDBException ex)
    {
        _logger.LogWarning(ex, "Qdrant unavailable, falling back to in-memory store");
        
        // Fallback to in-memory vector store
        return await _inMemoryStore.SearchAsync(...);
    }
}
```

### In-Memory Vector Store

**Behavior**:
- Brute-force cosine similarity search
- No persistence (data lost on restart)
- Performance degradation for large schemas (> 100 tables)
- Automatic switch back to Qdrant when available

## 6. Failure Modes & Recovery

### Scenario 1: LLM Returns Invalid Format

**Problem**: LLM returns text instead of JSON
```
Response: "Here's the SQL query: SELECT * FROM Customers"
Expected: {"sql": "SELECT * FROM Customers", "confidence": 0.95}
```

**Recovery**:
```csharp
private string ExtractSqlFromJson(string response)
{
    try
    {
        var jsonDoc = JsonDocument.Parse(response);
        return jsonDoc.RootElement.GetProperty("sql").GetString();
    }
    catch (JsonException)
    {
        // Fallback: Extract from markdown code blocks
        return ExtractSqlFromMarkdown(response);
    }
}
```

### Scenario 2: SQL Execution Error

**Problem**: Generated SQL has syntax error or invalid table
```sql
SELECT * FROM Custmers WHERE CustomerID = 1  -- Typo: Custmers
```

**Recovery** (Self-Correction):
```csharp
private async Task<(SqlExecutionResult, List<CorrectionAttempt>)> ExecuteWithSelfCorrectionAsync(
    string sql,
    RetrievedSchemaContext schema,
    IntentAnalysis intent,
    CancellationToken cancellationToken)
{
    var corrections = new List<CorrectionAttempt>();
    var currentSql = sql;
    
    for (int attempt = 1; attempt <= MaxSelfCorrectionAttempts; attempt++)
    {
        var result = await _sqlExecutor.ExecuteAsync(currentSql, cancellationToken);
        
        if (result.Success)
        {
            return (result, corrections);
        }
        
        // Self-correct using LLM
        var correctedSql = await _sqlGenerator.CorrectSqlAsync(
            currentSql,
            result.ErrorMessage,
            schema,
            intent,
            cancellationToken);
        
        corrections.Add(new CorrectionAttempt
        {
            Attempt = attempt,
            OriginalSql = currentSql,
            CorrectedSql = correctedSql,
            Error = result.ErrorMessage
        });
        
        currentSql = correctedSql;
    }
    
    // All attempts failed
    return (new SqlExecutionResult { Success = false, ErrorMessage = "Max correction attempts exceeded" }, corrections);
}
```

### Scenario 3: Qdrant Unavailable

**Problem**: Qdrant service is down or unreachable
```
Error: Cannot connect to Qdrant at http://localhost:6333
```

**Recovery**:
1. Retry with exponential backoff (3 attempts)
2. If still fails, fallback to in-memory vector store
3. Log warning and continue processing
4. Automatic switch back when Qdrant recovers

### Scenario 4: Timeout

**Problem**: LLM API call or SQL execution takes too long
```
Error: Operation timed out after 30 seconds
```

**Recovery**:
```csharp
public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
    
    try
    {
        return await ExecuteInternalAsync(request, cts.Token);
    }
    catch (OperationCanceledException)
    {
        if (ct.IsCancellationRequested)
        {
            throw;  // User cancelled
        }
        else
        {
            throw new TimeoutException($"Operation timed out after {request.TimeoutSeconds} seconds");
        }
    }
}
```

## 7. CancellationToken Propagation

### Best Practices

**DO**:
```csharp
// ✅ Pass CancellationToken to all async methods
public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct = default)
{
    var schema = await _schemaCache.GetAsync(request.ConnectionId, ct);
    var sql = await _sqlGenerator.GenerateAsync(request.Question, schema, ct);
    var result = await _sqlExecutor.ExecuteAsync(sql, ct);
    return result;
}

// ✅ Check cancellation before expensive operations
if (ct.IsCancellationRequested)
{
    throw new OperationCanceledException();
}
```

**DON'T**:
```csharp
// ❌ Don't ignore CancellationToken
public async Task<QueryResult> ExecuteAsync(QueryRequest request)
{
    var schema = await _schemaCache.GetAsync(request.ConnectionId);  // Missing ct
    // ...
}

// ❌ Don't catch OperationCanceledException without rethrowing
try
{
    await operation(ct);
}
catch (OperationCanceledException)
{
    // Swallowed - bad!
}
```

## 8. Logging Strategy

### Log Levels

**Information**:
- Request start/end
- Pipeline selection
- LLM calls count
- Execution time

**Warning**:
- Retry attempts
- Fallback activation
- Rate limit hit
- Schema not found

**Error**:
- Unrecoverable errors
- Max retries exceeded
- Configuration errors
- Security violations

### Structured Logging

```csharp
_logger.LogInformation(
    "[{Component}] {Action} - {Metric}: {Value}",
    "EnhancedAgent",
    "ProcessQuery",
    "ExecutionTime",
    stopwatch.ElapsedMilliseconds);

_logger.LogWarning(
    "[{Component}] {Action} failed - Attempt: {Attempt}/{Max}, Error: {Error}",
    "LLMErrorHandler",
    "RetryWithBackoff",
    attempt,
    maxRetries,
    ex.Message);

_logger.LogError(
    ex,
    "[{Component}] {Action} failed permanently - {Context}",
    "SqlExecutor",
    "ExecuteQuery",
    new { Sql = sql, ConnectionId = connectionId });
```

### Correlation ID

**Usage**:
```csharp
using (LogContext.PushProperty("CorrelationId", correlationId))
{
    _logger.LogInformation("Processing request");
    await ProcessAsync();
    _logger.LogInformation("Request completed");
}
```

**Output**:
```
[2026-03-27 10:15:30] [INFO] [CorrelationId: abc-123] Processing request
[2026-03-27 10:15:32] [INFO] [CorrelationId: abc-123] Request completed
```

## 9. Retry Configuration

### Recommended Settings

| Error Type | Strategy | Max Retries | Delay |
|-----------|----------|-------------|-------|
| Rate Limit | Exponential Backoff | 3 | 2^n seconds |
| Network Error | Exponential Backoff | 3 | 2^n seconds |
| Timeout | Immediate Retry | 2 | 0 seconds |
| Connection Pool | Fixed Wait | 3 | 5 seconds |
| Deadlock | Fixed Wait | 3 | 5 seconds |
| Invalid API Key | No Retry | 0 | N/A |
| Quota Exceeded | No Retry | 0 | N/A |
| Syntax Error | Self-Correction | 3 | 0 seconds |

### Configuration

**appsettings.json**:
```json
{
  "ErrorHandling": {
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 5,
    "CircuitBreakerThreshold": 5,
    "CircuitBreakerDurationSeconds": 60,
    "EnableSelfCorrection": true,
    "MaxSelfCorrectionAttempts": 3
  }
}
```

## 10. Production Debugging

### Debug Checklist

**When query fails**:
1. Check correlation ID in logs
2. Verify schema is loaded
3. Check LLM API key validity
4. Verify Qdrant connectivity
5. Check SQL syntax
6. Review error message and stack trace
7. Check retry attempts and outcomes

**Log Search Queries**:
```bash
# Find all errors for a correlation ID
grep "CorrelationId: abc-123" logs/api-*.log | grep ERROR

# Find all rate limit errors
grep "Rate limit" logs/api-*.log

# Find all self-correction attempts
grep "Self-correction" logs/api-*.log
```

### Common Error Patterns

**Pattern 1**: Repeated rate limit errors
```
[ERROR] Rate limit exceeded for 5 consecutive requests
```
**Solution**: Increase rate limit threshold or add delay between requests

**Pattern 2**: Circuit breaker constantly open
```
[ERROR] Circuit breaker open for Qdrant service
```
**Solution**: Check Qdrant health, restart service, verify network connectivity

**Pattern 3**: All self-correction attempts fail
```
[ERROR] Max correction attempts (3) exceeded for SQL generation
```
**Solution**: Review schema quality, check LLM prompt, verify table/column names
