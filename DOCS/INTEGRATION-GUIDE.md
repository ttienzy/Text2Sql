# Phase 4 Integration Guide

## 📋 Checklist

### ✅ Phase 3: Error Handlers (COMPLETED)

- [x] BaseErrorHandler (abstract base)
- [x] ConnectionErrorHandler (circuit breaker)
- [x] LLMErrorHandler (rate limiting)
- [x] SqlErrorHandler (error analysis)
- [x] VectorDBErrorHandler (vector DB errors)
- [x] ErrorHandlerServiceExtensions (DI registration)
- [x] README documentation

### 🔄 Phase 4: Integration (TODO)

#### 1. Update Program.cs / DI Configuration

**File:** `TextToSqlAgent.Console/Program.cs`

```csharp
// Add this line after other service registrations
services.AddErrorHandlers();
```

#### 2. Update SqlExecutor Constructor Calls

**Files to update:**

- `TextToSqlAgent.Console/Agent/TextToSqlAgentOrchestrator.cs`
- Any other files creating `SqlExecutor`

**Before:**

```csharp
var sqlExecutor = new SqlExecutor(dbConfig, logger);
```

**After:**

```csharp
var sqlExecutor = new SqlExecutor(
    dbConfig,
    logger,
    connectionErrorHandler,  // Inject this
    sqlErrorHandler);        // Inject this
```

or via DI:

```csharp
// In constructor
public TextToSqlAgentOrchestrator(
    SqlExecutor sqlExecutor,  // Already has handlers injected
    ...)
{
    _sqlExecutor = sqlExecutor;
}
```

#### 3. Update GeminiClient Constructor Calls

**Files to update:**

- Files creating `GeminiClient` instances
- Plugin constructors

**Before:**

```csharp
var geminiClient = new GeminiClient(geminiConfig, logger);
```

**After:**

```csharp
var geminiClient = new GeminiClient(
    geminiConfig,
    logger,
    llmErrorHandler);  // Inject this
```

#### 4. Update TextToSqlAgentOrchestrator

**File:** `TextToSqlAgent.Console/Agent/TextToSqlAgentOrchestrator.cs`

##### 4a. Add Error Handlers to Constructor

```csharp
public class TextToSqlAgentOrchestrator
{
    private readonly SqlErrorHandler _sqlErrorHandler;
    private readonly VectorDBErrorHandler _vectorDBErrorHandler;
    private readonly ConnectionErrorHandler _connectionErrorHandler;

    public TextToSqlAgentOrchestrator(
        NormalizePromptTask normalizeTask,
        IntentAnalysisPlugin intentPlugin,
        SchemaScanner schemaScanner,
        SchemaIndexer schemaIndexer,
        SchemaRetriever schemaRetriever,
        SqlGeneratorPlugin sqlPlugin,
        SqlCorrectorPlugin correctorPlugin,
        SqlExecutor sqlExecutor,
        DatabaseConfig dbConfig,
        ILogger<TextToSqlAgentOrchestrator> logger,

        // NEW: Error handlers
        SqlErrorHandler sqlErrorHandler,
        VectorDBErrorHandler vectorDBErrorHandler,
        ConnectionErrorHandler connectionErrorHandler)
    {
        // ... existing assignments ...

        _sqlErrorHandler = sqlErrorHandler;
        _vectorDBErrorHandler = vectorDBErrorHandler;
        _connectionErrorHandler = connectionErrorHandler;
    }
}
```

##### 4b. Enhance Self-Correction Loop

Update `ExecuteWithSelfCorrectionAsync` to use error handlers:

```csharp
private async Task<(SqlExecutionResult Result, int Attempts)> ExecuteWithSelfCorrectionAsync(
    string sql,
    string originalQuestion,
    string schemaContext,
    CancellationToken cancellationToken)
{
    var attempts = 0;
    var maxAttempts = 3;

    while (attempts < maxAttempts)
    {
        attempts++;
        _logger.LogInformation(
            "[Orchestrator] Self-correction attempt {Attempt}/{Max}",
            attempts,
            maxAttempts);

        // Execute SQL
        var result = await _sqlExecutor.ExecuteAsync(sql, cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("[Orchestrator] SQL execution successful");
            return (result, attempts);
        }

        // ===== USE ERROR HANDLER =====
        var sqlError = _sqlErrorHandler.AnalyzeError(result.ErrorMessage ?? "", sql);

        _logger.LogWarning(
            "[Orchestrator] SQL Error: {Type} - {Message}",
            sqlError.Type,
            sqlError.ErrorMessage);

        // Check if error is recoverable
        if (!sqlError.IsRecoverable)
        {
            _logger.LogError(
                "[Orchestrator] Error is not recoverable: {ErrorCode}",
                sqlError.ErrorCode);

            return (result, attempts);
        }

        if (attempts >= maxAttempts)
        {
            _logger.LogError(
                "[Orchestrator] Max correction attempts reached");
            return (result, attempts);
        }

        // ===== SELF-CORRECTION =====
        _logger.LogInformation(
            "[Orchestrator] Attempting to correct SQL. Suggestion: {Fix}",
            sqlError.SuggestedFix);

        // Call SQL Corrector Plugin
        var correctionContext = new
        {
            OriginalQuestion = originalQuestion,
            FailedSQL = sql,
            ErrorMessage = result.ErrorMessage,
            ErrorType = sqlError.Type.ToString(),
            InvalidElement = sqlError.InvalidElement,
            SuggestedFix = sqlError.SuggestedFix,
            SchemaContext = schemaContext
        };

        sql = await _correctorPlugin.CorrectSqlAsync(
            correctionContext,
            cancellationToken);

        _logger.LogInformation(
            "[Orchestrator] Generated corrected SQL (attempt {Attempt})",
            attempts);
    }

    // Failed after max attempts
    return (new SqlExecutionResult
    {
        Success = false,
        ErrorMessage = "Failed to execute SQL after maximum correction attempts"
    }, attempts);
}
```

##### 4c. Handle Vector DB Errors

Update `EnsureSchemaIndexedAsync` to use VectorDBErrorHandler:

```csharp
private async Task EnsureSchemaIndexedAsync(CancellationToken cancellationToken)
{
    try
    {
        await _vectorDBErrorHandler.HandleVectorDBErrorAsync(
            async () =>
            {
                if (_schemaCache == null)
                {
                    _logger.LogInformation("[Orchestrator] Scanning schema...");
                    _schemaCache = await _schemaScanner.ScanSchemaAsync(cancellationToken);

                    _logger.LogInformation("[Orchestrator] Indexing schema...");
                    await _schemaIndexer.IndexSchemaAsync(_schemaCache, cancellationToken);
                }
                return true;
            },
            new Exception("Pre-check"),
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(
            ex,
            "[Orchestrator] Vector DB error. Falling back to full schema.");

        // Fallback: Use full schema without RAG
        if (_schemaCache == null)
        {
            _schemaCache = await _schemaScanner.ScanSchemaAsync(cancellationToken);
        }
    }
}
```

##### 4d. Add Status Monitoring Methods

```csharp
public string GetSystemHealthStatus()
{
    var status = new StringBuilder();

    status.AppendLine("=== System Health ===");
    status.AppendLine($"Circuit Breaker: {_connectionErrorHandler.GetCircuitBreakerStatus()}");
    status.AppendLine($"Rate Limit: {_llmErrorHandler?.GetRateLimitStatus() ?? "N/A"}");
    status.AppendLine($"Schema Cached: {_schemaCache != null}");

    return status.ToString();
}
```

#### 5. Update Dependency Injection in Program.cs

**File:** `TextToSqlAgent.Console/Program.cs`

Add after existing service registrations:

```csharp
// ===== ERROR HANDLING =====
services.AddErrorHandlers();

// ===== UPDATE EXISTING SERVICES =====
// SqlExecutor now needs error handlers
services.AddSingleton(sp =>
{
    var dbConfig = sp.GetRequiredService<DatabaseConfig>();
    var logger = sp.GetRequiredService<ILogger<SqlExecutor>>();
    var connectionHandler = sp.GetRequiredService<ConnectionErrorHandler>();
    var sqlHandler = sp.GetRequiredService<SqlErrorHandler>();

    return new SqlExecutor(dbConfig, logger, connectionHandler, sqlHandler);
});

// GeminiClient now needs LLM error handler
services.AddSingleton(sp =>
{
    var geminiConfig = sp.GetRequiredService<GeminiConfig>();
    var logger = sp.GetRequiredService<ILogger<GeminiClient>>();
    var llmHandler = sp.GetRequiredService<LLMErrorHandler>();

    return new GeminiClient(geminiConfig, logger, llmHandler);
});
```

#### 6. Test Error Handling

Create integration tests:

**File:** `TextToSqlAgent.Tests.Integration/ErrorHandling/ErrorHandlerTests.cs`

```csharp
public class ErrorHandlerTests
{
    [Fact]
    public async Task ConnectionErrorHandler_ShouldRetryWithBackoff()
    {
        // Test connection retry logic
    }

    [Fact]
    public async Task LLMErrorHandler_ShouldHandleRateLimit()
    {
        // Test rate limit handling
    }

    [Fact]
    public async Task SqlErrorHandler_ShouldAnalyzeInvalidColumn()
    {
        // Test SQL error analysis
    }

    [Fact]
    public async Task VectorDBErrorHandler_ShouldFallbackGracefully()
    {
        // Test Vector DB fallback
    }
}
```

## 🎯 Expected Outcomes

After integration:

1. ✅ **Robust Error Handling**
   - Connection errors → Automatic retry with circuit breaker
   - LLM errors → Smart rate limit handling
   - SQL errors → Detailed analysis for self-correction
   - Vector DB errors → Graceful fallback

2. ✅ **Better Observability**
   - Circuit breaker status monitoring
   - Rate limit status tracking
   - Detailed error logging with codes

3. ✅ **Improved Self-Correction**
   - Better error analysis
   - Structured error information
   - Higher success rate

4. ✅ **Production Ready**
   - Graceful degradation
   - Fail-fast when appropriate
   - No cascading failures

## 🧪 Testing Steps

1. **Test Connection Errors**

   ```
   - Stop database
   - Run query
   - Verify circuit breaker opens
   - Verify retry logic works
   ```

2. **Test LLM Rate Limits**

   ```
   - Trigger rate limit (many requests)
   - Verify wait and retry
   - Verify rate limit status
   ```

3. **Test SQL Errors**

   ```
   - Generate query with invalid column
   - Verify error analysis
   - Verify self-correction triggered
   ```

4. **Test Vector DB Errors**
   ```
   - Stop Qdrant
   - Run query
   - Verify fallback to full schema
   ```

## 📊 Metrics to Track

- Circuit breaker open/close events
- Rate limit hit count
- SQL correction success rate
- Average retry attempts
- Error recovery rate

## 🚀 Next Steps

1. Implement this integration guide
2. Run integration tests
3. Monitor error rates in production
4. Fine-tune retry parameters
5. Add metrics export (Phase 5-6)

## ⚠️ Important Notes

- Error handlers are **optional** (backward compatible)
- Existing code works without handlers (fallback to legacy)
- Handlers improve resilience but are not required
- Add handlers gradually, test each integration
