# Design Decisions & Trade-offs

## 1. Clean Architecture Pattern

### Decision
Áp dụng Clean Architecture với 4 layers: API → Application → Core → Infrastructure

### Rationale
- **Separation of Concerns**: Mỗi layer có trách nhiệm rõ ràng
- **Testability**: Core business logic không phụ thuộc vào infrastructure
- **Flexibility**: Dễ dàng thay đổi LLM provider, database, vector store
- **Maintainability**: Code dễ đọc, dễ maintain

### Trade-offs
**Pros**:
- ✅ High testability
- ✅ Easy to swap implementations
- ✅ Clear dependency direction (inward)
- ✅ Business logic isolated from infrastructure

**Cons**:
- ❌ More boilerplate code (interfaces, DTOs)
- ❌ Steeper learning curve for new developers
- ❌ More files and folders to navigate

### Alternative Considered
**Vertical Slice Architecture**: Organize by features instead of layers

**Why Not Chosen**: 
- Harder to enforce consistency across features
- More code duplication
- Less suitable for shared infrastructure (LLM, Qdrant)

---

## 2. Pipeline-Based Query Processing

### Decision
Route queries to different pipelines based on complexity: Simple → Medium → Complex

### Rationale
- **Performance**: Simple queries don't need full ReAct loop
- **Cost**: Fewer LLM calls for simple queries
- **User Experience**: Faster response for common queries
- **Scalability**: Can optimize each pipeline independently

### Implementation
```
Simple Pipeline (70% queries):
- 2-3 LLM calls
- 3-5 seconds
- Single table, no joins

Medium Pipeline (25% queries):
- 4-6 LLM calls
- 10-15 seconds
- Multiple tables, basic joins

Complex Pipeline (5% queries):
- 8-12 LLM calls
- 30-60 seconds
- Subqueries, analytics, ReAct loop
```

### Trade-offs
**Pros**:
- ✅ Optimized performance per complexity level
- ✅ Lower cost (fewer LLM calls for simple queries)
- ✅ Auto-escalation on failure

**Cons**:
- ❌ Classification can be wrong (need fallback)
- ❌ More code to maintain (3 pipelines)
- ❌ Complexity threshold tuning required

### Alternative Considered
**Single Pipeline**: All queries go through same flow

**Why Not Chosen**:
- Slower for simple queries
- Higher LLM costs
- Worse user experience

---

## 3. Hybrid RAG Strategy

### Decision
Combine vector search + keyword matching + graph traversal with weighted scoring

### Rationale
- **Recall**: Vector search alone misses exact matches
- **Precision**: Keyword matching alone misses semantic similarity
- **Completeness**: Graph traversal finds related tables via foreign keys

### Weights
```
Vector Search:    50% (semantic similarity)
Keyword Matching: 30% (exact matches)
Graph Traversal:  20% (related tables)
```

### Trade-offs
**Pros**:
- ✅ Better recall and precision
- ✅ Handles both semantic and exact queries
- ✅ Finds related tables automatically

**Cons**:
- ❌ More complex implementation
- ❌ Slower than single strategy
- ❌ Weight tuning required

### Alternative Considered
**Vector Search Only**: Simpler, faster

**Why Not Chosen**:
- Misses exact table name matches
- Requires perfect embeddings
- No relationship awareness

---

## 4. Intent-Based Routing

### Decision
Classify intent (QUERY/WRITE/DDL/FORBIDDEN) before processing

### Rationale
- **Safety**: Prevent accidental data modifications
- **User Confirmation**: Require explicit confirmation for writes
- **Specialized Handling**: Different pipelines for different intents
- **Audit Trail**: Track all write/DDL operations

### Implementation
```
Intent Classification → Route to Pipeline:
- SELECT → Query Pipeline (auto-execute)
- INSERT/UPDATE/DELETE → Write Pipeline (preview + confirm)
- CREATE/ALTER/DROP → DDL Pipeline (preview + confirm)
- Dangerous operations → Forbidden Pipeline (reject)
```

### Trade-offs
**Pros**:
- ✅ Prevents accidental data loss
- ✅ Better user experience (preview before execute)
- ✅ Audit trail for compliance
- ✅ Specialized error handling per intent

**Cons**:
- ❌ Extra LLM call for classification
- ❌ Classification can be wrong (need high confidence threshold)
- ❌ More complex routing logic

### Alternative Considered
**No Intent Classification**: Execute all queries directly

**Why Not Chosen**:
- Dangerous (accidental DELETE/DROP)
- No preview for writes
- Poor audit trail

---

## 5. Self-Correction with Max 3 Attempts

### Decision
Automatically retry failed SQL with LLM-based correction, max 3 attempts

### Rationale
- **Reliability**: Handle LLM mistakes automatically
- **User Experience**: No manual retry needed
- **Success Rate**: Increases from 85% to 95%+

### Implementation
```
Attempt 1: Execute generated SQL
  ↓ (if error)
Attempt 2: Self-correct with error message
  ↓ (if error)
Attempt 3: Self-correct with more context
  ↓ (if error)
Return error to user
```

### Trade-offs
**Pros**:
- ✅ Higher success rate (95%+)
- ✅ Better user experience
- ✅ Handles common LLM mistakes (typos, wrong table names)

**Cons**:
- ❌ Slower on errors (3x LLM calls)
- ❌ Higher cost on errors
- ❌ Can still fail after 3 attempts

### Alternative Considered
**No Self-Correction**: Return error immediately

**Why Not Chosen**:
- Lower success rate (85%)
- Poor user experience
- More manual retries

---

## 6. Dual LLM Provider Support

### Decision
Support both OpenAI and Google Gemini with factory pattern

### Rationale
- **Flexibility**: Switch providers based on cost, performance, availability
- **Redundancy**: Fallback if one provider is down
- **Cost Optimization**: Use cheaper provider for simple tasks

### Implementation
```csharp
public ILLMClient CreateClient()
{
    return _llmProvider switch
    {
        "openai" => new OpenAIClient(...),
        "gemini" => new GeminiClient(...),
        _ => throw new ArgumentException($"Unknown provider: {_llmProvider}")
    };
}
```

### Trade-offs
**Pros**:
- ✅ Vendor independence
- ✅ Cost optimization
- ✅ Redundancy

**Cons**:
- ❌ Different embedding dimensions (768 vs 1536)
- ❌ Different prompt formats
- ❌ More testing required

### Alternative Considered
**Single Provider**: OpenAI only

**Why Not Chosen**:
- Vendor lock-in
- No fallback option
- Higher cost

---

## 7. Semantic Kernel for LLM Integration

### Decision
Use Microsoft Semantic Kernel instead of direct API calls

### Rationale
- **Abstraction**: Unified interface for multiple LLM providers
- **Features**: Built-in retry, timeout, token counting
- **Plugins**: Easy to add custom functions
- **Maintenance**: Microsoft-maintained library

### Trade-offs
**Pros**:
- ✅ Unified interface
- ✅ Built-in features (retry, timeout)
- ✅ Well-documented
- ✅ Active development

**Cons**:
- ❌ Extra dependency
- ❌ Learning curve
- ❌ Some features not needed

### Alternative Considered
**Direct API Calls**: Use HttpClient directly

**Why Not Chosen**:
- More boilerplate code
- Need to implement retry logic
- Harder to switch providers

---

## 8. Qdrant for Vector Storage

### Decision
Use Qdrant instead of other vector databases (Pinecone, Weaviate, Milvus)

### Rationale
- **Open Source**: Self-hosted, no vendor lock-in
- **Performance**: Fast cosine similarity search
- **Features**: Filtering, payload storage, HNSW index
- **Docker Support**: Easy deployment

### Trade-offs
**Pros**:
- ✅ Open source (free)
- ✅ Self-hosted (data privacy)
- ✅ Fast search (< 500ms)
- ✅ Easy deployment (Docker)

**Cons**:
- ❌ Need to manage infrastructure
- ❌ No managed service (yet)
- ❌ Smaller community than Pinecone

### Alternative Considered
**Pinecone**: Managed vector database

**Why Not Chosen**:
- Expensive ($70+/month)
- Vendor lock-in
- Data privacy concerns

---

## 9. In-Memory Fallback for Qdrant

### Decision
Implement in-memory vector store as fallback when Qdrant unavailable

### Rationale
- **Resilience**: System continues working even if Qdrant is down
- **Development**: Can develop without Qdrant running
- **Testing**: Easier to test without external dependencies

### Implementation
```csharp
try
{
    return await _qdrantService.SearchAsync(...);
}
catch (VectorDBException)
{
    _logger.LogWarning("Qdrant unavailable, using in-memory fallback");
    return await _inMemoryStore.SearchAsync(...);
}
```

### Trade-offs
**Pros**:
- ✅ High availability
- ✅ Easier development
- ✅ Graceful degradation

**Cons**:
- ❌ Slower search (brute-force)
- ❌ No persistence (data lost on restart)
- ❌ Memory usage for large schemas

### Alternative Considered
**No Fallback**: Fail if Qdrant unavailable

**Why Not Chosen**:
- Poor availability
- Bad user experience
- Harder to develop/test

---

## 10. JWT with Refresh Token Rotation

### Decision
Use JWT for authentication with refresh token rotation

### Rationale
- **Security**: Short-lived access tokens (15 minutes)
- **UX**: Long-lived refresh tokens (7 days)
- **Revocation**: Can revoke refresh tokens
- **Stateless**: No server-side session storage

### Implementation
```
Access Token:  15 minutes (stateless)
Refresh Token: 7 days (stored in database)
Rotation:      New refresh token on each refresh
```

### Trade-offs
**Pros**:
- ✅ Secure (short-lived access tokens)
- ✅ Good UX (auto-refresh)
- ✅ Revocable (refresh tokens in DB)
- ✅ Stateless (no session storage)

**Cons**:
- ❌ More complex than simple JWT
- ❌ Database queries for refresh
- ❌ Token rotation overhead

### Alternative Considered
**Simple JWT**: Long-lived tokens only

**Why Not Chosen**:
- Less secure (can't revoke)
- Worse if token leaked
- No rotation

---

## 11. Conversation Context Management

### Decision
Store conversation history in database and enrich queries with context

### Rationale
- **Multi-turn Support**: Handle follow-up questions
- **Pronoun Resolution**: "Show me their orders" → "Show me Customer X's orders"
- **Context Awareness**: Understand references to previous queries
- **Better UX**: Natural conversation flow

### Implementation
```csharp
// Extract context from previous turn
var lastTurn = context.History.Last();
var primaryEntity = lastTurn.PrimaryEntity;  // e.g., "Customers"

// Enrich current question
if (ContainsPronouns(userQuestion))
{
    enrichedQuestion = ResolvePronouns(userQuestion, primaryEntity);
}
```

### Trade-offs
**Pros**:
- ✅ Natural conversation flow
- ✅ Handles pronouns and references
- ✅ Better user experience
- ✅ Context-aware SQL generation

**Cons**:
- ❌ More complex implementation
- ❌ Database queries for history
- ❌ Context can be wrong (need confidence threshold)

### Alternative Considered
**Stateless**: Each query independent

**Why Not Chosen**:
- Poor UX (can't handle follow-ups)
- No pronoun resolution
- Unnatural conversation

---

## 12. Pagination for Large Results

### Decision
Automatically paginate results > 100 rows, cache in Redis

### Rationale
- **Performance**: Don't send 10,000 rows to frontend
- **UX**: Progressive loading
- **Memory**: Reduce memory usage
- **Network**: Reduce bandwidth

### Implementation
```
Query returns > 100 rows:
1. Execute full query
2. Cache result in Redis (15 min TTL)
3. Return first page (50 rows)
4. Frontend requests more pages as needed
```

### Trade-offs
**Pros**:
- ✅ Better performance
- ✅ Lower memory usage
- ✅ Better UX (progressive loading)
- ✅ Reduced network traffic

**Cons**:
- ❌ More complex implementation
- ❌ Redis dependency
- ❌ Cache invalidation complexity

### Alternative Considered
**No Pagination**: Return all rows

**Why Not Chosen**:
- Poor performance for large results
- High memory usage
- Bad UX (long wait time)

---

## 13. Rate Limiting Strategy

### Decision
Token bucket algorithm with per-user limits

### Rationale
- **Cost Control**: Prevent LLM API cost explosion
- **Abuse Prevention**: Prevent system abuse
- **Fair Usage**: Ensure fair resource allocation
- **Burst Support**: Allow short bursts

### Configuration
```
Capacity:    10 requests
Refill Rate: 1 request per second
Burst:       10 requests in 1 second
Daily Limit: 100 requests per user
```

### Trade-offs
**Pros**:
- ✅ Cost control
- ✅ Abuse prevention
- ✅ Fair usage
- ✅ Burst support

**Cons**:
- ❌ Can frustrate power users
- ❌ Need to tune limits
- ❌ More complex implementation

### Alternative Considered
**No Rate Limiting**: Unlimited requests

**Why Not Chosen**:
- Uncontrolled costs
- Vulnerable to abuse
- Unfair resource allocation

---

## 14. Structured Logging with Serilog

### Decision
Use Serilog for structured logging instead of built-in ILogger

### Rationale
- **Structured**: Log as JSON objects, not strings
- **Searchable**: Easy to query logs
- **Enrichment**: Add context (correlation ID, user ID)
- **Sinks**: Multiple outputs (console, file, Application Insights)

### Trade-offs
**Pros**:
- ✅ Structured logging
- ✅ Easy to search
- ✅ Rich context
- ✅ Multiple sinks

**Cons**:
- ❌ Extra dependency
- ❌ Learning curve
- ❌ More configuration

### Alternative Considered
**Built-in ILogger**: Use ASP.NET Core logging

**Why Not Chosen**:
- Less structured
- Harder to search
- Limited enrichment

---

## 15. Docker Deployment

### Decision
Deploy using Docker Compose with 3 services: API, Frontend, Qdrant

### Rationale
- **Consistency**: Same environment everywhere
- **Isolation**: Services isolated from host
- **Portability**: Easy to deploy anywhere
- **Scalability**: Easy to scale horizontally

### Trade-offs
**Pros**:
- ✅ Consistent environment
- ✅ Easy deployment
- ✅ Portable
- ✅ Scalable

**Cons**:
- ❌ Docker overhead
- ❌ More complex debugging
- ❌ Need Docker knowledge

### Alternative Considered
**Native Deployment**: Deploy directly on host

**Why Not Chosen**:
- Environment inconsistencies
- Harder to scale
- Dependency conflicts

---

## Summary of Key Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| Clean Architecture | Testability, Flexibility | Boilerplate code |
| Pipeline-Based | Performance, Cost | Complexity |
| Hybrid RAG | Better accuracy | Slower, more complex |
| Intent Routing | Safety, UX | Extra LLM call |
| Self-Correction | Higher success rate | Slower on errors |
| Dual LLM Provider | Flexibility, Redundancy | Different dimensions |
| Semantic Kernel | Abstraction, Features | Extra dependency |
| Qdrant | Open source, Fast | Self-hosted |
| In-Memory Fallback | High availability | Slower, no persistence |
| JWT + Refresh | Security, UX | Complexity |
| Conversation Context | Natural UX | Complexity |
| Pagination | Performance, UX | Redis dependency |
| Rate Limiting | Cost control | User friction |
| Structured Logging | Searchability | Extra dependency |
| Docker | Consistency, Portability | Overhead |

---

## Lessons Learned

### What Worked Well
1. **Clean Architecture**: Easy to test and maintain
2. **Pipeline-Based Processing**: Significant performance improvement
3. **Self-Correction**: Dramatically improved success rate
4. **Hybrid RAG**: Better accuracy than single strategy
5. **Intent Routing**: Prevented many accidental data modifications

### What Could Be Improved
1. **God Classes**: Some orchestrators too large (1700+ lines)
2. **Test Coverage**: Need more comprehensive tests
3. **Monitoring**: Need better observability
4. **Documentation**: Need more inline documentation
5. **Error Messages**: Need more user-friendly messages

### What We'd Do Differently
1. **Start with Tests**: TDD from the beginning
2. **Smaller Classes**: Enforce max 500 lines per class
3. **More Interfaces**: Better abstraction from the start
4. **Better Logging**: Structured logging from day 1
5. **Monitoring First**: Set up monitoring before production
