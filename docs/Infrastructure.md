# TextToSqlAgent.Infrastructure

Infrastructure layer containing implementations for external services, database access, caching, and observability.

## Overview

This layer implements the interfaces defined in Core and Application layers. Contains all external service integrations, database access, LLM clients, vector stores, and utilities.

## Project Structure

```
TextToSqlAgent.Infrastructure/
├── Agent/                 # Agent implementations
├── Analysis/             # Query analysis
├── Caching/              # Cache implementations
├── Configuration/        # Configuration models
├── Database/            # Database adapters
├── ErrorHandling/       # Error handling
├── Extensions/          # Extension methods
├── Factories/           # Factory implementations
├── Generation/          # SQL generation
├── LLM/                 # LLM client implementations
├── Observability/       # Metrics, logging, health checks
├── Prompts/             # Prompt templates
├── RAG/                 # RAG implementations
├── Security/            # Security utilities
├── Templates/           # Code generation templates
├── Tools/               # Tool implementations
├── Validation/          # Input validation
├── VectorDB/            # Vector database implementations
├── Verification/        # Result verification
└── TextToSqlAgent.Infrastructure.csproj
```

## File Roles

### Agent (ReAct Implementation)

| File                                                                             | Responsibility                                                                        |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| [`ReActAgent.cs`](TextToSqlAgent.Infrastructure/Agent/ReActAgent.cs)             | Implements ReAct (Reasoning + Acting) pattern. Loop: think → act → observe → reflect. |
| [`ReasoningEngine.cs`](TextToSqlAgent.Infrastructure/Agent/ReasoningEngine.cs)   | Reasoning engine for generating thoughts.                                             |
| [`ReflectionEngine.cs`](TextToSqlAgent.Infrastructure/Agent/ReflectionEngine.cs) | Self-reflection for error correction.                                                 |
| [`ToolRegistry.cs`](TextToSqlAgent.Infrastructure/Agent/ToolRegistry.cs)         | Registry for available tools.                                                         |
| [`LLMToolSelector.cs`](TextToSqlAgent.Infrastructure/Agent/LLMToolSelector.cs)   | LLM-powered tool selection.                                                           |
| [`CachedAgent.cs`](TextToSqlAgent.Infrastructure/Agent/CachedAgent.cs)           | Cached agent for performance.                                                         |

### LLM (Language Model Clients)

| File                                                                                     | Responsibility                                  |
| ---------------------------------------------------------------------------------------- | ----------------------------------------------- |
| [`OpenAIClient.cs`](TextToSqlAgent.Infrastructure/LLM/OpenAIClient.cs)                   | OpenAI GPT API client. Implements `ILLMClient`. |
| [`OpenAIEmbeddingClient.cs`](TextToSqlAgent.Infrastructure/LLM/OpenAIEmbeddingClient.cs) | OpenAI embedding generation.                    |
| [`GeminiClient.cs`](TextToSqlAgent.Infrastructure/LLM/GeminiClient.cs)                   | Google Gemini API client.                       |
| [`GeminiEmbeddingClient.cs`](TextToSqlAgent.Infrastructure/LLM/GeminiEmbeddingClient.cs) | Gemini embedding generation.                    |

### Database

| File                                                                          | Responsibility                         |
| ----------------------------------------------------------------------------- | -------------------------------------- |
| [`SqlExecutor.cs`](TextToSqlAgent.Infrastructure/Database/SqlExecutor.cs)     | SQL execution against databases.       |
| [`SchemaScanner.cs`](TextToSqlAgent.Infrastructure/Database/SchemaScanner.cs) | Scans database for schema information. |
| [`Adapters/`](TextToSqlAgent.Infrastructure/Database/Adapters)                | Database-specific adapters.            |

### VectorDB (Vector Store)

| File                                                                                      | Responsibility                          |
| ----------------------------------------------------------------------------------------- | --------------------------------------- |
| [`QdrantVectorStore.cs`](TextToSqlAgent.Infrastructure/VectorDB/QdrantVectorStore.cs)     | Qdrant vector database implementation.  |
| [`QdrantService.cs`](TextToSqlAgent.Infrastructure/VectorDB/QdrantService.cs)             | Qdrant API service.                     |
| [`InMemoryVectorStore.cs`](TextToSqlAgent.Infrastructure/VectorDB/InMemoryVectorStore.cs) | In-memory fallback for development.     |
| [`FallbackVectorStore.cs`](TextToSqlAgent.Infrastructure/VectorDB/FallbackVectorStore.cs) | Fallback when vector store unavailable. |

### RAG (Retrieval-Augmented Generation)

| File                                                                                       | Responsibility                               |
| ------------------------------------------------------------------------------------------ | -------------------------------------------- |
| [`SchemaRetriever.cs`](TextToSqlAgent.Infrastructure/RAG/SchemaRetriever.cs)               | Retrieves relevant schema from vector store. |
| [`SchemaIndexer.cs`](TextToSqlAgent.Infrastructure/RAG/SchemaIndexer.cs)                   | Indexes schema to vector store.              |
| [`HybridSearchEngine.cs`](TextToSqlAgent.Infrastructure/RAG/HybridSearchEngine.cs)         | Combines semantic and keyword search.        |
| [`AdvancedSchemaLinker.cs`](TextToSqlAgent.Infrastructure/RAG/AdvancedSchemaLinker.py)     | Advanced schema linking.                     |
| [`EntityRecognizer.cs`](TextToSqlAgent.Infrastructure/RAG/EntityRecognizer.cs)             | Recognizes database entities in query.       |
| [`KeywordSchemaRetriever.cs`](TextToSqlAgent.Infrastructure/RAG/KeywordSchemaRetriever.cs) | Keyword-based schema retrieval.              |
| [`RelationshipInference.cs`](TextToSqlAgent.Infrastructure/RAG/RelationshipInference.cs)   | Infers table relationships.                  |

### Caching

| File                                                                                 | Responsibility           |
| ------------------------------------------------------------------------------------ | ------------------------ |
| [`CacheService.cs`](TextToSqlAgent.Infrastructure/Caching/CacheService.cs)           | General caching service. |
| [`SimpleMemoryCache.cs`](TextToSqlAgent.Infrastructure/Caching/SimpleMemoryCache.cs) | Simple in-memory cache.  |

### Configuration

| File                                                                                 | Responsibility                                     |
| ------------------------------------------------------------------------------------ | -------------------------------------------------- |
| [`AgentConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/AgentConfig.cs)       | Agent configuration (maxSteps, temperature, etc.). |
| [`DatabaseConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/DatabaseConfig.cs) | Database connection config.                        |
| [`OpenAIConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/OpenAIConfig.cs)     | OpenAI API config.                                 |
| [`GeminiConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/GeminiConfig.cs)     | Gemini API config.                                 |
| [`QdrantConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/QdrantConfig.cs)     | Qdrant config.                                     |
| [`LLMProvider.cs`](TextToSqlAgent.Infrastructure/Configuration/LLMProvider.cs)       | LLM provider enum.                                 |
| [`RAGConfig.cs`](TextToSqlAgent.Infrastructure/Configuration/RAGConfig.cs)           | RAG configuration.                                 |

### ErrorHandling

| File                                                                                                 | Responsibility             |
| ---------------------------------------------------------------------------------------------------- | -------------------------- |
| [`BaseErrorHandler.cs`](TextToSqlAgent.Infrastructure/ErrorHandling/BaseErrorHandler.cs)             | Base error handler.        |
| [`SqlErrorHandler.cs`](TextToSqlAgent.Infrastructure/ErrorHandling/SqlErrorHandler.cs)               | SQL error handling.        |
| [`LLMErrorHandler.cs`](TextToSqlAgent.Infrastructure/ErrorHandling/LLMErrorHandler.cs)               | LLM API error handling.    |
| [`ConnectionErrorHandler.cs`](TextToSqlAgent.Infrastructure/ErrorHandling/ConnectionErrorHandler.cs) | Connection error handling. |
| [`VectorDBErrorHandler.cs`](TextToSqlAgent.Infrastructure/ErrorHandling/VectorDBErrorHandler.cs)     | Vector DB error handling.  |

### Observability

| File                                                                                         | Responsibility                |
| -------------------------------------------------------------------------------------------- | ----------------------------- |
| [`MetricsCollector.cs`](TextToSqlAgent.Infrastructure/Observability/MetricsCollector.cs)     | Collects application metrics. |
| [`HealthCheckService.cs`](TextToSqlAgent.Infrastructure/Observability/HealthCheckService.cs) | Health check implementation.  |
| [`TelemetryService.cs`](TextToSqlAgent.Infrastructure/Observability/TelemetryService.cs)     | Distributed tracing.          |
| [`ObservableAgent.cs`](TextToSqlAgent.Infrastructure/Observability/ObservableAgent.cs)       | Agent with observability.     |

### Security

| File                                                                                            | Responsibility                |
| ----------------------------------------------------------------------------------------------- | ----------------------------- |
| [`RateLimiter.cs`](TextToSqlAgent.Infrastructure/Security/RateLimiter.cs)                       | Rate limiting implementation. |
| [`SqlInjectionPrevention.cs`](TextToSqlAgent.Infrastructure/Security/SqlInjectionPrevention.cs) | SQL injection prevention.     |
| [`QueryCostEstimator.cs`](TextToSqlAgent.Infrastructure/Security/QueryCostEstimator.cs)         | Estimates query cost.         |

### Prompts

| File                                                                                     | Responsibility                 |
| ---------------------------------------------------------------------------------------- | ------------------------------ |
| [`PromptRegistry.cs`](TextToSqlAgent.Infrastructure/Prompts/PromptRegistry.cs)           | Registry for prompt templates. |
| [`PromptTemplate.cs`](TextToSqlAgent.Infrastructure/Prompts/PromptTemplate.cs)           | Prompt template class.         |
| [`PromptOptimizer.cs`](TextToSqlAgent.Infrastructure/Prompts/PromptOptimizer.cs)         | Optimizes prompts.             |
| [`SqlGenerationPrompt.cs`](TextToSqlAgent.Infrastructure/Prompts/SqlGenerationPrompt.cs) | SQL generation prompt.         |
| [`SqlCorrectionPrompt.cs`](TextToSqlAgent.Infrastructure/Prompts/SqlCorrectionPrompt.cs) | SQL correction prompt.         |

### Tools (ReAct Tools)

| File                                                                                         | Responsibility                |
| -------------------------------------------------------------------------------------------- | ----------------------------- |
| [`SqlExecutorTool.cs`](TextToSqlAgent.Infrastructure/Tools/SqlExecutorTool.cs)               | Tool for SQL execution.       |
| [`SqlGeneratorTool.cs`](TextToSqlAgent.Infrastructure/Tools/SqlGeneratorTool.cs)             | Tool for SQL generation.      |
| [`SchemaExplorerTool.cs`](TextToSqlAgent.Infrastructure/Tools/SchemaExplorerTool.cs)         | Tool for schema exploration.  |
| [`SqlValidatorTool.cs`](TextToSqlAgent.Infrastructure/Tools/SqlValidatorTool.cs)             | Tool for SQL validation.      |
| [`AmbiguityDetectorTool.cs`](TextToSqlAgent.Infrastructure/Tools/AmbiguityDetectorTool.cs)   | Tool for ambiguity detection. |
| [`ComplexityAnalyzerTool.cs`](TextToSqlAgent.Infrastructure/Tools/ComplexityAnalyzerTool.cs) | Tool for complexity analysis. |
| [`QueryDecomposerTool.cs`](TextToSqlAgent.Infrastructure/Tools/QueryDecomposerTool.cs)       | Tool for query decomposition. |
| [`ResultVerifierTool.cs`](TextToSqlAgent.Infrastructure/Tools/ResultVerifierTool.cs)         | Tool for result verification. |

### Analysis

| File                                                                                              | Responsibility             |
| ------------------------------------------------------------------------------------------------- | -------------------------- |
| [`AmbiguityDetector.cs`](TextToSqlAgent.Infrastructure/Analysis/AmbiguityDetector.cs)             | Detects query ambiguity.   |
| [`QueryComplexityAnalyzer.cs`](TextToSqlAgent.Infrastructure/Analysis/QueryComplexityAnalyzer.cs) | Analyzes query complexity. |
| [`QueryPatternMatcher.cs`](TextToSqlAgent.Infrastructure/Analysis/QueryPatternMatcher.cs)         | Matches query patterns.    |
| [`SqlErrorAnalyzer.cs`](TextToSqlAgent.Infrastructure/Analysis/SqlErrorAnalyzer.cs)               | Analyzes SQL errors.       |

### Verification

| File                                                                                | Responsibility          |
| ----------------------------------------------------------------------------------- | ----------------------- |
| [`ResultVerifier.cs`](TextToSqlAgent.Infrastructure/Verification/ResultVerifier.cs) | Verifies query results. |

### Factories

| File                                                                                             | Responsibility                 |
| ------------------------------------------------------------------------------------------------ | ------------------------------ |
| [`LLMClientFactory.cs`](TextToSqlAgent.Infrastructure/Factories/LLMClientFactory.cs)             | Factory for LLM clients.       |
| [`EmbeddingClientFactory.cs`](TextToSqlAgent.Infrastructure/Factories/EmbeddingClientFactory.cs) | Factory for embedding clients. |
| [`DatabaseAdapterFactory.cs`](TextToSqlAgent.Infrastructure/Factories/DatabaseAdapterFactory.cs) | Factory for database adapters. |

### Extensions

| File                                                                                                        | Responsibility                  |
| ----------------------------------------------------------------------------------------------------------- | ------------------------------- |
| [`ServiceCollectionExtensions.cs`](TextToSqlAgent.Infrastructure/Extensions/ServiceCollectionExtensions.cs) | DI extension methods.           |
| [`ProductionExtensions.cs`](TextToSqlAgent.Infrastructure/Extensions/ProductionExtensions.cs)               | Production-specific extensions. |

## Configuration

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "..."
  },
  "Cache": {
    "Provider": "Redis",
    "ConnectionString": "..."
  },
  "VectorStore": {
    "Provider": "Qdrant",
    "Url": "http://localhost:6333"
  }
}
```

## Dependencies

- `TextToSqlAgent.Core` - Core interfaces and models
- External packages: EF Core, Dapper, OpenAI, Qdrant, Redis, etc.
