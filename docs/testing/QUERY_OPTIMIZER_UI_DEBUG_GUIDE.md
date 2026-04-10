# Query Optimizer UI Debug Guide

## Issue
API returns 200 OK but UI doesn't display the optimized query.

## Backend Status
✅ API working correctly
- Returns 200 OK in ~66ms
- Cache hit (returning cached optimization result)
- No errors in backend logs

## Frontend Debug Steps

### 1. Check Browser Console
Open browser DevTools (F12) and check:

```javascript
// Look for these debug logs in console:
[OptimizedSqlViewer] result: {...}
[OptimizedSqlViewer] optimizedSql: "..."
```

### 2. Check Network Tab
1. Open DevTools → Network tab
2. Filter for "analyze"
3. Click on the request
4. Check "Response" tab
5. Verify the response structure:

```json
{
  "originalSql": "SELECT * FROM ...",
  "optimizedSql": "SELECT ... FROM ...",  // ← Should have value
  "isChanged": true,
  "severity": "ok",
  "detectedIssues": [...],
  "explanation": "...",
  ...
}
```

### 3. Common Issues

#### Issue A: optimizedSql is null or empty
**Symptom:** Response has `"optimizedSql": ""` or `"optimizedSql": null`

**Cause:** Cached response from before the JSON parsing fix

**Solution:** Clear the cache
```bash
# Option 1: Clear Redis cache
redis-cli FLUSHDB

# Option 2: Restart Redis
docker restart redis

# Option 3: Wait for cache TTL to expire (usually 1 hour)
```

#### Issue B: Response uses PascalCase instead of camelCase
**Symptom:** Response has `OptimizedSql` instead of `optimizedSql`

**Cause:** JSON serialization not configured correctly

**Solution:** Check `Program.cs` has:
```csharp
options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
```

#### Issue C: Frontend not receiving the response
**Symptom:** No console logs, no data in component

**Cause:** Mutation not calling onSuccess

**Solution:** Check QueryLab.jsx mutation setup

### 4. Force Fresh Optimization

To bypass cache and get a fresh result:

1. Modify the SQL query slightly (add a space or comment)
2. Or clear the cache as shown above
3. Or restart the API

### 5. Manual Test

Use this HTTP request to test the API directly:

```http
POST https://localhost:7189/api/query-optimizer/analyze
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "sql": "SELECT * FROM Users WHERE Id = 1",
  "connectionId": "your-connection-guid",
  "includeExecutionPlan": false
}
```

Expected response should have `optimizedSql` field with SQL content.

## Frontend Changes Made

### OptimizedSqlViewer.jsx
1. Added debug console logging
2. Added fallback to show originalSql if optimizedSql is empty
3. Added fallback message if both are empty

```javascript
value={result.optimizedSql || result.originalSql || '-- No SQL available'}
```

## Next Steps

1. Check browser console for debug logs
2. Check network response structure
3. If optimizedSql is empty, clear cache
4. If still not working, check if LLM is returning valid JSON

## Related Files
- Frontend: `frontend/src/components/query-lab/OptimizedSqlViewer.jsx`
- Frontend: `frontend/src/pages/QueryLab.jsx`
- Backend: `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`
- Backend: `TextToSqlAgent.API/Controllers/QueryOptimizerController.cs`
