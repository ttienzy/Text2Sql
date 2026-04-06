# SSE Streaming Flow Diagram

## 🔄 Complete Flow: Frontend → Backend → Database

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           FRONTEND (React)                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  User types: "Show me all users"                                       │
│       ↓                                                                 │
│  useStreamingQuery.startStream(question, conversationId, connectionId) │
│       ↓                                                                 │
│  fetch('/api/v2/agent/process/stream', {                               │
│    method: 'POST',                                                      │
│    body: { question, conversationId, connectionId }                    │
│  })                                                                     │
│       ↓                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │ SSE Event Stream (text/event-stream)                        │       │
│  │                                                              │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"VALIDATING","progress":0.05}                │       │
│  │       ↓ [0.2s]                                               │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"CLASSIFYING","progress":0.10}               │       │
│  │       ↓ [2s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"SCHEMA_RETRIEVAL","progress":0.20}          │       │
│  │       ↓ [3s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"SCHEMA_RETRIEVAL","progress":0.35}          │       │
│  │       ↓ [8s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"SQL_GENERATION","progress":0.50}            │       │
│  │       ↓ [1s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"SQL_VALIDATION","progress":0.65}            │       │
│  │       ↓ [3s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"EXECUTING","progress":0.75}                 │       │
│  │       ↓ [2s]                                                 │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"BUILDING_RESPONSE","progress":0.90}         │       │
│  │       ↓ [0.5s]                                               │       │
│  │ event: stage_update                                          │       │
│  │ data: {"stage":"COMPLETED","progress":1.0}                  │       │
│  │       ↓                                                       │       │
│  │ event: result                                                │       │
│  │ data: {"success":true,"answer":"...","sql":"..."}           │       │
│  └─────────────────────────────────────────────────────────────┘       │
│       ↓                                                                 │
│  Update UI: setProgress(75%), setCurrentStage("EXECUTING")             │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                      BACKEND (StreamingAgentController)                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  [HttpPost("process/stream")]                                           │
│  public async Task ProcessStream(StreamQueryRequest request)            │
│  {                                                                      │
│      // Setup SSE headers                                               │
│      Response.Headers.Append("Content-Type", "text/event-stream");     │
│                                                                         │
│      // ✅ Create progress reporter that writes SSE events              │
│      var progress = new Progress<AgentStageEvent>(async stageEvent =>  │
│      {                                                                  │
│          await WriteSseEventAsync("stage_update", stageEvent, ct);     │
│      });                                                                │
│                                                                         │
│      // Load conversation history                                       │
│      var conversationHistory = await LoadHistoryAsync(...);            │
│                                                                         │
│      // ✅ Pass progress to orchestrator                                │
│      var response = await _orchestrator.ProcessQueryAsync(             │
│          request.Question,                                              │
│          request.ConversationId,                                        │
│          conversationHistory,                                           │
│          progress,  // ← KEY: Progress reporter                        │
│          ct);                                                           │
│                                                                         │
│      // Emit final result                                               │
│      await WriteSseEventAsync("result", response, ct);                 │
│  }                                                                      │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                  ORCHESTRATOR (EnhancedAgentOrchestrator)               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  public async Task<AgentResponse> ProcessQueryAsync(                   │
│      string userQuestion,                                               │
│      string? conversationId,                                            │
│      List<Message>? conversationHistory,                                │
│      IProgress<AgentStageEvent>? progress,  // ← NEW parameter         │
│      CancellationToken ct)                                              │
│  {                                                                      │
│      // Step 0: Validate                                                │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = VALIDATING, Progress = 0.05                           │
│      });                                                                │
│      var validation = await ValidateAsync(...);  // [100-500ms]        │
│                                                                         │
│      // Step -1: Classify Intent                                        │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = CLASSIFYING, Progress = 0.10                          │
│      });                                                                │
│      var intent = await ClassifyAsync(...);  // [500ms-2s]             │
│                                                                         │
│      // Step 1: Load Schema                                             │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = SCHEMA_RETRIEVAL, Progress = 0.20                     │
│      });                                                                │
│      await EnsureSchemaLoadedAsync(...);  // [1-3s]                    │
│                                                                         │
│      // Step 4: RAG Retrieval                                           │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = SCHEMA_RETRIEVAL, Progress = 0.35                     │
│      });                                                                │
│      var schema = await RetrieveAsync(...);  // [2-3s]                 │
│                                                                         │
│      // Step 6: Generate SQL                                            │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = SQL_GENERATION, Progress = 0.50                       │
│      });                                                                │
│      var sql = await GenerateSqlAsync(...);  // [5-15s] ← LONGEST      │
│                                                                         │
│      // Step 7: Validate SQL                                            │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = SQL_VALIDATION, Progress = 0.65                       │
│      });                                                                │
│      ValidateSql(sql);  // [100-500ms]                                 │
│                                                                         │
│      // Step 9: Execute                                                 │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = EXECUTING, Progress = 0.75                            │
│      });                                                                │
│      var result = await ExecuteAsync(sql);  // [1-5s]                  │
│                                                                         │
│      // Step 10: Format                                                 │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = BUILDING_RESPONSE, Progress = 0.90                    │
│      });                                                                │
│      var answer = await FormatAsync(...);  // [1-3s]                   │
│                                                                         │
│      // Complete                                                        │
│      progress?.Report(new AgentStageEvent {                            │
│          Stage = COMPLETED, Progress = 1.0                             │
│      });                                                                │
│                                                                         │
│      return response;                                                   │
│  }                                                                      │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                         DATABASE (PostgreSQL/MySQL)                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  SELECT * FROM users LIMIT 100;                                         │
│       ↓                                                                 │
│  Returns 42 rows                                                        │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## 🎨 Visual Progress Bar

```
Time:    0s      2s      5s      8s     15s     20s     25s     28s    30s
         │       │       │       │       │       │       │       │       │
Progress: 5%    10%     20%     35%     50%     65%     75%     90%    100%
         ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Stage:   Valid  Class   Schema  RAG     Generate Validate Execute Build  Done
         ✓      ✓       ✓       ✓       ✓       ✓       ✓       ✓      ✓
```

## 🔍 Debugging

### Enable verbose logging
```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "TextToSqlAgent.Application.Services.EnhancedAgentOrchestrator": "Debug",
      "TextToSqlAgent.API.Controllers.StreamingAgentController": "Debug"
    }
  }
}
```

### Watch SSE events in browser
```javascript
// Browser console
const eventSource = new EventSource('/api/v2/agent/process/stream');
eventSource.addEventListener('stage_update', (e) => {
    const data = JSON.parse(e.data);
    console.log(`[${data.progress * 100}%] ${data.stage}: ${data.message}`);
});
```

### Monitor with curl
```bash
curl -N -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"question":"Show me all users","connectionId":"xxx"}' \
     https://localhost:7189/api/v2/agent/process/stream
```

## ⚡ Performance Tips

1. **Schema caching** - Schema loads once, cached for subsequent queries
2. **Conversation context** - Reuses in-memory context, no DB lookup per query
3. **RAG optimization** - Vector search is fast (~500ms)
4. **LLM streaming** - Phase 2 will add token-by-token streaming

## 🎯 Success Criteria

✅ Progress bar moves continuously (no stuck at 50%)  
✅ Each stage corresponds to actual work  
✅ Total time unchanged (~5-30s depending on query)  
✅ User sees what's happening at each moment  
✅ Self-correction visible if it occurs  
✅ No breaking changes to existing code

---

**Status:** ✅ IMPLEMENTED  
**Phase:** 1 of 3  
**Next:** LLM Token Streaming (Phase 2)
