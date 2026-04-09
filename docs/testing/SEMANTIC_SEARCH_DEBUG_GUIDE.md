# Semantic Search Debug Guide

**Date:** 2026-04-09  
**Issue:** Semantic Search không hoạt động ở frontend  
**Purpose:** Hướng dẫn debug và fix semantic search

---

## Các Nguyên Nhân Có Thể

### 1. Database Chưa Được Analyze ❌
**Triệu chứng:**
- Frontend hiển thị error: "Schema not analyzed"
- Backend log: "Please analyze the database first before searching"

**Nguyên nhân:**
- Semantic search cần schema được index vào Qdrant trước
- Indexing chỉ xảy ra khi user click "Analyze Database"

**Cách fix:**
```bash
# 1. Gọi analyze endpoint trước
POST /api/db-explorer/{connectionId}/analyze?mode=overview

# 2. Sau đó mới search được
GET /api/db-explorer/{connectionId}/search?query=khách hàng
```

**Frontend fix:**
- Thêm check: Nếu chưa analyze → hiển thị message "Please analyze database first"
- Hoặc tự động gọi analyze khi mở Semantic Search lần đầu

---

### 2. Qdrant Service Không Được Inject ❌
**Triệu chứng:**
- Backend error: "Semantic search not available"
- Backend log: "Qdrant indexer service is not configured"

**Nguyên nhân:**
- `DbExplorerQdrantIndexer` không được đăng ký trong DI container
- Hoặc Qdrant connection string không đúng

**Cách check:**
```csharp
// Program.cs - Check if this line exists
builder.Services.AddScoped<DbExplorerQdrantIndexer>();

// appsettings.json - Check Qdrant config
{
  "Qdrant": {
    "Endpoint": "http://localhost:6333",
    "ApiKey": ""
  }
}
```

**Cách fix:**
- Đảm bảo Qdrant đang chạy: `docker ps | grep qdrant`
- Check connection string trong appsettings.json
- Restart backend sau khi fix

---

### 3. Collection Name Không Đúng ❌
**Triệu chứng:**
- Search trả về 0 results
- Backend log: "Found 0 results"

**Nguyên nhân:**
- Collection name được set từ database name
- Nếu database name extract sai → search vào collection không tồn tại

**Cách check:**
```csharp
// Controller line 1267-1270
var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);
var qdrantService = HttpContext.RequestServices.GetRequiredService<QdrantService>();
qdrantService.SetCollectionName(databaseName);
```

**Debug:**
- Add log để xem collection name: `_logger.LogInformation("Collection name: {Name}", databaseName);`
- Check Qdrant collections: `curl http://localhost:6333/collections`

---

### 4. Qdrant Chưa Có Data ❌
**Triệu chứng:**
- Search trả về 0 results
- Qdrant collection tồn tại nhưng empty

**Nguyên nhân:**
- Indexing failed khi analyze
- Hoặc indexing chưa được gọi

**Cách check:**
```bash
# Check Qdrant collections
curl http://localhost:6333/collections

# Check points count
curl http://localhost:6333/collections/{collection_name}
```

**Cách fix:**
- Re-analyze database: `POST /api/db-explorer/{connectionId}/analyze?forceRefresh=true`
- Check backend logs cho Qdrant indexing errors

---

### 5. Score Threshold Quá Cao ❌
**Triệu chứng:**
- Search trả về 0 results hoặc rất ít results
- Nhưng Qdrant có data

**Nguyên nhân:**
- Default scoreThreshold = 0.7 (70%)
- Nếu query không match tốt → score < 0.7 → filtered out

**Cách fix:**
```javascript
// Frontend - Lower threshold for testing
const { data: searchResults } = useSemanticSearchQuery(
    connectionId,
    activeQuery,
    { 
        enabled: activeQuery.length >= 2,
        scoreThreshold: 0.5  // Lower to 50%
    }
);
```

---

## Debug Workflow

### Step 1: Check Backend Logs
```bash
# Tìm logs liên quan đến semantic search
grep -i "semantic search" logs/app.log
grep -i "qdrant" logs/app.log
```

**Logs cần tìm:**
- `[DbExplorerQdrantIndexer] Indexing schema with semantic tags into Qdrant...`
- `[DbExplorerQdrantIndexer] ✅ Successfully indexed {Count} tables`
- `[DbExplorer] Semantic search for '{Query}' returned {Count} results`

---

### Step 2: Check Qdrant Status
```bash
# Check if Qdrant is running
docker ps | grep qdrant

# Check collections
curl http://localhost:6333/collections

# Check specific collection
curl http://localhost:6333/collections/YourDatabaseName
```

**Expected response:**
```json
{
  "result": {
    "status": "green",
    "vectors_count": 50,  // Should match table count
    "points_count": 50
  }
}
```

---

### Step 3: Test Backend Directly
```bash
# Use test-semantic-search-debug.http file

# 1. Check status
GET /api/db-explorer/{connectionId}/status

# 2. Analyze database
POST /api/db-explorer/{connectionId}/analyze?mode=overview

# 3. Search
GET /api/db-explorer/{connectionId}/search?query=test&scoreThreshold=0.5
```

---

### Step 4: Check Frontend Network Tab
```
1. Open DevTools → Network tab
2. Search for "search" request
3. Check:
   - Request URL: /api/db-explorer/{connectionId}/search?query=...
   - Status Code: 200 or error?
   - Response body: { results: [...] } or error?
```

**Common errors:**
- 400: "Schema not analyzed" → Need to analyze first
- 500: "Semantic search not available" → Qdrant not configured
- 200 but empty results → Check score threshold or Qdrant data

---

## Quick Fixes

### Fix 1: Force Re-Index
```bash
# Delete cache and re-analyze
DELETE /api/db-explorer/{connectionId}/cache
POST /api/db-explorer/{connectionId}/analyze?forceRefresh=true&mode=overview
```

---

### Fix 2: Lower Score Threshold
```javascript
// frontend/src/api/dbExplorer/queries.js
export const useSemanticSearchQuery = (connectionId, query, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'search', connectionId, query],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/search`, {
                params: {
                    query,
                    limit: options.limit || 10,
                    scoreThreshold: options.scoreThreshold || 0.5,  // Changed from 0.7 to 0.5
                },
            });
            return response.data;
        },
        enabled: !!connectionId && !!query && query.length >= 2,
        staleTime: 1000 * 60 * 5,
        ...options,
    });
};
```

---

### Fix 3: Add Auto-Analyze on First Search
```javascript
// frontend/src/components/db-explorer/SemanticSearch.jsx
const SemanticSearch = ({ connectionId, onTableSelect, style }) => {
    const [searchQuery, setSearchQuery] = useState('');
    const [activeQuery, setActiveQuery] = useState('');
    
    // Check if database is analyzed
    const { data: status } = useStatusQuery(connectionId);
    const analyzeMutation = useAnalyzeMutation();
    
    const handleSearch = async (value) => {
        const trimmed = value.trim();
        if (trimmed.length >= 2) {
            // Auto-analyze if not analyzed yet
            if (!status?.hasSchema) {
                await analyzeMutation.mutateAsync({ 
                    connectionId, 
                    mode: 'overview' 
                });
            }
            setActiveQuery(trimmed);
        }
    };
    
    // ... rest of component
};
```

---

### Fix 4: Better Error Messages
```javascript
// frontend/src/components/db-explorer/SemanticSearch.jsx
{error && (
    <Alert
        message="Search Failed"
        description={
            error.response?.data?.error === "Schema not analyzed" 
                ? "Please analyze the database first by clicking the 'Analyze' button"
                : error.response?.data?.message || error.message
        }
        type="error"
        showIcon
        action={
            error.response?.data?.error === "Schema not analyzed" && (
                <Button 
                    size="small" 
                    onClick={() => analyzeMutation.mutate({ connectionId, mode: 'overview' })}
                >
                    Analyze Now
                </Button>
            )
        }
        style={{ marginBottom: 16 }}
    />
)}
```

---

## Expected Behavior

### When Working Correctly:

1. **User clicks "Analyze Database"**
   - Backend scans schema
   - Backend generates semantic tags for each table
   - Backend indexes to Qdrant
   - Log: "✅ Successfully indexed 50 tables with semantic tags"

2. **User searches "khách hàng"**
   - Frontend calls: `GET /api/db-explorer/{id}/search?query=khách hàng`
   - Backend embeds query
   - Backend searches Qdrant
   - Backend returns results with scores
   - Frontend displays results

3. **Results should include:**
   - Tables with "khách hàng" in name
   - Tables with "customer" in semantic tags
   - Tables with "KH" abbreviation
   - Related tables (Users, Contacts, etc.)

---

## Common Issues Summary

| Issue | Symptom | Fix |
|-------|---------|-----|
| Not analyzed | "Schema not analyzed" error | Analyze database first |
| Qdrant down | "Semantic search not available" | Start Qdrant container |
| No data | 0 results | Re-analyze with forceRefresh |
| High threshold | Few results | Lower scoreThreshold to 0.5 |
| Wrong collection | 0 results | Check database name extraction |

---

## Testing Checklist

- [ ] Qdrant is running (`docker ps`)
- [ ] Database is analyzed (`POST /analyze`)
- [ ] Qdrant has data (`curl /collections/{name}`)
- [ ] Search returns results (`GET /search?query=test`)
- [ ] Frontend displays results
- [ ] Score threshold is reasonable (0.5-0.7)
- [ ] Error messages are helpful

---

## Next Steps

1. **Run test-semantic-search-debug.http** để test backend
2. **Check browser DevTools Network tab** để xem frontend request
3. **Check backend logs** để xem Qdrant indexing
4. **Lower score threshold** nếu cần
5. **Add auto-analyze** nếu muốn UX tốt hơn

---

**Nếu vẫn không work, cung cấp:**
1. Backend logs (grep "semantic search")
2. Frontend error message
3. Qdrant collection status (`curl /collections`)
4. Network tab screenshot
