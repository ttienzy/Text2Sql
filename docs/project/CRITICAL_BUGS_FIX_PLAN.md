# Critical Bugs Fix Plan

**Created**: 2026-04-08  
**Status**: In Progress  
**Priority**: URGENT - Production blockers

## Summary

Phát hiện 10 vấn đề nghiêm trọng trong codebase, bao gồm:
- 4 Critical issues (có thể gây data leak, race conditions)
- 3 Serious issues (ảnh hưởng correctness, security)
- 3 Design issues (ảnh hưởng maintainability)

---

## 🔴 Critical Issues (Phải sửa ngay)

### #2: DatabaseConfig Mutation in Singleton (MOST CRITICAL)
**File**: `TextToSqlAgent.API/Controllers/StreamingAgentController.cs` (line 141-142)  
**Root Cause**: `DatabaseConfig` được đăng ký là Singleton trong `Program.cs` (line 177), nhưng `StreamingAgentController` đang mutate shared state:

```csharp
var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
var originalConnectionString = dbConfig.ConnectionString;
dbConfig.ConnectionString = connectionString; // ← MUTATING SINGLETON!
// ... process request ...
finally {
    dbConfig.ConnectionString = originalConnectionString; // ← RACE CONDITION!
}
```

**Impact**: 
- Request của user A có thể execute SQL vào database của user B
- Data leak nghiêm trọng
- Security vulnerability

**Solution**:
1. Tạo `DatabaseConfig` mới cho mỗi request (scoped instance)
2. HOẶC: Pass connectionString trực tiếp vào các service methods thay vì mutate config
3. HOẶC: Sử dụng `AsyncLocal<DatabaseConfig>` để isolate per-request

**Priority**: P0 - Fix immediately

---

### #4: Fire-and-Forget SQL Token Streaming
**File**: `TextToSqlAgent.API/Controllers/StreamingAgentController.cs` (line 168-177)

```csharp
Action<string> sqlTokenCallback = (token) =>
{
    _ = Task.Run(async () =>
    {
        await WriteSseEventAsync("sql_token", new { token }, ct);
    }, ct);
};
```

**Root Cause**: Mỗi SQL token tạo một `Task.Run` mới, không có:
- Backpressure control
- Ordering guarantee
- Error handling

**Impact**:
- Với 200+ tokens từ LLM, tạo 200 concurrent tasks ghi vào cùng response stream
- Race condition → tokens về sai thứ tự trên frontend
- Response.WriteAsync không thread-safe khi gọi concurrent

**Solution**:
1. Sử dụng `Channel<string>` để buffer tokens
2. Single consumer task đọc từ channel và ghi tuần tự
3. Hoặc: Sử dụng `SemaphoreSlim(1,1)` để serialize writes

**Priority**: P0 - Fix immediately

---

### #1: Race Condition on _cachedSchema
**File**: `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs` (line 919-933)

```csharp
private DatabaseSchema? _cachedSchema;

private async Task EnsureSchemaLoadedAsync(List<string> steps, CancellationToken ct)
{
    if (_cachedSchema != null) { return; } // ← NOT THREAD-SAFE
    
    _cachedSchema = await schemaScanner.ScanAsync(ct); // ← RACE CONDITION
}
```

**Root Cause**: `_cachedSchema` là instance field, không có lock. Nếu 2 requests đến đồng thời:
- Cả 2 thấy `_cachedSchema == null`
- Cả 2 gọi `ScanAsync()` → scan 2 lần
- Có thể ghi đè nhau

**Impact**:
- Duplicate schema scans (performance)
- Potential data corruption nếu scan results khác nhau

**Solution**:
```csharp
private readonly SemaphoreSlim _schemaScanLock = new(1, 1);

private async Task EnsureSchemaLoadedAsync(...)
{
    if (_cachedSchema != null) return;
    
    await _schemaScanLock.WaitAsync(ct);
    try
    {
        if (_cachedSchema != null) return; // Double-check
        _cachedSchema = await schemaScanner.ScanAsync(ct);
    }
    finally
    {
        _schemaScanLock.Release();
    }
}
```

**Priority**: P0 - Fix immediately

---

### #3: Progress<T> Callback on Wrong Thread
**File**: `TextToSqlAgent.API/Controllers/StreamingAgentController.cs` (line 159-167)

```csharp
var progress = new Progress<AgentStageEvent>(async stageEvent =>
{
    await WriteSseEventAsync("stage_update", stageEvent, ct);
});
```

**Root Cause**: 
- `Progress<T>` captures `SynchronizationContext` lúc khởi tạo
- ASP.NET Core không có `SynchronizationContext` → callback chạy trên threadpool
- `Response.WriteAsync` không thread-safe khi gọi concurrent

**Impact**:
- Race condition khi multiple stage updates ghi vào response stream
- SSE events có thể bị corrupt hoặc sai thứ tự

**Solution**:
1. Sử dụng `Channel<AgentStageEvent>` thay vì `Progress<T>`
2. Single consumer task đọc từ channel và ghi tuần tự
3. Hoặc: Wrap `WriteSseEventAsync` với `SemaphoreSlim(1,1)`

**Priority**: P1 - Fix soon

---

## 🟠 Serious Issues (Ảnh hưởng correctness)

### #5: Double/Triple Intent Classification
**Files**: 
- `EnhancedAgentOrchestrator.ProcessQueryAsync` (line ~150)
- `EnhancedAgentOrchestrator.ProcessMessageWithIntentRoutingAsync` (line 1580)
- `EnhancedAgentOrchestrator.RouteToQueryPipelineAsync` → calls `ProcessQueryAsync` again

**Root Cause**: 
```
ProcessMessageWithIntentRoutingAsync
  → ClassifyAsync (lần 1)
  → RouteToQueryPipelineAsync
    → ProcessQueryAsync
      → ClassifyAsync (lần 2) với databaseContext khác
```

**Impact**:
- Một câu hỏi được classify 2-3 lần
- Mỗi lần với context khác nhau → kết quả không nhất quán
- Performance overhead (LLM calls tốn tiền)

**Solution**:
1. Xóa intent classification trong `ProcessQueryAsync` (legacy code)
2. Chỉ classify 1 lần trong `ProcessMessageWithIntentRoutingAsync`
3. Pass `IntentResult` xuống các pipeline methods

**Priority**: P1 - Fix soon

---

### #6: BuildDatabaseContextAsync Dùng Sai schemaCache
**File**: `EnhancedAgentOrchestrator.cs` (line 1676-1690)

```csharp
private async Task<string> BuildDatabaseContextAsync(string connectionId, CancellationToken ct)
{
    var schema = await EnsureSchemaLoadedAsync(connectionId, ct);
}

private async Task<DatabaseSchema?> EnsureSchemaLoadedAsync(string connectionId, CancellationToken ct)
{
    var schema = await _schemaCache?.GetAsync(connectionId, ct)!; // ← null-forgiving operator
}
```

**Root Cause**: 
- `_schemaCache` là nullable (`ISchemaCache?`)
- Dùng `!` để suppress warning thay vì handle properly
- Nếu `_schemaCache` là null → `NullReferenceException`
- Exception bị swallow bởi `catch (Exception ex)` → trả về `string.Empty`
- Intent classification chạy không có context → routing sai

**Impact**:
- Silent failures
- Incorrect routing decisions
- Hard to debug

**Solution**:
```csharp
if (_schemaCache == null)
{
    _logger.LogWarning("[Schema] Schema cache not available");
    return null;
}

var schema = await _schemaCache.GetAsync(connectionId, ct);
```

**Priority**: P1 - Fix soon

---

### #7: Schema Fingerprint Dùng Guid.NewGuid()
**File**: `EnhancedAgentOrchestrator.cs` (line 1504-1515)

```csharp
private static SchemaFingerprint CreateSimpleFingerprint(DatabaseSchema schema)
{
    return new SchemaFingerprint
    {
        Hash = Guid.NewGuid().ToString(), // ← Random mỗi lần!
    };
}
```

**Root Cause**: 
- Fingerprint dùng để detect schema changes
- Hash là random → mỗi lần restart app đều re-index toàn bộ schema vào Qdrant
- Với 163 documents mất vài phút

**Impact**:
- Slow startup time
- Unnecessary Qdrant writes
- Performance degradation

**Solution**:
```csharp
private static SchemaFingerprint CreateSimpleFingerprint(DatabaseSchema schema)
{
    // Create deterministic hash from schema content
    var content = string.Join("|", 
        schema.Tables.OrderBy(t => t.TableName)
            .Select(t => $"{t.TableName}:{string.Join(",", t.Columns.Select(c => c.ColumnName))}"));
    
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
    var hash = Convert.ToBase64String(hashBytes);
    
    return new SchemaFingerprint { Hash = hash, ... };
}
```

**Priority**: P2 - Fix when possible

---

### #8: StackTrace Exposed in Error Response
**File**: `PipelineResponseBuilder.cs` (location TBD)

```csharp
Error = new ErrorDetails
{
    Code = "INTERNAL_ERROR",
    Message = exception.Message,
    StackTrace = exception.StackTrace, // ← Trả về client!
}
```

**Root Cause**: Stack trace chứa internal structure info (namespace, file path, line number)

**Impact**: Security vulnerability - information disclosure

**Solution**:
```csharp
Error = new ErrorDetails
{
    Code = "INTERNAL_ERROR",
    Message = exception.Message,
    StackTrace = _environment.IsDevelopment() ? exception.StackTrace : null
}
```

**Priority**: P1 - Fix soon (security)

---

## 🟡 Design Issues (Ảnh hưởng maintainability)

### #9: PipelineOrchestrator Parallel GROUP A Corrupt Shared Context
**File**: `PipelineOrchestrator.cs` (location TBD)

```csharp
var groupATasks = new List<Task<StageResult>>();
groupATasks.Add(ExecuteStageAsync(intentStage, context, ct));
groupATasks.Add(ExecuteStageAsync(validationStage, context, ct));
var groupAResults = await Task.WhenAll(groupATasks);
```

**Root Cause**: 
- Cả 2 stages nhận cùng `context` object
- Có thể ghi vào `context.Steps`, `context.Response`, `context.Schema` đồng thời
- Không có synchronization

**Impact**: Data corruption tiềm ẩn

**Solution**:
1. Clone context cho mỗi stage
2. Hoặc: Make context immutable, stages return new context
3. Hoặc: Don't run these stages in parallel (they may have dependencies)

**Priority**: P2 - Refactor when possible

---

### #10: Hai EnsureSchemaLoadedAsync Khác Nhau Cùng Tên
**File**: `EnhancedAgentOrchestrator.cs`

```csharp
// Overload 1: Dùng List<string> steps (legacy)
private async Task EnsureSchemaLoadedAsync(List<string> steps, CancellationToken ct)

// Overload 2: Dùng connectionId + ISchemaCache (new)
private async Task<DatabaseSchema?> EnsureSchemaLoadedAsync(string connectionId, CancellationToken ct)
```

**Root Cause**: 
- Hai methods cùng tên, logic hoàn toàn khác nhau
- Dùng storage khác nhau (`_cachedSchema` field vs `ISchemaCache`)

**Impact**: 
- Confusing code
- Inconsistent cache state
- Hard to maintain

**Solution**: Rename one of them:
- `EnsureSchemaLoadedAsync` (new, with ISchemaCache)
- `EnsureLegacySchemaLoadedAsync` (old, with _cachedSchema field)

**Priority**: P3 - Refactor when convenient

---

## Fix Order

1. **P0 - Immediate** (Today):
   - #2: DatabaseConfig mutation (data leak)
   - #4: Fire-and-forget SQL tokens (race condition)
   - #1: _cachedSchema race condition

2. **P1 - This Week**:
   - #3: Progress<T> threading issue
   - #5: Double intent classification
   - #6: schemaCache null handling
   - #8: StackTrace exposure (security)

3. **P2 - Next Sprint**:
   - #7: Random fingerprint
   - #9: Parallel context corruption

4. **P3 - Backlog**:
   - #10: Rename duplicate methods

---

## Testing Plan

After each fix:
1. Unit tests for the specific issue
2. Integration tests with concurrent requests
3. Load testing to verify no race conditions
4. Security audit for #8

---

## Notes

- All Critical issues involve concurrency/threading
- Root cause: Services registered as Singleton/Scoped but have mutable state
- Need to review DI registration strategy
- Consider making more services Scoped instead of Singleton
