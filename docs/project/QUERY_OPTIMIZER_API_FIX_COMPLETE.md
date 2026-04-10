# Query Optimizer API Fix - Complete

## Issues Fixed

### Issue 1: ConnectionId Type Mismatch ✅
**Error:** 400 Bad Request - "The JSON value could not be converted to System.Int32"

**Root Cause:**
- `OptimizeQueryRequest.ConnectionId` was defined as `int`
- Actual `Connection.Id` in the system is `string` (GUID format)
- Frontend was sending GUID strings, causing JSON deserialization failure

**Solution:**
- Changed `OptimizeQueryRequest.ConnectionId` from `int` to `string`
- Updated both controller endpoints to use string ID directly (removed `.ToString()`)
- Aligned with the actual `Connection` entity structure

**Files Modified:**
- `TextToSqlAgent.API/DTOs/QueryOptimizer/OptimizeQueryRequest.cs`
- `TextToSqlAgent.API/Controllers/QueryOptimizerController.cs`

### Issue 2: Missing Prompt File Path Resolution ✅
**Error:** DirectoryNotFoundException - "Could not find a part of the path 'D:\DACN1\TextToSqlAgent\TextToSqlAgent.API\Prompts\QueryOptimizer\optimize-query.skprompt.txt'"

**Root Cause:**
- `QueryOptimizerService` was using a simple relative path to load prompt template
- The path was relative to the API project's working directory, not the solution root
- Prompt files are located in the solution root `Prompts/` directory

**Solution:**
- Added `ResolvePromptFilePath()` helper method to `QueryOptimizerService`
- Method searches up the directory tree from both `AppDomain.CurrentDomain.BaseDirectory` and `Directory.GetCurrentDirectory()`
- Similar pattern to `PromptTemplateService` used by DbExplorer
- Added proper error handling with logging

**Files Modified:**
- `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`
  - Added `using System.IO;`
  - Added `ResolvePromptFilePath()` method
  - Updated `OptimizeWithLLMAsync()` to use path resolver

### Issue 3: LLM Response JSON Parsing Error ✅
**Error:** JsonException - "'`' is an invalid start of a value"

**Root Cause:**
- LLM was returning JSON wrapped in markdown code blocks (```json ... ```)
- Direct JSON deserialization failed because of the markdown formatting
- This is a common LLM behavior where responses are formatted for readability

**Solution:**
- Added `CleanJsonResponse()` helper method to strip markdown code blocks
- Handles both ```json and ``` prefixes
- Removes trailing ``` markers
- Added debug logging to see cleaned response
- Pattern consistent with other services in the codebase

**Files Modified:**
- `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`
  - Added `CleanJsonResponse()` method
  - Updated `OptimizeWithLLMAsync()` to clean response before parsing
  - Added debug logging for troubleshooting

## Testing

### Test Endpoints
```http
POST https://localhost:7189/api/query-optimizer/analyze
Content-Type: application/json
Authorization: Bearer {{token}}

{
  "sql": "SELECT * FROM Users WHERE Id = 1",
  "connectionId": "your-connection-guid-here",
  "includeExecutionPlan": false
}
```

### Expected Behavior
1. ✅ API accepts GUID string for `connectionId`
2. ✅ Prompt template file is found and loaded correctly
3. ✅ LLM response with markdown code blocks is parsed successfully
4. ✅ Query optimization proceeds without errors

## Build Status
✅ Build succeeded with 0 errors
- All warnings are pre-existing (nullability, obsolete APIs)
- No new diagnostics introduced

## Related Files
- Test file: `test-query-optimizer-fix.http`
- Prompt template: `Prompts/QueryOptimizer/optimize-query.skprompt.txt`

## Summary
All three API issues have been fixed:
1. Connection ID type mismatch resolved
2. Prompt file path resolution implemented
3. JSON parsing with markdown code block handling added

The Query Optimizer endpoints should now work correctly end-to-end.
