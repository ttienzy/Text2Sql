# System Architecture Overview

## Tổng quan hệ thống

Text-to-SQL Agent là một hệ thống AI-powered chuyển đổi ngôn ngữ tự nhiên thành SQL queries, được xây dựng theo Clean Architecture với khả năng xử lý multi-turn conversations và tự động sửa lỗi.

## Các Layer chính

### 1. API Layer (TextToSqlAgent.API)
- **Vai trò**: Entry point cho tất cả requests, xử lý authentication, middleware
- **Controllers chính**:
  - `AgentController` - Xử lý query processing với EnhancedAgentOrchestrator
  - `ConversationAwareAgentController` - Multi-turn conversation support
  - `ConnectionsController` - Quản lý database connections
  - `DbExplorerController` - Schema exploration và analysis
  - `DDLOperationController` - DDL operations (CREATE/ALTER/DROP)
  - `WriteOperationController` - DML operations (INSERT/UPDATE/DELETE)
  - `AuthController` - Authentication và authorization

### 2. Application Layer (TextToSqlAgent.Application)
- **Vai trò**: Business logic, orchestration, pipelines
- **Components chính**:
  - **Orchestrators**:
    - `EnhancedAgentOrchestrator` - Main orchestrator với intent routing
    - `AgentOrchestrator` - Query complexity-based routing
    - `ConversationAwareOrchestrator` - Multi-turn conversation context
  - **Pipelines**:
    - `SimpleQueryPipeline` - Single table, no joins (70% queries, 3-5s)
    - `MediumQueryPipeline` - Multiple tables, basic joins (25% queries, 10-15s)
    - `ComplexQueryPipeline` - Subqueries, analytics (5% queries, 30-60s)
    - `WriteOperationPipeline` - INSERT/UPDATE operations
    - `DDLPipeline` - Schema modifications
  - **Routing**:
    - `QueryClassifier` - Rule-based + LLM fallback classification
    - `IntentClassifier` - Detects SELECT/INSERT/UPDATE/DELETE/CREATE/ALTER/DROP/FORBIDDEN

### 3. Core Layer (TextToSqlAgent.Core)
- **Vai trò**: Domain models, interfaces, business rules
- **Components**:
  - Domain models: `DatabaseSchema`, `QueryResult`, `AgentRequest`, `AgentResponse`
  - Interfaces: `ILLMClient`, `IAgent`, `ISqlExecutor`, `IVectorStore`
  - Exceptions: `AgentException`, `LLMApiException`, `VectorDBException`

### 4. Infrastructure Layer (TextToSqlAgent.Infrastructure)
- **Vai trò**: External integrations, persistence, caching
- **Components chính**:
  - **LLM Integration**:
    - `GeminiClient` - Google Gemini API client
    - `OpenAIClient` - OpenAI GPT-4o client
    - `LLMClientFactory` - Factory pattern cho provider selection
  - **Vector Database**:
    - `QdrantService` - Qdrant REST API client
    - `InMemoryVectorStore` - Fallback khi Qdrant unavailable
  - **RAG System**:
    - `SchemaRetriever` - Hybrid search (vector + keyword + graph)
    - `SchemaIndexer` - Embedding generation và storage
    - `KeywordSchemaRetriever` - Keyword-based matching
  - **Error Handling**:
    - `BaseErrorHandler` - Retry strategies (immediate, exponential, circuit breaker)
    - `LLMErrorHandler` - Rate limit, quota handling
    - `SqlErrorHandler` - Invalid column/table detection
    - `ConnectionErrorHandler` - Connection pooling, circuit breaker
    - `VectorDBErrorHandler` - Fallback to in-memory
  - **Database**:
    - `AppDbContext` - EF Core context
    - `SqlServerAdapter` - SQL Server specific operations
  - **Agent System**:
    - `ReActAgent` - Reasoning + Acting loop
    - `ReasoningEngine` - LLM-based thinking
    - `ReflectionEngine` - Self-reflection on results

### 5. Frontend (React 19 + Vite)
- **Tech Stack**: React 19, Vite, Ant Design, Zustand, React Query
- **Components**:
  - State management: `authStore`, `connectionStore`, `conversationStore`
  - API client: Axios với interceptors (token refresh, error handling)
  - UI: Ant Design components với responsive design

## Data Flow: User Query → SQL Execution

```
1. User Input (Frontend)
   ↓
2. API Controller (AgentController.ProcessMessage)
   ↓
3. EnhancedAgentOrchestrator
   ├─ Step -1: Intent Classification (QUERY/WRITE/DDL/FORBIDDEN)
   ├─ Step 0: Query Validation (relevance check)
   ├─ Step 0.5: Conversation Context Enrichment
   ├─ Step 1: Load Schema from Cache
   ├─ Step 2: Normalize Prompt
   ├─ Step 3: Setup Qdrant Collection
   ├─ Step 4: RAG - Retrieve Relevant Schema
   │   ├─ Vector Search (Qdrant)
   │   ├─ Keyword Matching
   │   └─ Graph Traversal
   ├─ Step 5: Intent Analysis
   ├─ Step 6: Generate SQL (LLM)
   ├─ Step 7: Validate SQL Safety
   ├─ Step 8: Explain Query (optional)
   ├─ Step 9: Execute with Self-Correction
   │   ├─ Execute SQL
   │   ├─ If error → Self-correct (max 3 attempts)
   │   └─ Return result
   ├─ Step 10: Pagination (if > 100 rows)
   ├─ Step 11: Format Answer (LLM)
   └─ Step 12: Generate Suggested Queries
   ↓
4. Response to Frontend
   ├─ SQL query
   ├─ Execution result (paginated)
   ├─ Natural language answer
   ├─ Suggested follow-up queries
   └─ Processing steps
```

## External Dependencies

### 1. LLM Providers
- **Google Gemini** (primary):
  - Model: `gemini-2.5-flash`
  - Embedding: `gemini-embedding-1.0`
  - Use case: SQL generation, intent analysis, query explanation
- **OpenAI** (alternative):
  - Model: `gpt-4o`
  - Embedding: `text-embedding-3-small`
  - Use case: Same as Gemini, configurable via `LLMProvider` setting

### 2. Vector Database
- **Qdrant**:
  - Version: Latest
  - Ports: 6333 (REST), 6334 (gRPC)
  - Use case: Schema embedding storage, semantic search
  - Fallback: In-memory vector store

### 3. SQL Database
- **SQL Server 2022**:
  - Primary database for application data
  - Target database for user queries
  - Connection pooling enabled

### 4. Caching
- **Memory Cache**:
  - Query embeddings (1000 entries, 60min TTL)
  - Schema cache
- **Redis** (optional):
  - Query result pagination
  - DB explorer caching
  - Session management

### 5. Authentication
- **JWT** with refresh token rotation
- **ASP.NET Core Identity** for user management

## Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                         Frontend                             │
│  React 19 + Vite + Ant Design + Zustand                    │
└────────────────────┬────────────────────────────────────────┘
                     │ HTTP/REST
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                      API Layer                               │
│  Controllers + Middleware + Authentication                   │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                  Application Layer                           │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  EnhancedAgentOrchestrator (Intent Routing)          │  │
│  └──────────────────┬───────────────────────────────────┘  │
│                     │                                        │
│     ┌───────────────┼───────────────┬──────────────┐       │
│     ↓               ↓                ↓              ↓       │
│  ┌─────┐      ┌─────────┐     ┌─────────┐    ┌─────────┐ │
│  │Query│      │  Write  │     │   DDL   │    │Forbidden│ │
│  │ P/L │      │Pipeline │     │Pipeline │    │Pipeline │ │
│  └─────┘      └─────────┘     └─────────┘    └─────────┘ │
│     │                                                        │
│     ├─ SimpleQueryPipeline (70%)                           │
│     ├─ MediumQueryPipeline (25%)                           │
│     └─ ComplexQueryPipeline (5%)                           │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                Infrastructure Layer                          │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │   LLM    │  │  Qdrant  │  │   RAG    │  │  Error   │  │
│  │  Client  │  │  Service │  │  System  │  │ Handlers │  │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │   SQL    │  │  Cache   │  │  Agent   │  │Database  │  │
│  │ Executor │  │  System  │  │  System  │  │ Adapters │  │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘  │
└────────────────────┬────────────────────────────────────────┘
                     │
        ┌────────────┼────────────┬──────────────┐
        ↓            ↓            ↓              ↓
   ┌────────┐  ┌─────────┐  ┌─────────┐   ┌──────────┐
   │ Gemini │  │ Qdrant  │  │  Redis  │   │SQL Server│
   │   API  │  │ Vector  │  │  Cache  │   │    DB    │
   └────────┘  │   DB    │  └─────────┘   └──────────┘
               └─────────┘
```

## Giai đoạn phát triển

**Hiện tại: Production-Ready MVP với một số gaps**

Hệ thống đã có đầy đủ tính năng core cho production:
- ✅ Multi-turn conversation support với context enrichment
- ✅ Intent-based routing (QUERY/WRITE/DDL/FORBIDDEN)
- ✅ Self-correction với max 3 attempts
- ✅ Comprehensive error handling với retry strategies
- ✅ RAG với hybrid search (vector + keyword + graph)
- ✅ Query result pagination với Redis caching
- ✅ JWT authentication với refresh token rotation
- ✅ Structured logging với correlation ID
- ✅ Docker deployment support

**Critical Gaps (cần fix trước production)**:
- ⚠️ Test coverage thấp (20-30%) - cần tăng lên 80%+
- ⚠️ Rate limiting disabled by default - cần enable
- ⚠️ Schema auto-sync disabled - cần fix connection issues
- ⚠️ Không có monitoring/alerting system
- ⚠️ Connection strings không encrypted

**Nice-to-have (có thể defer)**:
- Multi-database support (PostgreSQL, MySQL)
- Advanced analytics và reporting
- Query plan caching
- Streaming responses

## Key Metrics

- **Query Processing Time**:
  - Simple: 3-5 seconds (target)
  - Medium: 10-15 seconds (target)
  - Complex: 30-60 seconds (target)
- **LLM Calls per Request**:
  - Simple: 2-3 calls
  - Medium: 4-6 calls
  - Complex: 8-12 calls
- **Success Rate**: 95%+ (with self-correction)
- **Schema Retrieval**: < 1 second (cached)
- **Vector Search**: < 500ms (Qdrant)
