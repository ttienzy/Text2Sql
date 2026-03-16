# TextToSqlAgent.Console

Console application for interactive testing and demonstration of the Text-to-SQL agent.

## Overview

A rich console UI using Spectre.Console library that allows users to interact with the Text-to-SQL agent through a command-line interface. This is the recommended way to test and explore the agent's capabilities.

## Project Structure

```
TextToSqlAgent.Console/
├── Agent/                        # Agent implementations
├── Commands/
│   ├── CommandHandler.cs       # Command processing
│   └── CommandTypes.cs        # Command type definitions
├── Configuration/
│   ├── ConfigurationManager.cs # Configuration management
│   ├── ConnectionManager.cs    # Database connection management
│   ├── SecureConfigStore.cs   # Secure configuration storage
│   └── SetupWizard.cs        # First-time setup wizard
├── Observability/
│   └── ConsoleMetrics.cs      # Console metrics display
├── Services/
│   └── ConsoleRequestProcessor.cs # Request processing
├── Setup/
│   ├── ConfigurationLoader.cs  # Configuration loading
│   └── DependencyInjection.cs # DI setup
├── UI/
│   ├── AgentStepRenderer.cs   # Agent step visualization
│   ├── ConnectionBuilder.cs   # Connection string builder
│   ├── ConsoleUI.cs          # Main console UI
│   ├── ResponseFormatter.cs  # Response formatting
│   └── TableRenderer.cs      # Table rendering
└── Program.cs                 # Entry point
```

## File Roles

### Commands

| File                                                                     | Responsibility                                                                                                                                                                |
| ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`CommandHandler.cs`](TextToSqlAgent.Console/Commands/CommandHandler.cs) | Processes user commands like `/help`, `/config`, `/clear`, `/new`. Handles natural language queries by delegating to EnhancedAgentOrchestrator. Supports Vietnamese commands. |
| [`CommandTypes.cs`](TextToSqlAgent.Console/Commands/CommandTypes.cs)     | Defines command type enums and command result types.                                                                                                                          |

### Configuration

| File                                                                                      | Responsibility                                                                                  |
| ----------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| [`ConfigurationManager.cs`](TextToSqlAgent.Console/Configuration/ConfigurationManager.cs) | Manages application configuration from appsettings.json. Handles environment-specific settings. |
| [`ConnectionManager.cs`](TextToSqlAgent.Console/Configuration/ConnectionManager.cs)       | Manages database connections, connection string building, and connection testing.               |
| [`SecureConfigStore.cs`](TextToSqlAgent.Console/Configuration/SecureConfigStore.cs)       | Securely stores sensitive config (API keys) using DPAPI encryption.                             |
| [`SetupWizard.cs`](TextToSqlAgent.Console/Configuration/SetupWizard.cs)                   | Interactive first-time setup wizard. Prompts for API key and validates configuration.           |

### UI

| File                                                                     | Responsibility                                                                                             |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------- |
| [`ConsoleUI.cs`](TextToSqlAgent.Console/UI/ConsoleUI.cs)                 | Main UI class with welcome banner, configuration display, and menus. Uses Spectre.Console for rich output. |
| [`AgentStepRenderer.cs`](TextToSqlAgent.Console/UI/AgentStepRenderer.cs) | Renders agent execution steps in real-time. Shows reasoning, tool selection, and execution progress.       |
| [`ConnectionBuilder.cs`](TextToSqlAgent.Console/UI/ConnectionBuilder.cs) | Interactive connection string builder with prompts for server, database, auth type.                        |
| [`ResponseFormatter.cs`](TextToSqlAgent.Console/UI/ResponseFormatter.cs) | Formats SQL queries and results for display. Adds syntax highlighting.                                     |
| [`TableRenderer.cs`](TextToSqlAgent.Console/UI/TableRenderer.cs)         | Renders query results as formatted tables using Spectre.Console.                                           |

### Services

| File                                                                                       | Responsibility                                                                                |
| ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------- |
| [`ConsoleRequestProcessor.cs`](TextToSqlAgent.Console/Services/ConsoleRequestProcessor.cs) | Processes user input and coordinates between UI and agent. Handles both commands and queries. |

### Setup

| File                                                                            | Responsibility                                                 |
| ------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| [`ConfigurationLoader.cs`](TextToSqlAgent.Console/Setup/ConfigurationLoader.cs) | Loads configuration from various sources (files, environment). |
| [`DependencyInjection.cs`](TextToSqlAgent.Console/Setup/DependencyInjection.cs) | Configures DI container with all required services.            |

### Observability

| File                                                                          | Responsibility                                           |
| ----------------------------------------------------------------------------- | -------------------------------------------------------- |
| [`ConsoleMetrics.cs`](TextToSqlAgent.Console/Observability/ConsoleMetrics.cs) | Displays metrics and performance information in console. |

## Running

```bash
dotnet run --project TextToSqlAgent.Console
```

## Features

### Interactive Mode

- Natural language query input
- Real-time agent step visualization
- Formatted SQL results display
- Connection management
- Multi-language support (English/Vietnamese)

### Commands

| Command       | Description              |
| ------------- | ------------------------ |
| `/new`        | Start new conversation   |
| `/connect`    | Connect to database      |
| `/disconnect` | Disconnect from database |
| `/schema`     | Show database schema     |
| `/config`     | Configure settings       |
| `/api-key`    | Set API key              |
| `/reset`      | Reset configuration      |
| `/history`    | Show query history       |
| `/clear`      | Clear screen             |
| `/help`       | Show help                |
| `/exit`       | Exit application         |

### Vietnamese Commands

- `/new` = "new conversation", "new chat", "hoi thoai moi", "hội thoại mới"
- `/clear` = "clear", "cls", "xoa", "xóa"
- `/help` = "help", "?", "trogiup", "trợ giúp"

## Configuration

```json
{
  "OpenAI": {
    "ApiKey": "your-api-key",
    "Model": "gpt-4o"
  },
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "..."
  },
  "Agent": {
    "MaxSteps": 10,
    "Temperature": 0.1
  }
}
```

## Dependencies

- `TextToSqlAgent.Application` - Application services
- `TextToSqlAgent.Core` - Domain models
- `TextToSqlAgent.Infrastructure` - Data access
- Spectre.Console - Rich console UI
