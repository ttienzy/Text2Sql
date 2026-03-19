# Cache Clear Guide - Fix Missing Columns in ER Diagram

## Problem
Columns are not showing in the ER Diagram because the cached graph data was created before the `Columns` property was added to the GraphData model.

## Solution
Clear the cache and re-analyze the database to regenerate the graph with column information.

## Steps

### Option 1: Using HTTP Client (test-graph-columns.http)

1. Open `test-graph-columns.http`
2. Update the variables:
   - `@connectionId` = your connection ID
   - `@token` = your JWT token (if needed)
3. Execute requests in order:
   - **Step 1**: Clear Cache → `DELETE /api/db-explorer/{connectionId}/cache`
   - **Step 2**: Re-analyze → `POST /api/db-explorer/{connectionId}/analyze?forceRefresh=true`
   - **Step 3**: Get Graph → `GET /api/db-explorer/{connectionId}/graph` (verify columns are present)

### Option 2: Using Frontend UI

1. Go to DB Explorer page
2. Click the **Refresh** button (🔄) in the Database Overview Card
3. Wait for analysis to complete
4. Navigate to the **ER Diagram** tab
5. Columns should now be visible in the table nodes

### Option 3: Using curl

```bash
# Replace with your actual values
CONNECTION_ID="your-connection-id"
BASE_URL="https://localhost:7189"
TOKEN="your-jwt-token"

# Step 1: Clear cache
curl -X DELETE "$BASE_URL/api/db-explorer/$CONNECTION_ID/cache" \
  -H "Authorization: Bearer $TOKEN"

# Step 2: Re-analyze
curl -X POST "$BASE_URL/api/db-explorer/$CONNECTION_ID/analyze?forceRefresh=true" \
  -H "Authorization: Bearer $TOKEN"

# Step 3: Verify graph has columns
curl -X GET "$BASE_URL/api/db-explorer/$CONNECTION_ID/graph" \
  -H "Authorization: Bearer $TOKEN"
```

## Verification

After re-analyzing, the ER Diagram should show:
- ✅ Column names with icons (🔑 for PK, 🔗 for FK)
- ✅ Data types next to each column
- ✅ Nullable indicator (?) for nullable columns
- ✅ Highlighted background for primary key columns
- ✅ Scrollable list if more than 10 columns
- ✅ "+X more columns..." indicator if table has >10 columns

## Technical Details

### What Changed
- Added `Columns` property to `GraphNode` model
- Added `GraphColumn` class with Name, Type, IsPrimaryKey, IsForeignKey, IsNullable
- Updated `GraphDataBuilder` to populate columns from `EnhancedTableInfo`
- Updated DTOs (`GraphNodeDto`, `GraphColumnDto`)
- Updated controller mapping to include columns in API response
- Updated frontend `TableNode` component to display columns

### Why Cache Clear is Needed
Redis cache stores the serialized `GraphData` object. Old cached data doesn't have the `Columns` property, so it returns `null` or empty array. Re-analyzing regenerates the graph with the new structure.

### Alternative: Auto-Migration (Future Enhancement)
Could add version checking to auto-invalidate old cache format:
```csharp
// In DbExplorerCacheService
if (graph != null && graph.Nodes.Any(n => n.Columns == null || n.Columns.Count == 0))
{
    _logger.LogWarning("Old graph format detected, invalidating cache");
    InvalidateCache(connectionId);
    return null;
}
```
