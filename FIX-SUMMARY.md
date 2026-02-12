# ✅ FIX COMPLETED - Qdrant gRPC Error

## 🔧 Changes Made

### Issue 1: HTTP/2 PROTOCOL_ERROR ✅ FIXED

**Root Cause**: Using gRPC client on REST API port

**Solution**: Changed from port 6333 (REST) → 6334 (gRPC)

**Files Modified**:

1. `appsettings.json`:
   - Port: 6333 → 6334
   - UseGrpc: false → true

2. `QdrantConfig.cs`:
   - Default port: 6333 → 6334
   - Default UseGrpc: false → true
   - Added comment explaining port usage

---

### Issue 2: Collection Doesn't Exist ✅ FIXED

**Root Cause**: Schema not indexed to Qdrant on startup

**Solution**: Auto-create collection and index schema on first query

**File Modified**: `TextToSqlAgentOrchestrator.cs`

**Changes**:

1. Added dependencies:
   - `SchemaIndexer` - for indexing schema
   - `QdrantService` - for collection management

2. Added field:
   - `_schemaIndexed` - track if schema already indexed

3. Added method:
   - `EnsureSchemaIndexedAsync()` - auto-index schema
     - Check if collection exists → create if not
     - Check point count → index if empty
     - Skip if already indexed

4. Updated `ProcessQueryAsync()`:
   - Call `EnsureSchemaIndexedAsync()` after schema scan
   - Step 2.5: "Index schema vào vector database"

5. Updated `ClearSchemaCache()`:
   - Also reset `_schemaIndexed` flag

---

## 🧪 How to Test

1. **Stop current app** (Ctrl+C)

2. **Clear Qdrant collection** (optional, to test from scratch):

   ```bash
   curl -X DELETE http://localhost:6333/collections/schema_embeddings
   ```

3. **Restart app**:

   ```bash
   cd TextToSqlAgent.Console
   dotnet run
   ```

4. **Try first query**:

   ```
   Question: Có bao nhiêu bảng trong database?
   ```

5. **Expected logs**:

   ```
   [Agent] Quét schema database...
   [Agent] Quét hoàn tất: 5 bảng, 4 quan hệ
   [Agent] Kiểm tra Qdrant collection...
   [Agent] Tạo collection mới...
   [Qdrant] Creating collection: schema_embeddings
   [Qdrant] Collection created
   [Agent] Index schema vào Qdrant...
   [Schema Indexer] Building schema documents...
   [Gemini Embedding] Generating batch embeddings...
   [Qdrant] Upserting X points
   [Agent] ✓ Schema đã được index
   [Schema Retriever] Retrieving schema for question...
   [Schema Retriever] Found 5 relevant schema elements
   ... (SQL generation & execution)
   ```

6. **Second query** (should use cached schema):

   ```
   Question: Liệt kê tất cả khách hàng
   ```

   Expected: No re-indexing, fast response

---

## 📊 Qdrant Port Reference

| Port     | Protocol      | Purpose  | Usage          |
| -------- | ------------- | -------- | -------------- |
| **6333** | HTTP/1.1      | REST API | curl, Postman  |
| **6334** | HTTP/2 (gRPC) | gRPC API | .NET client ✅ |
| 6335     | HTTP/1.1      | Internal | Cluster only   |

---

## 🎯 What Was Fixed

### Before:

```
❌ QdrantClient (gRPC) → localhost:6333 (REST API)
   → HTTP/2 PROTOCOL_ERROR

❌ SearchAsync() → Collection doesn't exist
   → Not found error
```

### After:

```
✅ QdrantClient (gRPC) → localhost:6334 (gRPC)
   → Protocol match

✅ First query → Auto-create collection + Auto-index schema
   → SearchAsync() works
```

---

## 🔄 Auto-Index Flow

```
First Query:
  1. Scan schema (5 tables)
  2. Check Qdrant collection exists?
     → No → Create collection
  3. Check point count = 0?
     → Yes → Index schema (tables, columns, relationships)
  4. Mark _schemaIndexed = true
  5. Continue with RAG search

Second Query:
  1. Use cached schema
  2. Schema already indexed → Skip indexing
  3. Directly RAG search
```

---

## 🚨 Notes

1. **First query is slower** (schema indexing ~17-20s for 34 documents with 500ms delay)
   - This is NORMAL - wait for it to complete
   - Delay prevents hitting Gemini API rate limits (60 RPM)
   - Progress shown in logs: "Processing batch 1/4", "2/4", etc.

2. **Subsequent queries are fast** (cached schema + indexed vectors)
3. **Clear cache command** will force re-index on next query
4. **Qdrant data persists** between app restarts (unless you delete collection manually)

---

## ⏱️ Indexing Performance

**Current delays** (optimized):

- 500ms between embeddings = ~120 requests/min (safe for 60 RPM limit)
- For 34 documents: ~17 seconds total

**Optional: Further optimization**:

- Set delay to 300ms for faster indexing (risky if high load)
- Edit `SchemaIndexer.cs` line 217: `Task.Delay(300)`
- Edit `GeminiEmbeddingClient.cs` line 80: `Task.Delay(300)`

**Don't set below 300ms** - risks hitting rate limits!

---

## 🛠️ Clear Qdrant Collection

If you want to force re-indexing:

```powershell
# Run this script
.\clear-qdrant.ps1

# Or manually:
curl -X DELETE http://localhost:6333/collections/schema_embeddings
```

---

## ✅ Checklist

- [x] Fix gRPC port (6333 → 6334)
- [x] Add auto-collection creation
- [x] Add auto-schema indexing
- [x] Add indexed status tracking
- [x] Update clear cache to reset indexed flag
- [x] Add comprehensive logging

---

Created: 2026-01-26 22:57
Status: ✅ READY TO TEST
