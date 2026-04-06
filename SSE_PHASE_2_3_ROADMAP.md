# SSE Streaming - Phase 2 & 3 Roadmap

## 📋 Overview

Phase 1 (✅ COMPLETED) giải quyết fake streaming bằng cách emit progress events tại mỗi step thực.  
Phase 2 & 3 sẽ cải thiện UX hơn nữa với token streaming và optimization.

---

## 🚀 Phase 2: LLM Token Streaming (3-5 ngày)

### Goal
Stream từng token SQL từ LLM về frontend real-time, thay vì chờ toàn bộ SQL được generate.

### Current Problem
```
Progress: 50% ━━━━━━━━━━━━━━━━━━━━░░░░░░░░░░░░░░░░░░░░
Message:  "Generating SQL query with AI..."
          ⏸️ User chờ 10-15s không có feedback
```

### After Phase 2
```
Progress: 50% ━━━━━━━━━━━━━━━━━━━━░░░░░░░░░░░░░░░░░░░░
SQL:      SELECT u.name, u.email
          FROM users u
          WHERE u.created_at > '2024-01-01'
          ORDER BY u.created_at DESC
          LIMIT 100;
          ↑ Từng dòng xuất hiện dần (như ChatGPT)
```

### Implementation Tasks

#### Task 2.1: Backend - Add Streaming Support to SqlGeneratorService
```csharp
// File: TextToSqlAgent.Plugins/SqlGeneratorPlugin.cs

public async Task<string> GenerateSqlStreamAsync(
    IntentAnalysis intent,
    RetrievedSchemaContext schema,
    string question,
    IProgress<string>? tokenProgress = null,
    CancellationToken ct = default)
{
    var chatRequest = new ChatCompletionsOptions
    {
        DeploymentName = _config.DeploymentName,
        Messages = { ... },
        Temperature = 0.1f,
        MaxTokens = 500
    };

    // ✅ Use streaming API
    var streamingResponse = await _openAiClient.GetChatCompletionsStreamingAsync(
        chatRequest, ct);

    var sqlBuilder = new StringBuilder();

    await foreach (var update in streamingResponse.WithCancellation(ct))
    {
        if (update.ContentUpdate != null)
        {
            var token = update.ContentUpdate;
            sqlBuilder.Append(token);
            
            // ✅ Emit token to progress reporter
            tokenProgress?.Report(token);
        }
    }

    return sqlBuilder.ToString();
}
```

#### Task 2.2: Backend - Add sql_token Event Type
```csharp
// File: TextToSqlAgent.API/Controllers/StreamingAgentController.cs

// In ProcessStream method, update progress reporter:
var progress = new Progress<AgentStageEvent>(async stageEvent =>
{
    await WriteSseEventAsync("stage_update", stageEvent, ct);
});

// Add token progress reporter
var tokenProgress = new Progress<string>(async token =>
{
    await WriteSseEventAsync("sql_token", new { token }, ct);
});

// Pass both to orchestrator
var response = await _orchestrator.ProcessQueryAsync(
    request.Question,
    request.ConversationId,
    conversationHistory,
    progress,
    tokenProgress,  // ← NEW
    ct);
```

#### Task 2.3: Frontend - Handle sql_token Events
```javascript
// File: frontend/src/hooks/useStreamingQuery.js

const [sqlTokens, setSqlTokens] = useState([]);

// In event handler:
switch (eventType) {
    case 'sql_token':
        setSqlTokens(prev => [...prev, data.token]);
        break;
    
    case 'stage_update':
        // ... existing code
        break;
}

return {
    // ... existing returns
    sqlTokens,
    generatedSql: sqlTokens.join('')
};
```

#### Task 2.4: Frontend - Progressive SQL Display
```javascript
// File: frontend/src/components/chat/SqlBlock.jsx

export const SqlBlock = ({ sql, isStreaming, tokens }) => {
    const displaySql = isStreaming ? tokens.join('') : sql;
    
    return (
        <pre className="sql-block">
            <code>{displaySql}</code>
            {isStreaming && <span className="cursor">▊</span>}
        </pre>
    );
};
```

### Expected Result
```
User Experience:
- Progress reaches 50%
- SQL appears token-by-token: "SELECT" → "SELECT u.name" → "SELECT u.name, u.email" → ...
- User sees AI "thinking" in real-time
- Feels much faster even though total time is same
```

### Effort Estimate
- Backend: 2 days
- Frontend: 1 day
- Testing: 1 day
- **Total: 4 days**

---

## ⚡ Phase 3: Optimization & Caching (5-7 ngày)

### Goal
Reduce actual processing time through caching and parallel execution.

### Optimization 1: Schema Pre-loading

**Current:**
```
User selects connection → User asks question → Load schema (2-3s) → Process
```

**After:**
```
User selects connection → Pre-load schema in background → User asks question → Process (schema already cached)
```

**Implementation:**
```csharp
// File: TextToSqlAgent.API/Controllers/ConnectionsController.cs

[HttpPost("{id}/select")]
public async Task<IActionResult> SelectConnection(string id)
{
    // ... existing code ...
    
    // ✅ Trigger background schema pre-loading
    _ = Task.Run(async () =>
    {
        try
        {
            await _orchestrator.PreloadSchemaAsync(id);
            _logger.LogInformation("Schema pre-loaded for connection {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pre-load schema for {Id}", id);
        }
    });
    
    return Ok();
}
```

**Impact:** Reduces Step 1 from 2-3s to <100ms (cache hit)

---

### Optimization 2: Intent Classification Caching

**Current:**
```
Every query → LLM call for intent classification (500ms-2s)
```

**After:**
```
Common patterns cached → Only novel queries call LLM
```

**Implementation:**
```csharp
// File: TextToSqlAgent.Application/Services/IntentClassificationCache.cs

public class IntentClassificationCache
{
    private readonly MemoryCache _cache = new();
    
    public async Task<IntentClassificationResult> GetOrClassifyAsync(
        string question,
        Func<Task<IntentClassificationResult>> classifier)
    {
        var key = ComputeHash(question.ToLowerInvariant());
        
        if (_cache.TryGetValue(key, out IntentClassificationResult? cached))
        {
            return cached;
        }
        
        var result = await classifier();
        
        _cache.Set(key, result, TimeSpan.FromMinutes(30));
        
        return result;
    }
}
```

**Impact:** Reduces Step -1 from 500ms-2s to <10ms (cache hit)

---

### Optimization 3: Parallel Processing

**Current (Sequential):**
```csharp
// Step 0: Validate (500ms)
var validation = await ValidateAsync(...);

// Step 1: Load Schema (2s)
await LoadSchemaAsync(...);

// Total: 2.5s
```

**After (Parallel):**
```csharp
// ✅ Run validation and schema loading in parallel
var validationTask = ValidateAsync(...);
var schemaTask = LoadSchemaAsync(...);

await Task.WhenAll(validationTask, schemaTask);

// Total: 2s (saved 500ms)
```

**Implementation:**
```csharp
// In ProcessQueryAsync:

// ✅ Start both tasks in parallel
var validationTask = queryValidator.ValidateQueryAsync(enrichedQuestion, [], ct);
var schemaTask = EnsureSchemaLoadedAsync(steps, ct);

// Emit validation progress
progress?.Report(new AgentStageEvent { Stage = VALIDATING, Progress = 0.05 });

// Wait for validation first (fast)
var validation = await validationTask;

if (!validation.IsRelevant)
{
    // Early exit - cancel schema loading
    return response;
}

// Emit schema progress
progress?.Report(new AgentStageEvent { Stage = SCHEMA_RETRIEVAL, Progress = 0.20 });

// Wait for schema (may already be done)
await schemaTask;
```

**Impact:** Reduces total time by 500ms-1s

---

### Optimization 4: Query Result Caching

**Current:**
```
Same query asked twice → Execute SQL twice
```

**After:**
```
Same query asked twice → Return cached result (if fresh)
```

**Implementation:**
```csharp
// File: TextToSqlAgent.Infrastructure/Caching/RedisQueryResultCache.cs
// (Already exists, just need to enable)

// In appsettings.json:
{
  "QueryResultCache": {
    "Enabled": true,
    "TtlSeconds": 300,  // 5 minutes
    "MaxCacheSize": 1000
  }
}
```

**Impact:** Reduces Step 9 from 1-5s to <50ms (cache hit)

---

## 📊 Phase 2 & 3 Combined Impact

### Current (Phase 1)
```
Timeline: 0s ──────────────────────────────────────────────── 30s
Progress: 5% → 10% → 20% → 35% → 50% ⏸️ → 65% → 75% → 90% → 100%
                                    ↑ 10-15s stuck here
```

### After Phase 2 (Token Streaming)
```
Timeline: 0s ──────────────────────────────────────────────── 30s
Progress: 5% → 10% → 20% → 35% → 50% ████████ → 65% → 75% → 90% → 100%
                                    ↑ SQL typing out
Perceived: Feels 2x faster (even though same time)
```

### After Phase 3 (Optimization)
```
Timeline: 0s ────────────────────────────── 15s
Progress: 5% → 10% → 20% → 35% → 50% ████ → 65% → 75% → 90% → 100%
                    ↑ Parallel      ↑ Cached
Actual: 50% faster (15s instead of 30s)
```

---

## 🎯 Priority Ranking

### High Priority (Do Next)
1. **Phase 2 - LLM Token Streaming** ⭐⭐⭐
   - Biggest UX impact
   - Solves "stuck at 50%" perception
   - Users love seeing AI "think"

### Medium Priority
2. **Phase 3.1 - Schema Pre-loading** ⭐⭐
   - Easy to implement
   - Noticeable speed improvement
   - Low risk

3. **Phase 3.2 - Intent Caching** ⭐⭐
   - Moderate implementation effort
   - Good for repeated queries
   - Requires cache invalidation strategy

### Lower Priority
4. **Phase 3.3 - Parallel Processing** ⭐
   - Complex implementation
   - Modest speed improvement (500ms)
   - Higher risk of race conditions

5. **Phase 3.4 - Query Result Caching** ⭐
   - Already implemented (just enable)
   - Good for dashboards
   - May cause stale data issues

---

## 📝 Implementation Order

### Recommended Sequence
1. ✅ **Phase 1** (DONE) - Real progress reporting
2. 🔜 **Phase 2** (NEXT) - LLM token streaming
3. 🔜 **Phase 3.1** - Schema pre-loading
4. 🔜 **Phase 3.2** - Intent caching
5. 🔜 **Phase 3.3** - Parallel processing (if needed)

### Why This Order?
- Phase 2 has biggest UX impact with moderate effort
- Phase 3.1 is easy win with noticeable improvement
- Phase 3.2 builds on Phase 3.1 (both use caching)
- Phase 3.3 is complex, do last (or skip if not needed)

---

## 🎓 Lessons Learned

### What Worked Well
- ✅ IProgress<T> pattern is perfect for streaming
- ✅ Backward compatibility (optional parameter)
- ✅ Small, focused changes (low risk)
- ✅ Clear progress stages (easy to understand)

### What to Improve
- ⚠️ Need token streaming for LLM calls
- ⚠️ Schema loading still slow on first query
- ⚠️ No cancellation feedback (user doesn't know if cancel worked)

### Best Practices
- Always emit progress AFTER starting work, not before
- Use descriptive messages ("Generating SQL..." not "Processing...")
- Include detail field for debugging
- Handle progress reporter exceptions gracefully
- Test with slow queries to verify progress updates

---

**Status:** Phase 1 ✅ | Phase 2 🔜 | Phase 3 🔜  
**Next Action:** Implement Phase 2 - LLM Token Streaming  
**Estimated Total Effort:** 8-12 days for all phases
