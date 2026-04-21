# TextToSQL Agent

AI-powered Natural Language to SQL Converter với Agentic Architecture

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Frontend (React)                        │
│                     http://localhost:5173                       │
└─────────────────────────────┬───────────────────────────────────┘
                              │ REST + SSE
┌─────────────────────────────▼───────────────────────────────────┐
│                      .NET API (ASP.NET Core)                    │
│                     http://localhost:5251                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐ │
│  │ Orchestrator│  │   RAG/Qdrant│  │ Python Intent Classifier│ │
│  └──────┬──────┘  └──────┬──────┘  └───────────┬─────────────┘ │
└─────────┼────────────────┼─────────────────────┼───────────────┘
          │                │                     │
          ▼                ▼                     ▼
    ┌──────────┐    ┌───────────┐         ┌────────────┐
    │ SQL      │    │  Qdrant   │         │  Python    │
    │ Server   │    │  (Vector) │         │  Sidecar   │
    └──────────┘    └───────────┘         └────────────┘
```

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker & Docker Compose
- SQL Server 2022 (local or container)
- Qdrant (vector database)

### 1. Start Infrastructure

```bash
# Start all services (SQL Server, Qdrant, Redis, API, Frontend, Python Sidecar)
docker-compose up -d
```

Services:
| Service | Port | URL |
|---------|------|-----|
| Frontend | 5173 | http://localhost:5173 |
| API | 5251 | http://localhost:5251 |
| Python Sidecar | 8100 | http://localhost:8100 |
| Qdrant | 6333 | http://localhost:6333 |
| SQL Server | 11433 | localhost,11433 |

### 2. Database Setup

```bash
# Initialize test database (if using test container)
docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "Test@Pass123!" < test-data/setup-test-db.sql
```

### 3. Run Locally (Without Docker)

```bash
# Backend
dotnet run --project TextToSqlAgent.API

# Frontend (separate terminal)
cd frontend
npm install
npm run dev
```

## Environment Variables

### Required (.env)

```bash
# Create in TextToSqlAgent.API/
OPENAI_API_KEY=sk-your-openai-key
DATABASE_CONNECTION_STRING=Server=localhost;Database=TextToSqlAgent;User Id=sa;Password=YourPassword;TrustServerCertificate=True;
```

### Optional

```bash
QDRANT_URL=http://localhost:6333
JWT_SECRET=your-32-char-min-secret-key
REDIS_CONNECTION_STRING=localhost:6379
INTENT_CLASSIFIER_PROVIDER=Python  # or "LLM"
PYTHON_SIDECAR_URL=http://localhost:8100
```

## Project Structure

```
TextToSqlAgent/
├── TextToSqlAgent.API/           # REST API, Controllers, SSE
├── TextToSqlAgent.Application/   # Business logic, Orchestrator, Pipeline
├── TextToSqlAgent.Core/          # Models, Interfaces, Enums
├── TextToSqlAgent.Infrastructure/# Database, Qdrant, Redis, Auth
├── TextToSqlAgent.Plugins/       # SQL Executor, Schema Loader plugins
├── TextToSqlAgent.Console/       # CLI tool
├── TextToSqlAgent.Evaluation/    # Evaluation metrics
├── TextToSqlAgent.Tests.Unit/    # Unit tests
├── TextToSqlAgent.Tests.Integration/ # Integration tests
├── frontend/                     # React + Vite + Ant Design
├── python-sidecar/               # Python intent classifier (ML)
├── test-data/                    # Test database setup
├── Prompts/                      # LLM prompt templates
├── docs/                         # Architecture docs
└── docker-compose.yml            # Production compose
```

## Key Features

### Intent Classification
- **Python Sidecar**: ML-based classifier (scikit-learn)
- **LLM Fallback**: Uses OpenAI for intent detection

### Agent Pipeline
1. **Validating** - Query normalization & relevance check
2. **Classifying** - Intent detection (SELECT/WRITE/DDL)
3. **Schema Retrieval** - Load from Qdrant vector DB
4. **SQL Generation** - Generate via LLM
5. **SQL Validation** - Safety & syntax check
6. **Executing** - Run against SQL Server
7. **Self-Correcting** - Retry on failure

### Real-time Updates
- SSE (Server-Sent Events) for streaming progress
- Live SQL preview during generation
- Interactive thinking indicator

## Testing

### Integration Tests

```bash
# Start test infrastructure
docker-compose -f docker-compose.test.yml up -d

# Initialize test database
docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "Test@Pass123!" < test-data/setup-test-db.sql

# Run tests
dotnet test TextToSqlAgent.Tests.Integration
```

### API Testing (REST Client)

```bash
# Test using VS Code REST Client
code test-all-features.http
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v2/agent/process/stream` | SSE streaming query |
| POST | `/api/v1/query` | Simple query (legacy) |
| GET | `/api/connections` | List database connections |
| POST | `/api/connections` | Create connection |
| GET | `/api/conversations` | List conversations |

## Python Sidecar

Intent classifier ML service:

```bash
# Build & start
docker-compose up -d python-sidecar

# Test
curl -X POST http://localhost:8100/api/v1/intent/detect \
  -H "Content-Type: application/json" \
  -d '{"query": "show me all customers"}'
```

Training data: `python-sidecar/training/data/benchmark/`

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core, Entity Framework Core
- **Frontend**: React 19, Vite 6, Ant Design 6, Zustand
- **AI**: OpenAI GPT-4o, Semantic Kernel
- **Database**: SQL Server 2022, Qdrant (vector), Redis (cache)
- **ML**: Python, scikit-learn, FastAPI
- **Container**: Docker, Docker Compose

## Troubleshooting

### API not starting
```bash
# Check logs
docker logs texttosqlagent-api-1

# Verify environment
docker exec texttosqlagent-api-1 env | grep -E "OPENAI|DATABASE|QDRANT"
```

### Python Sidecar connection refused
```bash
# Check health
curl http://localhost:8100/health

# Rebuild
docker-compose up -d --build python-sidecar
```

### Database connection issues
```bash
# Check SQL Server
docker logs texttosql-test-db

# Test connection
docker exec -it texttosql-test-db /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "Test@Pass123!" -Q "SELECT 1"
```

## License

MIT