# SSE Streaming - Quick Reference

## 🎯 What Changed?

**One sentence:** Orchestrator now emits progress events as it actually processes, instead of controller emitting fake events upfront.

## 🔧 How to Use

### Backend (Already Done ✅)

```csharp
// Create progress reporter
var progress = new Progress<AgentStageEvent>(async stageEvent =>
{
    await WriteSseEventAsync("stage_update", stageEvent, ct);
});

// Pass to orchestrator
var response = await _orchestrator.ProcessQueryAsync(
    question,
    conversationId,
    conversationHistory,
    progress,  // ← This is the key!
    ct);
```

### Frontend (Already Working ✅)

```javascript
// useStreamingQuery.js already handles stage_update events
const { startStream, progress, currentStage } = useStreamingQuery();

// Start streaming
await startStream(question, conversationId, connectionId);

// React to progress updates
console.log(`Progress: ${progress}%`);
console.log(`Stage: ${currentStage.stage}`);
console.log(`Message: ${currentStage.message}`);
```

## 📊 Progress Stages

| Progress | Stage | Message | When |
|----------|-------|---------|------|
| 5% | VALIDATING | "Validating and normalizing..." | Query validation |
| 10% | CLASSIFYING | "Analyzing your question intent..." | Intent classification |
| 20% | SCHEMA_RETRIEVAL | "Loading database schema..." | Schema loading |
| 35% | SCHEMA_RETRIEVAL | "Finding relevant tables..." | RAG vector search |
| 50% | SQL_GENERATION | "Generating SQL query with AI..." | LLM call (longest) |
| 65% | SQL_VALIDATION | "Validating SQL safety..." | SQL safety check |
| 75% | EXECUTING | "Executing SQL query..." | Database execution |
| 78%+ | CORRECTING | "Auto-correcting SQL (attempt N)..." | Error recovery |
| 90% | BUILDING_RESPONSE | "Building final response..." | Format answer |
| 100% | COMPLETED | "Processing complete!" | Done |

## 🧪 Quick Test

```powershell
# Test with PowerShell
.\test-sse-streaming.ps1 `
  -Token "your-jwt-token" `
  -ConnectionId "your-connection-id" `
  -Question "Show me all users"
```

Expected: Progress bar moves smoothly from 0% → 100% over 5-30s depending on query complexity.

## 🐛 Troubleshooting

### Problem: Progress stuck at 0%
**Cause:** Progress reporter not passed to orchestrator  
**Fix:** Ensure `progress` parameter is passed in ProcessQueryAsync call

### Problem: Progress jumps from 0% to 100%
**Cause:** Orchestrator not emitting progress events  
**Fix:** Check that progress?.Report() calls are present in orchestrator

### Problem: Progress stuck at 50%
**Cause:** LLM call taking too long (10-20s is normal)  
**Fix:** This is expected - SQL generation is the longest step

### Problem: No events received
**Cause:** SSE connection failed or CORS issue  
**Fix:** Check browser console, verify CORS headers, check auth token

## 📚 Related Files

- `TextToSqlAgent.Core/Models/AgentStageEvent.cs` - Event model
- `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs` - Progress emitter
- `TextToSqlAgent.API/Controllers/StreamingAgentController.cs` - SSE endpoint
- `frontend/src/hooks/useStreamingQuery.js` - Frontend hook
- `frontend/src/hooks/useStreamingProgress.js` - Progress UI (if exists)

## 🎓 Key Concepts

### IProgress<T> Pattern
```csharp
// Create reporter
var progress = new Progress<AgentStageEvent>(stageEvent => {
    // Handle event (e.g., write to SSE stream)
});

// Pass to async method
await LongRunningTask(progress);

// Inside LongRunningTask
progress?.Report(new AgentStageEvent { ... });
```

### SSE Event Format
```
event: stage_update
data: {"stage":"SQL_GENERATION","message":"Generating SQL...","progress":0.50}

event: result
data: {"success":true,"answer":"...","sql":"..."}
```

### Frontend SSE Parsing
```javascript
// Parse SSE events
for (const line of eventBlock.split('\n')) {
    if (line.startsWith('event: ')) {
        eventType = line.slice(7).trim();
    } else if (line.startsWith('data: ')) {
        eventData = JSON.parse(line.slice(6));
    }
}
```

---

**Last Updated:** March 29, 2026  
**Version:** 1.0 (Phase 1 Complete)
