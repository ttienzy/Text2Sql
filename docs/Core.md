# TextToSqlAgent.Core

Core domain layer containing domain models, interfaces, enums, and agent-related abstractions.

## Overview

This is the central layer containing all domain models, interfaces (contracts), and core abstractions. **No external dependencies** on other project layers - this is the domain boundary.

## Project Structure

```
TextToSqlAgent.Core/
├── Agent/
│   ├── AgentAction.cs           # Agent action definition
│   ├── AgentContext.cs         # Agent execution context
│   ├── AgentObservation.cs     # Agent observation
│   ├── AgentReflection.cs      # Agent reflection
│   ├── AgentRequest.cs         # Agent request model
│   ├── AgentResult.cs          # Agent result model
│   ├── AgentState.cs           # Agent state
│   ├── AgentStep.cs            # Agent step
│   ├── AgentStepEvent.cs       # Event streams for frontend real-time updates
│   ├── ClarificationRequest.cs # For user clarification requests
│   ├── ConversationAwareAgentRequest.cs # Specialized request for context
│   ├── IAgent.cs               # Agent interface
│   ├── IReasoningEngine.cs     # Reasoning engine interface
│   └── IReflectionEngine.cs   # Reflection engine interface
├── Enums/
│   └── DatabaseProvider.cs     # Database provider enum
├── Exceptions/
│   └── AgentExceptions.cs      # Domain exceptions
├── Interfaces/
│   ├── IDatabaseAdapter.cs     # Database adapter interface
│   ├── IEmbeddingClient.cs    # Embedding client interface
│   ├── ILLMClient.cs           # LLM client interface
│   ├── IQueryRouter.cs         # Query router interface
│   └── IVectorStore.cs         # Vector store interface
├── Models/
│   ├── AgentResponse.cs        # Agent response
│   ├── ConversationContext.cs  # Conversation context
│   ├── CorrectionAttempt.cs    # SQL correction attempt
│   ├── DatabaseSchema.cs       # Database schema
│   ├── Entity.cs               # Base entity
│   ├── IntentAnalysis.cs       # Intent analysis result
│   ├── NormalizedPrompt.cs     # Normalized prompt
│   ├── QueryContracts.cs       # Query contracts
│   ├── QueryValidationResult.cs # Validation result
│   ├── RetrievedSchemaContext.cs # Retrieved schema context
│   ├── SchemaDocument.cs       # Schema document
│   ├── SqlError.cs             # SQL error
│   ├── SqlExecutionResult.cs   # SQL execution result
│   └── SubQuery.cs             # Sub-query
├── Ports/
│   └── IQueryPorts.cs          # Query port interfaces
├── Tasks/
│   ├── IAgentTask.cs           # Agent task interface
│   └── NormalizePromptTask.cs  # Prompt normalization task
├── Tools/
│   ├── ITool.cs               # Tool interface
│   ├── IToolRegistry.cs       # Tool registry interface
│   ├── ToolInput.cs           # Tool input
│   ├── ToolResult.cs          # Tool result
│   └── ToolSchema.cs          # Tool schema
└── TextToSqlAgent.Core.csproj
```

## File Roles

### Agent (ReAct Pattern)

| File                                                                     | Responsibility                                               |
| ------------------------------------------------------------------------ | ------------------------------------------------------------ |
| [`IAgent.cs`](TextToSqlAgent.Core/Agent/IAgent.cs)                       | Main agent interface. Defines `ExecuteAsync()` method.       |
| [`AgentRequest.cs`](TextToSqlAgent.Core/Agent/AgentRequest.cs)           | Input to agent: question, conversationId, context.           |
| [`AgentResult.cs`](TextToSqlAgent.Core/Agent/AgentResult.cs)             | Output from agent: sql, result, answer, steps.               |
| [`AgentStep.cs`](TextToSqlAgent.Core/Agent/AgentStep.cs)                 | Individual step in ReAct loop: thought, action, observation. |
| [`AgentState.cs`](TextToSqlAgent.Core/Agent/AgentState.cs)               | Current agent state: thinking, acting, observing.            |
| [`AgentContext.cs`](TextToSqlAgent.Core/Agent/AgentContext.cs)           | Execution context: schema, history, config.                  |
| [`AgentAction.cs`](TextToSqlAgent.Core/Agent/AgentAction.cs)             | Action definition: tool name, input, parameters.             |
| [`AgentObservation.cs`](TextToSqlAgent.Core/Agent/AgentObservation.cs)   | Observation from tool execution.                             |
| [`AgentReflection.cs`](TextToSqlAgent.Core/Agent/AgentReflection.cs)     | Reflection for self-correction.                              |
| [`AgentStepEvent.cs`](TextToSqlAgent.Core/Agent/AgentStepEvent.cs)       | Event definition for real-time frontend streaming updates.   |
| [`ClarificationRequest.cs`](TextToSqlAgent.Core/Agent/ClarificationRequest.cs) | Data model handles agent asking user for clarifications.     |
| [`ConversationAwareAgentRequest.cs`](TextToSqlAgent.Core/Agent/ConversationAwareAgentRequest.cs) | Specialized request containing context history and IDs.      |
| [`IReasoningEngine.cs`](TextToSqlAgent.Core/Agent/IReasoningEngine.cs)   | Interface for reasoning engine.                              |
| [`IReflectionEngine.cs`](TextToSqlAgent.Core/Agent/IReflectionEngine.cs) | Interface for reflection engine.                             |

### Interfaces (Ports)

| File                                                                        | Responsibility                                                                    |
| --------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| [`ILLMClient.cs`](TextToSqlAgent.Core/Interfaces/ILLMClient.cs)             | LLM API client interface. Defines `GenerateAsync()` method.                       |
| [`IDatabaseAdapter.cs`](TextToSqlAgent.Core/Interfaces/IDatabaseAdapter.cs) | Database operations interface. Defines `ExecuteQueryAsync()`, `GetSchemaAsync()`. |
| [`IVectorStore.cs`](TextToSqlAgent.Core/Interfaces/IVectorStore.cs)         | Vector storage interface. Defines `UpsertAsync()`, `SearchAsync()`.               |
| [`IEmbeddingClient.cs`](TextToSqlAgent.Core/Interfaces/IEmbeddingClient.cs) | Embedding generation interface. Defines `GenerateEmbeddingAsync()`.               |
| [`IQueryRouter.cs`](TextToSqlAgent.Core/Interfaces/IQueryRouter.cs)         | Query routing interface.                                                          |

### Models

| File                                                                                | Responsibility                                                       |
| ----------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| [`AgentResponse.cs`](TextToSqlAgent.Core/Models/AgentResponse.cs)                   | Response from orchestrator: success, sql, result, message, metadata. |
| [`DatabaseSchema.cs`](TextToSqlAgent.Core/Models/DatabaseSchema.cs)                 | Database schema: tables, columns, relationships, indexes.            |
| [`SqlExecutionResult.cs`](TextToSqlAgent.Core/Models/SqlExecutionResult.cs)         | Result from SQL execution: rows, columns, data.                      |
| [`SqlError.cs`](TextToSqlAgent.Core/Models/SqlError.cs)                             | SQL error information: message, line, column.                        |
| [`IntentAnalysis.cs`](TextToSqlAgent.Core/Models/IntentAnalysis.cs)                 | Query intent: type (SELECT, INSERT, etc.), entities, complexity.     |
| [`QueryValidationResult.cs`](TextToSqlAgent.Core/Models/QueryValidationResult.cs)   | Validation result: isValid, errors, warnings.                        |
| [`ConversationContext.cs`](TextToSqlAgent.Core/Models/ConversationContext.cs)       | Conversation context: sessionId, history, variables.                 |
| [`CorrectionAttempt.cs`](TextToSqlAgent.Core/Models/CorrectionAttempt.cs)           | SQL correction attempt: originalSql, correctedSql, error, success.   |
| [`SchemaDocument.cs`](TextToSqlAgent.Core/Models/SchemaDocument.cs)                 | Schema document for vector store.                                    |
| [`RetrievedSchemaContext.cs`](TextToSqlAgent.Core/Models/RetrievedSchemaContext.cs) | Retrieved schema context from RAG.                                   |
| [`NormalizedPrompt.cs`](TextToSqlAgent.Core/Models/NormalizedPrompt.cs)             | Normalized prompt for LLM.                                           |
| [`QueryContracts.cs`](TextToSqlAgent.Core/Models/QueryContracts.cs)                 | Query contracts for pipeline.                                        |
| [`Entity.cs`](TextToSqlAgent.Core/Models/Entity.cs)                                 | Base entity class.                                                   |
| [`SubQuery.cs`](TextToSqlAgent.Core/Models/SubQuery.cs)                             | Sub-query definition.                                                |

### Enums

| File                                                                   | Responsibility                                             |
| ---------------------------------------------------------------------- | ---------------------------------------------------------- |
| [`DatabaseProvider.cs`](TextToSqlAgent.Core/Enums/DatabaseProvider.cs) | Supported databases: SqlServer, PostgreSQL, MySQL, SQLite. |

### Exceptions

| File                                                                      | Responsibility                                                              |
| ------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| [`AgentExceptions.cs`](TextToSqlAgent.Core/Exceptions/AgentExceptions.cs) | Domain exceptions: AgentException, ValidationException, ExecutionException. |

### Ports

| File                                                         | Responsibility                                                                 |
| ------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| [`IQueryPorts.cs`](TextToSqlAgent.Core/Ports/IQueryPorts.cs) | Query port interfaces: IQueryValidator, IIntentAnalyzer, ISchemaProvider, etc. |

### Tasks

| File                                                                         | Responsibility                            |
| ---------------------------------------------------------------------------- | ----------------------------------------- |
| [`IAgentTask.cs`](TextToSqlAgent.Core/Tasks/IAgentTask.cs)                   | Agent task interface.                     |
| [`NormalizePromptTask.cs`](TextToSqlAgent.Core/Tasks/NormalizePromptTask.cs) | Prompt normalization task implementation. |

### Tools

| File                                                             | Responsibility                  |
| ---------------------------------------------------------------- | ------------------------------- |
| [`ITool.cs`](TextToSqlAgent.Core/Tools/ITool.cs)                 | Tool interface for ReAct agent. |
| [`IToolRegistry.cs`](TextToSqlAgent.Core/Tools/IToolRegistry.cs) | Tool registry interface.        |
| [`ToolInput.cs`](TextToSqlAgent.Core/Tools/ToolInput.cs)         | Tool input data.                |
| [`ToolResult.cs`](TextToSqlAgent.Core/Tools/ToolResult.cs)       | Tool execution result.          |
| [`ToolSchema.cs`](TextToSqlAgent.Core/Tools/ToolSchema.cs)       | Tool schema definition.         |

## Architecture - Clean Architecture

```
┌─────────────────────────────────────────┐
│         Presentation Layer              │
│    (API, Console, Tests)               │
├─────────────────────────────────────────┤
│         Application Layer              │
│    (Orchestrators, Adapters)           │
├─────────────────────────────────────────┤
│           Core Layer                     │
│  (Interfaces, Models, Domain Logic)    │
├─────────────────────────────────────────┤
│        Infrastructure Layer             │
│  (Implementations, External Services)   │
└─────────────────────────────────────────┘
```

## Usage

```csharp
// Use interfaces from Core
public class MyService
{
    public MyService(ILLMClient llmClient, IDatabaseAdapter databaseAdapter)
    {
        // ...
    }
}
```

## Dependencies

This layer has **no dependencies** on other project layers. It contains only:

- C# interfaces (contracts)
- Domain models (POCOs)
- Enums
- Exceptions
