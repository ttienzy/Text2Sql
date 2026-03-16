# TextToSqlAgent.Plugins

Extensible plugin system for the Text-to-SQL agent.

## Overview

Provides a plugin architecture allowing customization and extension of agent capabilities. Each plugin implements specific functionality that can be enabled/disabled.

## Project Structure

```
TextToSqlAgent.Plugins/
├── IntentAnalysisPlugin.cs      # Intent analysis
├── QueryExplainerPlugin.cs      # SQL explanation
├── QueryValidatorPlugin.cs      # Query validation
├── SqlCorrectorPlugin.cs        # SQL correction
├── SqlGeneratorPlugin.cs        # SQL generation
└── TextToSqlAgent.Plugins.csproj
```

## File Roles

| File                                                                        | Responsibility                                                                                           |
| --------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| [`IntentAnalysisPlugin.cs`](TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs) | Analyzes user query to determine intent (SELECT, INSERT, UPDATE, DELETE), complexity, and entities.      |
| [`QueryValidatorPlugin.cs`](TextToSqlAgent.Plugins/QueryValidatorPlugin.cs) | Validates user input before processing. Checks for empty queries, unsupported characters, length limits. |
| [`QueryExplainerPlugin.cs`](TextToSqlAgent.Plugins/QueryExplainerPlugin.cs) | Explains generated SQL in natural language. Helps users understand what the SQL does.                    |
| [`SqlGeneratorPlugin.cs`](TextToSqlAgent.Plugins/SqlGeneratorPlugin.cs)     | Custom SQL generation with template support. Can be extended for specific databases.                     |
| [`SqlCorrectorPlugin.cs`](TextToSqlAgent.Plugins/SqlCorrectorPlugin.cs)     | SQL syntax correction when execution fails. Attempts to fix common errors.                               |

## Plugin Architecture

```csharp
public interface IAgentPlugin
{
    string Name { get; }
    int Priority { get; }
    Task<bool> ExecuteAsync(PluginContext context);
}
```

## Usage

```csharp
// Register plugins
services.AddPlugin<IntentAnalysisPlugin>();
services.AddPlugin<QueryValidatorPlugin>();

// Or in configuration
{
  "Plugins": {
    "IntentAnalysis": true,
    "QueryValidation": true,
    "SqlExplanation": true
  }
}
```

## Dependencies

- `TextToSqlAgent.Core` - Core interfaces
