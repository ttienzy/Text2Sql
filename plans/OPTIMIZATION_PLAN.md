# TextToSqlAgent - Optimization Plan

## Executive Summary

Dựa trên phân tích codebase và yêu cầu của user, hệ thống hiện tại đã có:

- ✅ Streaming endpoint (`/api/Agent/stream`) - Frontend đang dùng đúng
- ✅ Qdrant cho semantic schema retrieval - đúng chỗ
- ✅ Background jobs cho async queries - API không bị treo

**Vấn đề cần giải quyết:**

- ⚠️ ReActAgent dùng Custom Tool Selection (2 LLM calls/step) thay vì Native Tool Calling (1 LLM call/step)
- ⚠️ SimpleQueryPipeline dùng regex parsing thay vì Structured Output

---

## Current State Analysis

### 1. ReActAgent Flow (Hiện tại)

```
Step N:
  Think (_reasoningEngine.ThinkAsync)     → LLM Call #1
  Select Tool (_toolSelector.SelectActionAsync) → LLM Call #2
  Execute Tool
  Reflect (_reflectionEngine.ReflectAsync) → LLM Call #3
```

**Total: 3 LLM calls per step × 10 steps = 30 LLM calls**

### 2. Tool Selection (Hiện tại)

- Dùng `LLMToolSelector` - gửi tool descriptions trong prompt
- LLM parse text để chọn tool → không reliable

### 3. SQL Generation (Hiện tại)

- `SimpleQueryPipeline.GenerateSqlAsync` → plain text LLM call
- Dùng regex `ExtractSql` để parse SQL từ response
- Không có structured output validation

---

## Optimization Targets

### Priority 1: Native Tool Calling (High Impact)

**Mục tiêu:** Giảm LLM calls từ 3 → 1-2 per step

**Cách tiếp cận:**

- Dùng OpenAI Function Calling API (`tools` parameter)
- Gộp Think + Select thành 1 LLM call
- Model tự quyết định gọi function nào

**Benefits:**

- Giảm ~50% LLM calls cho ReAct loop
- Reliable tool selection (structured output)
- Không cần mô tả tools trong prompt

**Implementation:**

```csharp
// Thay vì:
var (thought, plan) = await _reasoningEngine.ThinkAsync(...);
var action = await _toolSelector.SelectActionAsync(...);

// Dùng:
var response = await _llmClient.CompleteWithFunctionsAsync(
    prompt,
    tools: GetToolDefinitions(),
    function_call: "auto");
```

---

### Priority 2: Structured Output (Medium Impact)

**Mục tiêu:** SQL generation không cần regex parsing

**Cách tiếp cận:**

- Dùng OpenAI Structured Outputs (`response_format: { type: "json_schema" }`)
- Yêu cầu LLM trả về JSON với schema cố định

**Benefits:**

- Parse không bao giờ lỗi
- Có confidence score
- Có validation tự động

**Implementation:**

```csharp
// Thay vì:
var response = await _llmClient.CompleteAsync(prompt);
var sql = ExtractSql(response); // Regex - fragile

// Dùng:
var response = await _llmClient.CompleteWithStructuredOutputAsync(
    prompt,
    response_format: new JsonSchema { ... });
var sql = response.sql; // Guaranteed parse
```

---

### Priority 3: Streaming Enhancement

**Mục tiêu:** User nhìn thấy tiến trình real-time

**Hiện tại:** ✅ `/api/Agent/stream` đã implement

**Cần kiểm tra:**

- Frontend có đang dùng `/stream` không → ✅ Có
- SSE có bị block không (Nginx proxy_buffering)
- CORS có block SSE không

---

## Implementation Roadmap

### Phase 1: Structured Output for SQL Generation (1-2 days)

**Files cần sửa:**

- [`SimpleQueryPipeline.cs`](TextToSqlAgent.Application/Pipelines/SimpleQueryPipeline.cs)
- [`MediumQueryPipeline.cs`](TextToSqlAgent.Application/Pipelines/MediumQueryPipeline.cs)
- [`ILLMClient.cs`](TextToSqlAgent.Infrastructure/LLM/ILLMClient.cs)

**Steps:**

1. Thêm method `CompleteWithStructuredOutputAsync<T>` vào LLMClient
2. Định nghĩa JSON schema cho SQL generation
3. Thay thế `ExtractSql()` bằng structured parsing

---

### Phase 2: Native Tool Calling (3-5 days)

**Files cần sửa:**

- [`ReActAgent.cs`](TextToSqlAgent.Infrastructure/Agent/ReActAgent.cs)
- [`LLMToolSelector.cs`](TextToSqlAgent.Infrastructure/Agent/LLMToolSelector.cs)
- Tạo `FunctionCallingAgent` mới (hoặc refactor ReActAgent)

**Steps:**

1. Tạo tool definitions từ ToolRegistry
2. Implement function calling flow
3. Remove custom Think + Select separation
4. Add early termination for tool calls

---

### Phase 3: Streaming & UX (1-2 days)

**Files cần sửa:**

- Kiểm tra Nginx config
- [`AgentController.cs`](TextToSqlAgent.API/Controllers/AgentController.cs) - nếu cần

**Steps:**

1. Verify SSE streaming works through proxies
2. Add retry logic in frontend hook
3. Add reconnection handling

---

## Expected Improvements

| Metric              | Before | After  | Improvement    |
| ------------------- | ------ | ------ | -------------- |
| LLM calls (Complex) | 30-45  | 15-20  | ~50% reduction |
| Parse errors        | ~10%   | ~0%    | Reliable       |
| SQL generation time | 5-10s  | 2-3s   | ~60% faster    |
| Token usage         | High   | Medium | ~30% savings   |

---

## Risk Assessment

| Risk                                          | Impact | Mitigation                  |
| --------------------------------------------- | ------ | --------------------------- |
| Native tool calling requires newer LLM models | Medium | Use gpt-4o or gpt-4o-mini   |
| Structured output not supported by all models | Low    | Fallback to regex if needed |
| Streaming through proxies                     | Low    | Add proxy config docs       |

---

## Files Reference

### Backend

- [`TextToSqlAgent.API/Controllers/AgentController.cs`](TextToSqlAgent.API/Controllers/AgentController.cs)
- [`TextToSqlAgent.Application/Pipelines/SimpleQueryPipeline.cs`](TextToSqlAgent.Application/Pipelines/SimpleQueryPipeline.cs)
- [`TextToSqlAgent.Application/Pipelines/MediumQueryPipeline.cs`](TextToSqlAgent.Application/Pipelines/MediumQueryPipeline.cs)
- [`TextToSqlAgent.Infrastructure/Agent/ReActAgent.cs`](TextToSqlAgent.Infrastructure/Agent/ReActAgent.cs)
- [`TextToSqlAgent.Infrastructure/LLM/`](TextToSqlAgent.Infrastructure/LLM/)

### Frontend

- [`frontend/src/hooks/useAgentStream.js`](frontend/src/hooks/useAgentStream.js)
- [`frontend/src/api/agent/clarification.js`](frontend/src/api/agent/clarification.js)

### Documentation

- [`docs/SYSTEM_OVERVIEW_API.md`](docs/SYSTEM_OVERVIEW_API.md)
- [`docs/API.md`](docs/API.md)
