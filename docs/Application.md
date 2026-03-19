# TextToSqlAgent.Application

Application layer containing business logic, orchestration, and query processing pipelines.

## Overview

This layer contains the core agent orchestration, adapters for various services, and the query processing pipeline. It bridges the Core interfaces with Infrastructure implementations.

## Project Structure

```
TextToSqlAgent.Application/
├── Adapters/
│   ├── ConversationStoreAdapter.cs   # Conversation storage
│   ├── IntentAnalyzerAdapter.cs     # Intent analysis
│   ├── QueryValidatorAdapter.cs     # Query validation
│   ├── ResultFormatterAdapter.cs    # Result formatting
│   ├── SchemaProviderAdapter.cs     # Schema provider
│   ├── SchemaRetrieverAdapter.cs    # Schema retrieval
│   ├── SqlCorrectorAdapter.cs       # SQL correction
│   ├── SqlExecutorAdapter.cs       # SQL execution
│   └── SqlGeneratorAdapter.cs      # SQL generation
├── Pipelines/
│   └── QueryPipeline.cs            # Query processing pipeline
├── Routing/
│   ├── IQueryRouter.cs            # Query router interface
│   └── QueryRouter.cs             # Query router implementation
├── Services/
│   ├── AgentOrchestrator.cs       # Core orchestrator implementation
│   ├── ConversationAwareOrchestrator.cs # Orchestrator for multi-turn conversations
│   ├── ConversationManager.cs     # Conversation management
│   ├── EnhancedAgentOrchestrator.cs # Enhanced ReAct orchestrator
│   ├── FastPathQueryRouter.cs     # Fast path routing
│   ├── HumanInTheLoopOrchestrator.cs # Human-in-the-loop verification
│   ├── IAgentOrchestrator.cs      # Base interface for orchestrators
│   ├── LazyAgentServiceFactory.cs # Lazy service factory
│   ├── RuleBasedSuggestionService.cs # Rule-based query suggestions
│   └── TextToSqlAgentOrchestrator.cs # Main orchestrator
└── TextToSqlAgent.Application.csproj
```

## File Roles

### Orchestrators (Services)

| File                                                                                                 | Responsibility                                                                                                                                                                                                         |
| ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`AgentOrchestrator.cs`](TextToSqlAgent.Application/Services/AgentOrchestrator.cs)                   | Core orchestrator implementation managing standard query flows.                                                                                                                                                        |
| [`ConversationAwareOrchestrator.cs`](TextToSqlAgent.Application/Services/ConversationAwareOrchestrator.cs) | Specialized orchestrator managing stateful, multi-turn context-aware interactions.                                                                                                                                     |
| [`EnhancedAgentOrchestrator.cs`](TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs)   | Advanced orchestrator with ReAct pattern. Features: lazy loading, query validation, SQL explanation, intelligent error handling.                                                                                       |
| [`HumanInTheLoopOrchestrator.cs`](TextToSqlAgent.Application/Services/HumanInTheLoopOrchestrator.cs) | Orchestrator implementing HITL steps (e.g. clarification and confirmation requests) before execution.                                                                                                                  |
| [`IAgentOrchestrator.cs`](TextToSqlAgent.Application/Services/IAgentOrchestrator.cs)                 | Base contract interface for all orchestrator types within the system.                                                                                                                                                  |
| [`RuleBasedSuggestionService.cs`](TextToSqlAgent.Application/Services/RuleBasedSuggestionService.cs) | Evaluates and provides rule-based prompt and query suggestions.                                                                                                                                                        |
| [`TextToSqlAgentOrchestrator.cs`](TextToSqlAgent.Application/Services/TextToSqlAgentOrchestrator.cs) | Legacy orchestrator. Processes queries through validation → RAG → SQL generation → execution → formatting.                                                                                                             |
| [`FastPathQueryRouter.cs`](TextToSqlAgent.Application/Services/FastPathQueryRouter.cs)               | Optimized routing for simple queries. Bypasses full agent for known patterns.                                                                                                                                          |
| [`LazyAgentServiceFactory.cs`](TextToSqlAgent.Application/Services/LazyAgentServiceFactory.cs)       | Factory for lazy service initialization. Enables fast startup by deferring expensive operations.                                                                                                                       |
| [`ConversationManager.cs`](TextToSqlAgent.Application/Services/ConversationManager.cs)               | Manages multi-turn conversations. Stores and retrieves conversation history.                                                                                                                                           |

### Pipeline

| File                                                                        | Responsibility                                                                                                                                                                                                                            |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`QueryPipeline.cs`](TextToSqlAgent.Application/Pipelines/QueryPipeline.cs) | **Thin orchestrator** following Phase 2 clean architecture. Delegates to ports with no business logic. Processes queries through: validation → intent analysis → schema retrieval → SQL generation → execution → correction → formatting. |

### Adapters

| File                                                                                             | Responsibility                                                        |
| ------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------- |
| [`ConversationStoreAdapter.cs`](TextToSqlAgent.Application/Adapters/ConversationStoreAdapter.cs) | Implements `IConversationStore` for conversation persistence.         |
| [`IntentAnalyzerAdapter.cs`](TextToSqlAgent.Application/Adapters/IntentAnalyzerAdapter.cs)       | Implements `IIntentAnalyzer` for query intent classification.         |
| [`QueryValidatorAdapter.cs`](TextToSqlAgent.Application/Adapters/QueryValidatorAdapter.cs)       | Implements `IQueryValidator` for input validation.                    |
| [`ResultFormatterAdapter.cs`](TextToSqlAgent.Application/Adapters/ResultFormatterAdapter.cs)     | Implements `IResultFormatter` for formatting query results.           |
| [`SchemaProviderAdapter.cs`](TextToSqlAgent.Application/Adapters/SchemaProviderAdapter.cs)       | Implements `ISchemaProvider` for schema information.                  |
| [`SchemaRetrieverAdapter.cs`](TextToSqlAgent.Application/Adapters/SchemaRetrieverAdapter.cs)     | Implements `ISchemaRetriever` for schema retrieval from vector store. |
| [`SqlCorrectorAdapter.cs`](TextToSqlAgent.Application/Adapters/SqlCorrectorAdapter.cs)           | Implements `ISqlCorrector` for SQL error correction.                  |
| [`SqlExecutorAdapter.cs`](TextToSqlAgent.Application/Adapters/SqlExecutorAdapter.cs)             | Implements `ISqlExecutor` for SQL execution.                          |
| [`SqlGeneratorAdapter.cs`](TextToSqlAgent.Application/Adapters/SqlGeneratorAdapter.cs)           | Implements `ISqlGenerator` for LLM-powered SQL generation.            |

### Routing

| File                                                                    | Responsibility                                                   |
| ----------------------------------------------------------------------- | ---------------------------------------------------------------- |
| [`IQueryRouter.cs`](TextToSqlAgent.Application/Routing/IQueryRouter.cs) | Interface for query routing strategy.                            |
| [`QueryRouter.cs`](TextToSqlAgent.Application/Routing/QueryRouter.cs)   | Routes queries to appropriate handler (fast path vs full agent). |

## Query Processing Flow

```
User Query
    ↓
Query Validation (QueryValidatorAdapter)
    ↓
Intent Analysis (IntentAnalyzerAdapter)
    ↓
Schema Retrieval (SchemaRetrieverAdapter)
    ↓
SQL Generation (SqlGeneratorAdapter)
    ↓
SQL Execution (SqlExecutorAdapter)
    ↓
SQL Correction (SqlCorrectorAdapter) [if needed]
    ↓
Result Formatting (ResultFormatterAdapter)
    ↓
Response
```

## Usage

```csharp
var orchestrator = serviceProvider.GetRequiredService<EnhancedAgentOrchestrator>();
var result = await orchestrator.ProcessQueryAsync("Show me all customers from Hanoi");
```

## Dependencies

- `TextToSqlAgent.Core` - Domain interfaces and models
- `TextToSqlAgent.Infrastructure` - External service implementations
- `TextToSqlAgent.Plugins` - Plugin system
