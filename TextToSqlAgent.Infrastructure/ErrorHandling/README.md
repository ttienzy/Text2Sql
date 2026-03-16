# Error Handling System

## Overview

Robust error handling system với specialized handlers cho từng loại lỗi trong TextToSqlAgent.

## Architecture

```
BaseErrorHandler (Abstract)
├── ConnectionErrorHandler  → Database connection errors
├── LLMErrorHandler        → LLM API errors (rate limits, quotas)
├── SqlErrorHandler        → SQL execution errors
└── VectorDBErrorHandler   → Vector database errors
```

## Features

### ✅ **BaseErrorHandler**

- Common retry strategies:
  - `ImmediateRetry`: Retry ngay không delay
  - `ExponentialBackoff`: Retry với delay tăng theo cấp số nhân (2^attempt)
  - `WaitAndRetry`: Retry với delay cố định
  - `CircuitBreaker`: Fail fast sau threshold
  - `Fallback`: Fallback strategy
- Automatic error analysis
- Configurable max retry attempts

### 🔌 **ConnectionErrorHandler**

- Circuit breaker pattern
- Intelligent connection retry
- Consecutive failure tracking
- Auto-reset after cooldown

**Circuit Breaker:**

- Opens after 5 consecutive failures
- Resets after 60 seconds
- Prevents cascading failures

### 🤖 **LLMErrorHandler**

- Rate limit awareness
- Quota management
- Smart retry strategies

**Handles:**

- Rate limit (429) → Wait and retry
- Quota exceeded (403) → No retry
- Invalid API key (401) → No retry
- Service unavailable (503) → Exponential backoff
- Timeout → Immediate retry

### 📊 **SqlErrorHandler**

- SQL error analysis
- Self-correction support
- Detailed error information

**Handles:**

- Invalid column/table → Trigger regeneration
- Syntax errors → Trigger regeneration
- Ambiguous columns → Trigger regeneration
- Permission denied → Fail
- Timeout → Fail

### 🗄️ **VectorDBErrorHandler**

- Vector DB connectivity
- Embedding failures
- Schema indexing errors

**Handles:**

- DB unavailable → Exponential backoff
- Schema not indexed → Fail with guidance
- Embedding failed → Wait and retry

## Usage

### 1️⃣ **Register in DI Container**

```csharp
// In Program.cs or Startup.cs
services.AddErrorHandlers();
```

### 2️⃣ **Inject Handlers**

```csharp
public class SqlExecutor
{
    private readonly ConnectionErrorHandler _connectionHandler;
    private readonly SqlErrorHandler _sqlHandler;

    public SqlExecutor(
        ConnectionErrorHandler connectionHandler,
        SqlErrorHandler sqlHandler)
    {
        _connectionHandler = connectionHandler;
        _sqlHandler = sqlHandler;
    }
}
```

### 3️⃣ **Use Handlers**

```csharp
// Handle connection errors
var result = await _connectionHandler.HandleConnectionErrorAsync(
    async () => await ExecuteQueryAsync(),
    exception,
    cancellationToken);

// Handle LLM errors
var response = await _llmHandler.HandleLLMErrorAsync(
    async () => await CallLLMAsync(),
    exception,
    cancellationToken);

// Analyze SQL errors
var sqlError = _sqlHandler.AnalyzeError(errorMessage, sql);
```

## Error Flow

### **Connection Error Flow**

```
1. Connection attempt fails
   ↓
2. ConnectionErrorHandler.HandleConnectionErrorAsync()
   ↓
3. Check circuit breaker state
   ↓
4. Circuit OPEN? → Fail fast
   ↓ NO
5. Circuit CLOSED → Retry with exponential backoff
   ↓
6a. Success → Reset failure counter
6b. Failure → Increment counter → Open circuit if threshold reached
```

### **LLM Error Flow**

```
1. LLM API call fails
   ↓
2. LLMErrorHandler.HandleLLMErrorAsync()
   ↓
3. Check if in rate limit cooldown
   ↓
4. In cooldown? → Fail with retry-after time
   ↓ NO
5. Analyze error type
   ↓
6. Rate limit → Wait 60s → Retry
7. Quota exceeded → Fail (no retry)
8. Invalid key → Fail (no retry)
9. Service unavailable → Exponential backoff
10. Timeout → Immediate retry
```

### **SQL Error Flow**

```
1. SQL execution fails
   ↓
2. SqlErrorHandler.AnalyzeError()
   ↓
3. Parse error message with regex patterns
   ↓
4. Categorize error (InvalidColumn, InvalidTable, Syntax, etc.)
   ↓
5. Return SqlError with:
   - Type
   - Severity
   - IsRecoverable
   - SuggestedFix
   - InvalidElement
   ↓
6. Orchestrator uses info for self-correction
```

## Error Response Structure

```csharp
SqlError
{
    Type: SqlErrorType                    // InvalidColumnName, LLMRateLimitExceeded, etc.
    Severity: ErrorSeverity              // Low, Medium, High, Critical
    Category: ErrorCategory              // Database, LLM, Network, etc.
    ErrorMessage: string                 // Original error message
    ErrorCode: string                    // "SQL_COL_001", "LLM_RATE_001", etc.
    InvalidElement: string?              // Column/table name causing error
    SuggestedFix: string?               // Human-readable suggested fix
    IsRecoverable: bool                 // Can this error be recovered?
    MaxRetryAttempts: int              // How many retries allowed
    RecommendedStrategy: RetryStrategy  // Which retry strategy to use
    Timestamp: DateTime                 // When did error occur
}
```

## Monitoring

### **Get Circuit Breaker Status**

```csharp
var status = _connectionHandler.GetCircuitBreakerStatus();
// Returns: "OPEN (resets in 45s)" or "CLOSED (failures: 2/5)"
```

### **Get Rate Limit Status**

```csharp
var status = _llmHandler.GetRateLimitStatus();
// Returns: "ACTIVE (resets in 30s)" or "NONE"
```

## Best Practices

1. ✅ **Always inject handlers via DI**
   - Don't `new` them manually
   - Let DI container manage lifecycle

2. ✅ **Use appropriate handler for each error type**
   - Connection → `ConnectionErrorHandler`
   - LLM API → `LLMErrorHandler`
   - SQL → `SqlErrorHandler`
   - Vector DB → `VectorDBErrorHandler`

3. ✅ **Check circuit breaker status before expensive operations**

   ```csharp
   if (_connectionHandler.GetCircuitBreakerStatus().Contains("OPEN"))
   {
       // Skip operation or use cached data
   }
   ```

4. ✅ **Log error details for debugging**

   ```csharp
   _logger.LogError(
       "Error Type: {Type}, Code: {Code}, Recoverable: {Recoverable}",
       error.Type,
       error.ErrorCode,
       error.IsRecoverable);
   ```

5. ✅ **Handle errors gracefully in UI**
   ```csharp
   if (!result.Success)
   {
       var errorDetails = result.ErrorDetails;
       var suggestedFix = errorDetails["SuggestedFix"];

       Console.WriteLine($"Error: {result.ErrorMessage}");
       Console.WriteLine($"Suggestion: {suggestedFix}");
   }
   ```

## Testing

### **Unit Tests**

```csharp
[Fact]
public async Task ConnectionHandler_ShouldOpenCircuitAfter5Failures()
{
    // Arrange
    var handler = new ConnectionErrorHandler(logger, analyzer);

    // Act: Fail 5 times
    for (int i = 0; i < 5; i++)
    {
        await Assert.ThrowsAsync<Exception>(async () =>
            await handler.HandleConnectionErrorAsync(
                () => throw new Exception("Connection failed"),
                new Exception(),
                CancellationToken.None));
    }

    // Assert
    var status = handler.GetCircuitBreakerStatus();
    Assert.Contains("OPEN", status);
}
```

## Configuration

All handlers use `appsettings.json` for configuration:

```json
{
  "Database": {
    "MaxRetryAttempts": 3,
    "CommandTimeout": 30
  },
  "Gemini": {
    "MaxTokens": 8192,
    "Temperature": 0.7
  }
}
```

## Error Codes Reference

### Database Errors

- `DB_CONN_001`: Connection failed
- `DB_TIMEOUT_001`: Query timeout
- `DB_PERM_001`: Permission denied

### SQL Errors

- `SQL_COL_001`: Invalid column name
- `SQL_OBJ_001`: Invalid table name
- `SQL_SYNTAX_001`: Syntax error
- `SQL_AMBIG_001`: Ambiguous column

### LLM Errors

- `LLM_RATE_001`: Rate limit exceeded
- `LLM_QUOTA_001`: Quota exceeded
- `LLM_AUTH_001`: Invalid API key
- `LLM_SERVICE_001`: Service unavailable
- `LLM_TIMEOUT_001`: Request timeout

### Vector DB Errors

- `VDB_UNAVAIL_001`: Vector DB unavailable
- `VDB_INDEX_001`: Schema not indexed
- `VDB_EMBED_001`: Embedding generation failed

## Troubleshooting

### Q: Circuit breaker keeps opening?

**A:** Check database connectivity. Circuit opens after 5 consecutive failures.

### Q: Rate limit errors even after waiting?

**A:** Check if retry-after time is correctly set. Default is 60s.

### Q: SQL errors not being corrected?

**A:** Ensure `SqlErrorAnalyzer` is properly analyzing errors. Check regex patterns.

### Q: Vector DB errors?

**A:** Verify Qdrant is running and accessible. Check API endpoint in config.

## Future Enhancements

- [ ] Metrics export (Prometheus)
- [ ] Distributed tracing (OpenTelemetry)
- [ ] Advanced circuit breaker (Polly integration)
- [ ] Error aggregation and reporting
- [ ] Auto-scaling based on error rates
