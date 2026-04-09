# Critical Bugs - Phase 2 Fix Plan

**Created**: 2026-04-08  
**Status**: Ready to Implement  
**Priority**: URGENT - Production blockers discovered after Phase 1

---

## Summary

Phát hiện 3 vấn đề nghiêm trọng mới sau khi review Phase 1:
- 1 Critical issue (blocking Write/DDL pipelines)
- 2 Serious issues (performance + cache contamination)

---

## 🔴 NEW-1: ValidateSql() Blocks Write/DDL Pipelines (CRITICAL)

### Root Cause Analysis

**Location**: `TextToSqlAgent.Plugins/SqlGeneratorPlugin.cs` (line 146-180)

**Problem**:
```csharp
public bool ValidateSql(string sql)
{
    var dangerousKeywords = new[]
    {
        "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE",
        "INSERT", "UPDATE", "EXEC", "EXECUTE", "SP_",
        "XP_", "GRANT", "REVOKE", "SHUTDOWN"
    };
    
    foreach (var keyword in dangerousKeywords)
    {
        if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
        {
            return false; // ← BLOCKS INSERT, UPDATE, CREATE, ALTER!
        }
    }
    
    // Must contain SELECT
    if (!upperSql.Contains("SELECT"))
    {
        return false; // ← BLOCKS non-SELECT queries!
    }
}
```

**Impact**:
- Write pipeline cần generate `INSERT`/`UPDATE` → bị reject
- DDL pipeline cần generate `CREATE`/`ALTER` → bị reject
- Nếu bất kỳ flow nào đi qua `ProcessQueryAsync` (có thể xảy ra), SQL bị chặn ở Step 7
- User không thể thực hiện write operations

**Call Chain**:
```
ProcessQueryAsync (EnhancedAgentOrchestrator)
  → Step 7: Validate SQL
    → queryValidator.ValidateQuery(sql)
      → SqlGeneratorPlugin.ValidateSql(sql)
        → return false for INSERT/UPDATE/CREATE/ALTER
```

### Solution Design

**Option 1: Context-Aware Validation (RECOMMENDED)**
```csharp
public bool ValidateSql(string sql, PipelineType pipelineType = PipelineType.Query)
{
    var upperSql = sql.ToUpper();
    
    // Different validation rules per pipeline
    switch (pipelineType)
    {
        case PipelineType.Query:
            // Strict: Only SELECT allowed
            return ValidateQuerySql(upperSql);
            
        case PipelineType.Write:
            // Allow INSERT, UPDATE (but not DELETE, DROP)
            return ValidateWriteSql(upperSql);
            
        case PipelineType.Ddl:
            // Allow CREATE, ALTER (but not DROP)
            return ValidateDdlSql(upperSql);
            
        case PipelineType.Forbidden:
            // Always reject
            return false;
            
        default:
            return ValidateQuerySql(upperSql);
    }
}

private bool ValidateQuerySql(string sql)
{
    // Current logic - only SELECT
    var dangerousKeywords = new[] { "DROP", "DELETE", "TRUNCATE", "ALTER", 
                                     "CREATE", "INSERT", "UPDATE", "EXEC" };
    foreach (var keyword in dangerousKeywords)
    {
        if (Regex.IsMatch(sql, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            return false;
    }
    return sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase);
}

private bool ValidateWriteSql(string sql)
{
    // Allow INSERT, UPDATE but not DELETE, DROP, TRUNCATE
    var forbiddenKeywords = new[] { "DROP", "TRUNCATE", "EXEC", "EXECUTE", 
                                     "SP_", "XP_", "GRANT", "REVOKE", "SHUTDOWN" };
    foreach (var keyword in forbiddenKeywords)
    {
        if (Regex.IsMatch(sql, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            return false;
    }
    
    // Must contain INSERT or UPDATE
    return sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
           sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase);
}

private bool ValidateDdlSql(string sql)
{
    // Allow CREATE, ALTER but not DROP
    var forbiddenKeywords = new[] { "DROP", "TRUNCATE", "DELETE", "EXEC", 
                                     "EXECUTE", "SP_", "XP_", "GRANT", "REVOKE" };
    foreach (var keyword in forbiddenKeywords)
    {
        if (Regex.IsMatch(sql, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            return false;
    }
    
    // Must contain CREATE or ALTER
    return sql.Contains("CREATE", StringComparison.OrdinalIgnoreCase) ||
           sql.Contains("ALTER", StringComparison.OrdinalIgnoreCase);
}
```

**Option 2: Skip Validation for Non-Query Pipelines (SIMPLER)**
```csharp
// In ProcessQueryAsync
if (intentResult?.Route == PipelineRoute.Query)
{
    // Only validate for Query pipeline
    var isValid = queryValidator.ValidateQuery(sql);
    if (!isValid)
    {
        // ... error handling
    }
}
// Skip validation for Write/DDL pipelines - they have their own validation
```

### Implementation Plan

**Priority**: P0 - CRITICAL

**Steps**:
1. ✅ Add `PipelineType` parameter to `ValidateSql()`
2. ✅ Implement separate validation methods per pipeline type
3. ✅ Update all callers to pass pipeline type
4. ✅ Add unit tests for each validation method
5. ✅ Integration test: Write pipeline generates INSERT successfully
6. ✅ Integration test: DDL pipeline generates CREATE successfully

**Files to Change**:
- `TextToSqlAgent.Plugins/SqlGeneratorPlugin.cs` (ValidateSql method)
- `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs` (pass pipeline type)
- `TextToSqlAgent.Core/Enums/PipelineType.cs` (if not exists, create)

**Testing**:
- [ ] Unit test: ValidateQuerySql rejects INSERT/UPDATE
- [ ] Unit test: ValidateWriteSql accepts INSERT/UPDATE
- [ ] Unit test: ValidateDdlSql accepts CREATE/ALTER
- [ ] Integration: Write pipeline end-to-end
- [ ] Integration: DDL pipeline end-to-end

**Estimated Time**: 2 hours

---

## 🟠 NEW-2: _schemaIndexed Reset Every Request (SERIOUS)

### Root Cause Analysis

**Location**: `TextToSqlAgent.API/Program.cs` (line 323)

**Problem**:
```csharp
// Program.cs
builder.Services.AddScoped<EnhancedAgentOrchestrator>(); // ← Scoped = new instance per request

// EnhancedAgentOrchestrator.cs
private bool _schemaIndexed = false; // ← Instance field
private DatabaseSchema? _cachedSchema; // ← Also reset per request!

private async Task EnsureSchemaLoadedAsync(List<string> steps, CancellationToken ct)
{
    if (!_schemaIndexed)
    {
        await TryEnsureSchemaIndexedAsync(_cachedSchema, ct); // ← Called EVERY request!
        _schemaIndexed = true; // ← Reset to false on next request
    }
}
```

**Impact**:
- `EnhancedAgentOrchestrator` is Scoped → new instance per request
- `_schemaIndexed = false` every request → calls `GetPointCountAsync()` to check Qdrant
- With 100 concurrent users = 100 Qdrant API calls just to check "already indexed?"
- `_cachedSchema` also reset → duplicate schema scans (despite SemaphoreSlim fix in Phase 1)

**Performance Impact**:
- 100 concurrent requests = 100 Qdrant calls (~50-100ms each)
- Total overhead: 5-10 seconds of unnecessary Qdrant calls
- Network bandwidth waste
- Qdrant rate limiting risk

### Solution Design

**Option 1: Make EnhancedAgentOrchestrator Singleton (RECOMMENDED)**
```csharp
// Program.cs
builder.Services.AddSingleton<EnhancedAgentOrchestrator>();
```

**Pros**:
- Simple one-line change
- `_schemaIndexed` and `_cachedSchema` persist across requests
- No duplicate Qdrant checks
- Already thread-safe (SemaphoreSlim added in Phase 1)

**Cons**:
- Must verify all dependencies are Singleton-compatible
- Need to audit for any per-request state

**Option 2: Static Shared State (ALTERNATIVE)**
```csharp
// EnhancedAgentOrchestrator.cs
private static bool _schemaIndexed = false; // ← Static
private static DatabaseSchema? _cachedSchema; // ← Static
private static readonly SemaphoreSlim _schemaScanLock = new(1, 1); // ← Already static-safe
```

**Pros**:
- Keeps Scoped registration
- Shared state across all instances

**Cons**:
- Static state is harder to test
- Potential issues with multiple databases

**Option 3: External Cache Service (OVER-ENGINEERED)**
- Use Redis/Memory cache for `_schemaIndexed` flag
- Not recommended - adds complexity

### Implementation Plan

**Priority**: P1 - SERIOUS (Performance)

**Steps**:
1. ✅ Audit `EnhancedAgentOrchestrator` dependencies
2. ✅ Verify no per-request state (except via AsyncLocal)
3. ✅ Change registration from Scoped to Singleton
4. ✅ Add logging to track schema index checks
5. ✅ Load test: Verify only 1 Qdrant check per app lifetime

**Files to Change**:
- `TextToSqlAgent.API/Program.cs` (DI registration)

**Dependency Audit**:
```csharp
public EnhancedAgentOrchestrator(
    IAgentServiceFactory serviceFactory,      // ← Check: Singleton?
    AgentConfig agentConfig,                  // ← Singleton ✓
    DatabaseConfig dbConfig,                  // ← Singleton ✓ (with AsyncLocal)
    ILogger<EnhancedAgentOrchestrator> logger,// ← Singleton ✓
    PipelineResponseBuilder responseBuilder,  // ← Singleton ✓
    IIntentClassifier? intentClassifier,      // ← Scoped ✗
    IWritePipeline? writePipeline,            // ← Scoped ✗
    IDDLPipeline? ddlPipeline,                // ← Scoped ✗
    IForbiddenPipeline? forbiddenPipeline,    // ← Scoped ✗
    ISchemaCache? schemaCache,                // ← Scoped ✗
    IQueryResultCache? queryResultCache)      // ← Singleton ✓
```

**Issue**: Some dependencies are Scoped! Cannot make Singleton directly.

**Revised Solution**: Use `IServiceProvider` to resolve Scoped dependencies on-demand
```csharp
public EnhancedAgentOrchestrator(
    IServiceProvider serviceProvider, // ← Inject service provider
    AgentConfig agentConfig,
    DatabaseConfig dbConfig,
    ILogger<EnhancedAgentOrchestrator> logger,
    PipelineResponseBuilder responseBuilder)
{
    _serviceProvider = serviceProvider;
    // ... other singletons
}

// Resolve Scoped dependencies on-demand
private IIntentClassifier GetIntentClassifier()
{
    return _serviceProvider.CreateScope().ServiceProvider
        .GetRequiredService<IIntentClassifier>();
}
```

**Wait, this is getting complex. Better solution:**

**REVISED Option 1: Static Cache for Schema State Only**
```csharp
// Only make schema-related state static
private static bool _globalSchemaIndexed = false;
private static DatabaseSchema? _globalCachedSchema;
private static readonly SemaphoreSlim _globalSchemaScanLock = new(1, 1);

// Keep instance as Scoped, but use static cache
private async Task EnsureSchemaLoadedAsync(...)
{
    if (_globalCachedSchema != null)
    {
        steps.Add("Step 1.1: Use global cached schema");
        return;
    }
    
    await _globalSchemaScanLock.WaitAsync(ct);
    try
    {
        if (_globalCachedSchema != null) return;
        
        var schemaScanner = _serviceFactory.GetSchemaScanner();
        _globalCachedSchema = await schemaScanner.ScanAsync(ct);
        
        if (!_globalSchemaIndexed)
        {
            await TryEnsureSchemaIndexedAsync(_globalCachedSchema, ct);
            _globalSchemaIndexed = true;
        }
    }
    finally
    {
        _globalSchemaScanLock.Release();
    }
}
```

**This is the best solution**: Keep Scoped registration, only make schema cache static.

**Testing**:
- [ ] Load test: 100 concurrent requests
- [ ] Verify only 1 Qdrant GetPointCountAsync call
- [ ] Verify only 1 schema scan
- [ ] Monitor memory usage (static cache)

**Estimated Time**: 1 hour

---

## 🟠 NEW-3: Intent Cache Key Missing Context (SERIOUS)

### Root Cause Analysis

**Location**: `TextToSqlAgent.Application/Routing/IntentClassifier.cs` (line 224, 261, 280)

**Problem**:
```csharp
// Cache key = ONLY question text
var cachedResult = await _cacheService.GetCachedAsync(question, ct);

await _cacheService.CacheAsync(question, ruleResult, ct);
```

**Impact - Cross-Connection Cache Contamination**:

**Scenario 1: Different Databases**
```
User A (DB: ecommerce, tables: Orders, Products, Customers)
  Question: "liệt kê tất cả"
  → Intent: Query, confidence 0.80, entities: ["Orders"]
  → Cached with key: "liệt kê tất cả"

User B (DB: hr_system, tables: Employees, Departments)
  Question: "liệt kê tất cả"
  → Gets cached result from User A
  → Intent: Query, entities: ["Orders"] ← WRONG! No Orders table in HR DB
  → Routing decision based on wrong context
```

**Scenario 2: Same Question, Different Context**
```
User A: "show me the latest"
  Context: Recent conversation about orders
  → Intent: Query, target: Orders table
  → Cached

User B: "show me the latest"
  Context: Recent conversation about employees
  → Gets cached result
  → Intent: Query, target: Orders table ← WRONG CONTEXT!
```

**Why This is Serious**:
- Intent classification drives routing decisions
- Wrong routing → wrong pipeline → wrong SQL generation
- User sees results from wrong table/database
- Potential data leak if permissions not checked properly

### Solution Design

**Cache Key Must Include**:
1. Question text (current)
2. Database context hash (NEW)
3. Conversation context hash (optional, for better accuracy)

**Implementation**:
```csharp
private string GenerateCacheKey(
    string question, 
    string databaseContext, 
    string? conversationContext = null)
{
    // Create deterministic hash from contexts
    var contextString = $"{question}|{databaseContext}";
    
    if (!string.IsNullOrEmpty(conversationContext))
    {
        contextString += $"|{conversationContext}";
    }
    
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(contextString));
    var hash = Convert.ToBase64String(hashBytes);
    
    return $"intent:{hash}";
}

public async Task<IntentClassificationResult> ClassifyAsync(
    string question,
    string conversationContext,
    string databaseContext,
    CancellationToken ct = default)
{
    // Generate context-aware cache key
    var cacheKey = GenerateCacheKey(question, databaseContext, conversationContext);
    
    if (_cacheService != null)
    {
        var cachedResult = await _cacheService.GetCachedAsync(cacheKey, ct);
        if (cachedResult != null)
        {
            _logger.LogInformation("[IntentClassifier] Cache hit (context-aware)");
            return cachedResult;
        }
    }
    
    // ... classification logic ...
    
    // Cache with context-aware key
    if (_cacheService != null && result.Confidence >= 0.75)
    {
        await _cacheService.CacheAsync(cacheKey, result, ct);
    }
    
    return result;
}
```

**Alternative: Include ConnectionId in Cache Key**
```csharp
// Simpler approach - use connectionId as part of key
var cacheKey = $"intent:{connectionId}:{question}";
```

**Pros**:
- Simpler implementation
- Guarantees isolation per connection

**Cons**:
- Less cache hits (same question on same DB type won't hit cache)
- Wastes cache space

**RECOMMENDED: Hybrid Approach**
```csharp
// Use hash of database schema (table names) as context
var schemaHash = ComputeSchemaHash(databaseContext);
var cacheKey = $"intent:{schemaHash}:{question}";

private string ComputeSchemaHash(string databaseContext)
{
    // Extract table names, sort, hash
    var tables = ExtractTableNames(databaseContext);
    var sortedTables = string.Join(",", tables.OrderBy(t => t));
    
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sortedTables));
    return Convert.ToBase64String(hashBytes).Substring(0, 16); // Short hash
}
```

**Benefits**:
- Same schema → same hash → cache hit
- Different schema → different hash → no contamination
- Multiple connections to same DB type → cache reuse

### Implementation Plan

**Priority**: P1 - SERIOUS (Cache Contamination)

**Steps**:
1. ✅ Add `GenerateCacheKey()` method with schema hash
2. ✅ Update `ClassifyAsync()` to use context-aware key
3. ✅ Update `IIntentCacheService` interface if needed
4. ✅ Add unit tests for cache key generation
5. ✅ Integration test: Verify cache isolation per schema

**Files to Change**:
- `TextToSqlAgent.Application/Routing/IntentClassifier.cs`
- `TextToSqlAgent.Infrastructure/Caching/IntentCache.cs` (if interface changes)

**Testing**:
- [ ] Unit test: Same question, different schema → different cache keys
- [ ] Unit test: Same question, same schema → same cache key
- [ ] Integration: User A and B with different DBs → no cache contamination
- [ ] Performance: Verify cache hit rate doesn't drop significantly

**Estimated Time**: 2 hours

---

## Summary Table

| Issue | Priority | Impact | Estimated Time | Status |
|-------|----------|--------|----------------|--------|
| NEW-1: ValidateSql blocks Write/DDL | 🔴 P0 | Blocks write operations | 2 hours | Ready |
| NEW-2: Schema state reset per request | 🟠 P1 | Performance (100x Qdrant calls) | 1 hour | Ready |
| NEW-3: Intent cache contamination | 🟠 P1 | Wrong routing, data leak risk | 2 hours | Ready |

**Total Estimated Time**: 5 hours

---

## Implementation Order

### Phase 2A - Critical (Today)
1. **NEW-1**: Fix ValidateSql (2 hours)
   - Blocks production functionality
   - Must fix before Write/DDL pipelines can work

### Phase 2B - Serious (This Week)
2. **NEW-2**: Fix schema state (1 hour)
   - Performance issue
   - Easy fix (static fields)

3. **NEW-3**: Fix intent cache (2 hours)
   - Cache contamination
   - Requires careful testing

---

## Risk Assessment

### NEW-1 (ValidateSql)
- **Risk**: High - Blocks core functionality
- **Mitigation**: Comprehensive testing of all pipeline types
- **Rollback**: Easy - revert validation logic

### NEW-2 (Schema State)
- **Risk**: Medium - Static state in multi-tenant scenario
- **Mitigation**: Ensure schema is connection-specific, not global
- **Rollback**: Easy - revert to Scoped registration

### NEW-3 (Intent Cache)
- **Risk**: Medium - Cache key changes may reduce hit rate
- **Mitigation**: Monitor cache hit rate before/after
- **Rollback**: Easy - revert to simple key

---

## Testing Strategy

### Unit Tests
- [ ] ValidateSql for each pipeline type
- [ ] Cache key generation with different contexts
- [ ] Schema state persistence across requests

### Integration Tests
- [ ] Write pipeline end-to-end with INSERT
- [ ] DDL pipeline end-to-end with CREATE
- [ ] Cache isolation between different connections
- [ ] Schema indexing only once per app lifetime

### Load Tests
- [ ] 100 concurrent requests → verify 1 Qdrant check
- [ ] Cache hit rate monitoring
- [ ] No cross-connection contamination

---

## Notes

- All Phase 2 issues discovered through careful code review after Phase 1
- These are design flaws, not race conditions
- Fixes are relatively simple but require careful testing
- No breaking changes expected
- Backward compatible

---

**Next Steps**: Begin implementation of NEW-1 (ValidateSql) immediately.
