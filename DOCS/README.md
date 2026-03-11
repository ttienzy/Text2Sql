# 📚 TextToSqlAgent - Project Documentation

## Overview

TextToSqlAgent is an AI-powered Natural Language to SQL Agent system using ReAct Pattern. Production-ready system that converts natural language questions into SQL queries.

**Version:** 2.0.0  
**Status:** ✅ Production Ready  
**Last Updated:** March 9, 2026

---

## 🏗️ Project Structure

| Project                               | Description                                           |
| ------------------------------------- | ----------------------------------------------------- |
| [API](./API.md)                       | Web API layer with REST endpoints                     |
| [Console](./Console.md)               | Console application for interactive testing           |
| [Application](./Application.md)       | Application services and orchestration                |
| [Core](./Core.md)                     | Domain models, interfaces, and enums                  |
| [Infrastructure](./Infrastructure.md) | Database, cache, and external service implementations |
| [Plugins](./Plugins.md)               | Extensible plugin system                              |
| [Evaluation](./Evaluation.md)         | Testing and evaluation framework                      |

---

## 🚀 Quick Start

### Run Console App (Recommended for Testing)

```bash
dotnet run --project TextToSqlAgent.Console
```

### Run API

```bash
dotnet run --project TextToSqlAgent.API
```

### Run Tests

```bash
# Unit Tests
dotnet test TextToSqlAgent.Tests.Unit

# Integration Tests
dotnet test TextToSqlAgent.Tests.Integration
```

---

## 📋 Prerequisites

- .NET 10.0 SDK
- SQL Server (or any supported database)
- OpenAI API Key (or compatible LLM)

---

## 📁 Documentation Index

- [API Documentation](./API.md) - REST API endpoints and usage
- [Console Application](./Console.md) - CLI usage guide
- [Application Layer](./Application.md) - Service orchestration
- [Core Domain](./Core.md) - Domain models and interfaces
- [Infrastructure](./Infrastructure.md) - Data access and integrations

---

## 🔧 Configuration

See [`appsettings.json`](TextToSqlAgent.Console/appsettings.json) for configuration options.

### Required Environment Variables

- `OPENAI_API_KEY` - OpenAI API key
- `DATABASE_CONNECTION_STRING` - Database connection string

---

## 📊 Project Phases Completed

1. ✅ Phase 1: ReAct Agent Implementation
2. ✅ Phase 2: Advanced RAG & Schema Linking
3. ✅ Phase 3: Evaluation & Prompt Optimization
4. ✅ Phase 4: Observability & Monitoring
5. ✅ Phase 5: Advanced Features
6. ✅ Phase 6: Production Readiness
7. ✅ Phase 7: Agentic AI Enhancements
8. ✅ Phase 8: Console Application Refactor
