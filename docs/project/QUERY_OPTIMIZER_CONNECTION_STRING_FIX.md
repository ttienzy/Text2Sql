# Query Optimizer Connection String Decryption Fix

## Vấn Đề

**Error:**
```
System.ArgumentException: Keyword not supported: 'cfdj8j4mzeaza4teqtisx7i3ytzsxpr2vmwbyfi94sebxtslj5ikuhrukrduiknl/et7xiofse6btspkmkln5nibdeivtdwdrae9xn5rm9pm4i53rgrhwdg/anlb/tsvij1+zdiaeuruwzqqupw7lcsrevrbhyaigfd0eeyecf8rz/zyvmbrc9m52bvpegunvwbohbt3gomzrbhfgaddu+0r+ibn0us21c+farchwj7tbw0indqdl1fdaf/h+ay66fvobm7hx2lcgcbvbapzs2ob8bk'.
```

**Nguyên nhân:**
- `QueryOptimizerController` đang lấy `connection.ConnectionString` trực tiếp
- Connection string trong database được mã hóa (encrypted)
- `ExecutionPlanService` nhận encrypted string và cố parse như connection string thật
- SqlConnection không thể parse encrypted data → Error

## Root Cause Analysis

### Connection String Storage
Connection strings được mã hóa trong database để bảo mật:
- Sử dụng ASP.NET Core Data Protection API
- Encrypted với purpose: `"ConnectionString.Protection.v1"`
- Stored as encrypted string trong `Connection.ConnectionString` field

### Other Controllers Làm Đúng
Các controllers khác đều sử dụng `IConnectionEncryptionService`:

```csharp
// ✅ ĐÚNG - AgentController, StreamingAgentController, etc.
private readonly IConnectionEncryptionService _encryptionService;

var connection = await _connectionRepository.GetByIdAsync(connectionId);
var connectionString = _encryptionService.GetConnectionString(connection);
```

### QueryOptimizerController Làm Sai
```csharp
// ❌ SAI - Lấy encrypted string trực tiếp
var connection = await _connectionRepository.GetByIdAsync(request.ConnectionId);
var connectionString = connection.ConnectionString; // ← Encrypted!
```

## Giải Pháp

### Fix Implementation

**1. Inject `IConnectionEncryptionService`:**
```csharp
private readonly IConnectionEncryptionService _encryptionService;

public QueryOptimizerController(
    QueryOptimizerService queryOptimizerService,
    IConnectionRepository connectionRepository,
    IConnectionEncryptionService encryptionService) // ← Added
{
    _queryOptimizerService = queryOptimizerService;
    _connectionRepository = connectionRepository;
    _encryptionService = encryptionService; // ← Added
}
```

**2. Sử dụng `GetConnectionString()` để decrypt:**
```csharp
// ✅ ĐÚNG - Decrypt connection string
var connection = await _connectionRepository.GetByIdAsync(request.ConnectionId);
var connectionString = _encryptionService.GetConnectionString(connection);
```

**3. Apply cho cả 2 endpoints:**
- `/api/query-optimizer/analyze`
- `/api/query-optimizer/analyze-with-plan`

### Files Modified

**TextToSqlAgent.API/Controllers/QueryOptimizerController.cs:**
- Added `using TextToSqlAgent.API.Services;`
- Injected `IConnectionEncryptionService`
- Changed `connection.ConnectionString` → `_encryptionService.GetConnectionString(connection)`
- Applied to both `AnalyzeQuery` and `AnalyzeQueryWithPlan` methods

## How `GetConnectionString()` Works

```csharp
public string GetConnectionString(Connection connection)
{
    // If ConnectionString field is populated (new format)
    if (!string.IsNullOrEmpty(connection.ConnectionString))
    {
        // Decrypt the encrypted connection string
        return DecryptConnectionString(connection.ConnectionString, connection.Id);
    }
    
    // Backward compatibility: Build from individual fields
    var decryptedPassword = DecryptPassword(connection.EncryptedPassword, connection.Id);
    return BuildConnectionString(
        connection.Provider,
        connection.Host,
        connection.Port,
        connection.Database,
        connection.Username,
        decryptedPassword);
}
```

**Features:**
1. ✅ Decrypts encrypted connection strings
2. ✅ Backward compatible với old format (individual fields)
3. ✅ Uses Data Protection API với connection-specific purpose
4. ✅ Secure - không expose encrypted data

## Testing

### Before Fix
```
❌ Error: Keyword not supported: 'cfdj8j4mzeaza4teqtisx7i3ytzsxpr2vmwbyfi94sebxtslj5ikuhrukrduiknl...'
❌ ExecutionPlanService fails to connect
❌ No execution plan data returned
```

### After Fix
```
✅ Connection string decrypted correctly
✅ ExecutionPlanService connects successfully
✅ Execution plan data retrieved
✅ UI displays execution plan
```

### Test Steps
1. Clear Redis cache: `redis-cli FLUSHDB`
2. Toggle "Compare Execution Plans" ON
3. Submit query
4. Verify execution plan displays without errors

## Build Status

✅ Build succeeded with 0 errors
- All warnings are pre-existing
- No new diagnostics introduced

## Related Issues

This fix resolves:
1. ✅ Execution plan service connection errors
2. ✅ "Keyword not supported" errors
3. ✅ Compare Execution Plans feature not working

## Security Notes

**Why Encryption?**
- Connection strings contain sensitive credentials
- Must be encrypted at rest in database
- Data Protection API provides:
  - Key rotation support
  - Purpose-based encryption
  - Secure key storage

**Best Practice:**
- ✅ Always use `IConnectionEncryptionService.GetConnectionString()`
- ❌ Never use `connection.ConnectionString` directly
- ✅ Never log decrypted connection strings
- ✅ Use connection-specific encryption purposes

## Summary

**Problem:** QueryOptimizerController sử dụng encrypted connection string trực tiếp

**Solution:** Inject và sử dụng `IConnectionEncryptionService` để decrypt

**Impact:** 
- Execution plan feature giờ hoạt động đúng
- Connection string được decrypt an toàn
- Consistent với các controllers khác

**Files Changed:** 1 file
- `TextToSqlAgent.API/Controllers/QueryOptimizerController.cs`
