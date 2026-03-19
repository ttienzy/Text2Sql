# TextToSqlAgent.API

Web API layer providing REST endpoints for the Text-to-SQL agent system.

## Overview

ASP.NET Core Web API project that exposes the Text-to-SQL functionality via HTTP endpoints. Provides authentication, rate limiting, and observability features.

## Project Structure

```
TextToSqlAgent.API/
├── Controllers/
│   ├── AgentController.cs         # Main agent endpoints
│   ├── AuthController.cs          # Authentication endpoints
│   ├── BaseController.cs          # Base controller abstraction
│   ├── ConnectionsController.cs   # Database connections management
│   ├── ConversationAwareAgentController.cs # Agent endpoints with conversation support
│   ├── ConversationsController.cs # Conversation management endpoints
│   ├── HealthController.cs        # Health check endpoints
│   ├── JobsController.cs          # Background jobs and task tracking
│   ├── MessagesController.cs      # Chat message endpoints
│   ├── ObservabilityController.cs # Metrics and status endpoints
│   ├── ProductionAgentController.cs # Production endpoints
│   └── TestController.cs          # Test endpoints
├── Data/
│   ├── AppDbContext.cs            # EF Core DbContext for Identity
│   └── ApplicationUser.cs        # Identity user entity
├── DTOs/
│   ├── AgentModels.cs             # Agent request/response DTOs
│   └── AuthModels.cs             # Auth request/response DTOs
├── Middleware/
│   ├── RateLimitMiddleware.cs    # Rate limiting per user/IP
│   └── SecurityHeadersMiddleware.cs # Security headers
├── Services/
│   └── SchemaSyncBackgroundService.cs # Background schema sync
├── Migrations/                     # EF Core migrations
├── Program.cs                      # Entry point & DI setup
└── appsettings.json               # Configuration
```

## File Roles

### Controllers

| File                                                                                          | Responsibility                                                                                                                                                                               |
| --------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`AgentController.cs`](TextToSqlAgent.API/Controllers/AgentController.cs)                     | Main REST endpoints: `/api/agent/query`, `/api/agent/chat`. Handles query execution, conversation management, and result formatting. Supports both legacy orchestrator and new agentic mode. |
| [`AuthController.cs`](TextToSqlAgent.API/Controllers/AuthController.cs)                       | User authentication: register, login, profile. Uses ASP.NET Core Identity with JWT tokens.                                                                                                   |
| [`BaseController.cs`](TextToSqlAgent.API/Controllers/BaseController.cs)                       | Base class providing common utility methods and standardized responses for controllers.                                                                                                        |
| [`ConnectionsController.cs`](TextToSqlAgent.API/Controllers/ConnectionsController.cs)         | Manages database connections settings dynamically during runtime.                                                                                                                              |
| [`ConversationAwareAgentController.cs`](TextToSqlAgent.API/Controllers/ConversationAwareAgentController.cs) | Enhanced agent endpoints that specifically handle multi-turn context and contextual conversation workflows.                                                                                    |
| [`ConversationsController.cs`](TextToSqlAgent.API/Controllers/ConversationsController.cs)     | Endpoints to fetch, list, and manage conversation sessions and metadata.                                                                                                                     |
| [`HealthController.cs`](TextToSqlAgent.API/Controllers/HealthController.cs)                   | Standard liveness/readiness health-check endpoints.                                                                                                                                          |
| [`JobsController.cs`](TextToSqlAgent.API/Controllers/JobsController.cs)                       | Endpoints to monitor and manage background jobs and long-running tasks.                                                                                                                      |
| [`MessagesController.cs`](TextToSqlAgent.API/Controllers/MessagesController.cs)               | Endpoints to retrieve messages within a specific conversation context.                                                                                                                       |
| [`ObservabilityController.cs`](TextToSqlAgent.API/Controllers/ObservabilityController.cs)     | System health metrics, performance stats, and verbose status endpoints for monitoring.                                                                                                       |
| [`ProductionAgentController.cs`](TextToSqlAgent.API/Controllers/ProductionAgentController.cs) | Production-optimized endpoints with additional validation and error handling.                                                                                                                |
| [`TestController.cs`](TextToSqlAgent.API/Controllers/TestController.cs)                       | Developer/integration testing endpoints.                                                                                                                                                     |

### DTOs

| File                                                       | Responsibility                                                                                                                                |
| ---------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| [`AgentModels.cs`](TextToSqlAgent.API/DTOs/AgentModels.cs) | `QueryRequest` (question, conversationId), `QueryResponse` (sqlGenerated, result, rowCount, errorMessage, processingSteps, answer, metadata). |
| [`AuthModels.cs`](TextToSqlAgent.API/DTOs/AuthModels.cs)   | Request/response models for authentication (register, login).                                                                                 |

### Middleware

| File                                                                                         | Responsibility                                                                                      |
| -------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| [`RateLimitMiddleware.cs`](TextToSqlAgent.API/Middleware/RateLimitMiddleware.cs)             | Implements rate limiting per user/IP. Returns 429 when limit exceeded with `X-RateLimit-*` headers. |
| [`SecurityHeadersMiddleware.cs`](TextToSqlAgent.API/Middleware/SecurityHeadersMiddleware.cs) | Adds security headers (X-Frame-Options, X-Content-Type-Options, etc.).                              |

### Data

| File                                                               | Responsibility                                       |
| ------------------------------------------------------------------ | ---------------------------------------------------- |
| [`AppDbContext.cs`](TextToSqlAgent.API/Data/AppDbContext.cs)       | EF Core DbContext for Identity and application data. |
| [`ApplicationUser.cs`](TextToSqlAgent.API/Data/ApplicationUser.cs) | Custom user entity extending `IdentityUser`.         |

### Services

| File                                                                                           | Responsibility                                                              |
| ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| [`SchemaSyncBackgroundService.cs`](TextToSqlAgent.API/Services/SchemaSyncBackgroundService.cs) | Background service that periodically syncs database schema to vector store. |

## API Endpoints

### Agent Controller

| Method | Endpoint                      | Description                    |
| ------ | ----------------------------- | ------------------------------ |
| POST   | `/api/agent/query`            | Execute natural language query |
| POST   | `/api/agent/chat`             | Start a chat session           |
| GET    | `/api/agent/chat/{sessionId}` | Get chat history               |
| POST   | `/api/agent/validate`         | Validate SQL query             |

### Conversations & Messages Controller

| Method | Endpoint                                       | Description                              |
| ------ | ---------------------------------------------- | ---------------------------------------- |
| GET    | `/api/conversations`                           | List existing conversations              |
| GET    | `/api/conversations/{id}`                      | Get conversation details                 |
| GET    | `/api/conversations/{sessionId}/messages`      | Get messages inside a conversation       |

### Connections Controller

| Method | Endpoint                      | Description                              |
| ------ | ----------------------------- | ---------------------------------------- |
| GET    | `/api/connections`            | List available database connections      |
| POST   | `/api/connections`            | Add a new active connection              |

### Auth Controller

| Method | Endpoint             | Description       |
| ------ | -------------------- | ----------------- |
| POST   | `/api/auth/register` | Register new user |
| POST   | `/api/auth/login`    | Login user        |
| GET    | `/api/auth/profile`  | Get user profile  |

### Observability Controller

| Method | Endpoint                     | Description   |
| ------ | ---------------------------- | ------------- |
| GET    | `/api/observability/health`  | Health check  |
| GET    | `/api/observability/metrics` | Get metrics   |
| GET    | `/api/observability/status`  | System status |

## Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "OpenAI": {
    "ApiKey": "your-api-key"
  },
  "Database": {
    "Provider": "SqlServer"
  },
  "RateLimiting": {
    "Limit": 100,
    "Window": 60
  }
}
```

## Running

```bash
dotnet run --project TextToSqlAgent.API
```

The API will be available at `http://localhost:5000` (or configured port).

## Dependencies

- `TextToSqlAgent.Application` - Application services
- `TextToSqlAgent.Core` - Domain models
- `TextToSqlAgent.Infrastructure` - Data access
- `TextToSqlAgent.Plugins` - Plugin system
- Microsoft.AspNetCore.Identity - Authentication
- EF Core - Database access
