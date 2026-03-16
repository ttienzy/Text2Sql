# TextToSqlAgent.Evaluation

Testing and evaluation framework for the Text-to-SQL agent.

## Overview

Comprehensive evaluation framework for testing agent accuracy, performance, and reliability. Includes test datasets, metrics calculation, and evaluators.

## Project Structure

```
TextToSqlAgent.Evaluation/
├── Datasets/
│   └── SampleDataset.cs           # Test datasets
├── Metrics/
│   └── MetricsCalculator.cs      # Evaluation metrics
├── Models/
│   ├── EvaluationExample.cs       # Evaluation example
│   ├── EvaluationReport.cs        # Evaluation report
│   └── EvaluationResult.cs        # Evaluation result
├── Reports/
│   └── ReportGenerator.cs        # Report generation
├── Runners/
│   ├── BaselineEvaluator.cs      # Baseline evaluation
│   └── ReActAgentEvaluator.cs    # ReAct agent evaluation
├── Tests/
│   ├── BaselineEvaluationTests.cs # Baseline tests
│   └── ReActAgentEvaluationTests.cs # ReAct agent tests
├── Validators/
│   └── ResultValidator.cs        # Result validation
└── TextToSqlAgent.Evaluation.csproj
```

## File Roles

### Runners

| File                                                                                 | Responsibility                                                                       |
| ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------ |
| [`BaselineEvaluator.cs`](TextToSqlAgent.Evaluation/Runners/BaselineEvaluator.cs)     | Evaluates baseline (non-agentic) approach. Tests simple prompt-based SQL generation. |
| [`ReActAgentEvaluator.cs`](TextToSqlAgent.Evaluation/Runners/ReActAgentEvaluator.cs) | Evaluates ReAct agent approach. Tests reasoning + acting loop.                       |

### Metrics

| File                                                                             | Responsibility                                                  |
| -------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| [`MetricsCalculator.cs`](TextToSqlAgent.Evaluation/Metrics/MetricsCalculator.cs) | Calculates evaluation metrics: accuracy, precision, recall, F1. |

### Models

| File                                                                            | Responsibility                                               |
| ------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| [`EvaluationExample.cs`](TextToSqlAgent.Evaluation/Models/EvaluationExample.cs) | Single test case: question, expected SQL, expected result.   |
| [`EvaluationResult.cs`](TextToSqlAgent.Evaluation/Models/EvaluationResult.cs)   | Result of a single evaluation: passed/failed, error details. |
| [`EvaluationReport.cs`](TextToSqlAgent.Evaluation/Models/EvaluationReport.cs)   | Aggregated results: overall accuracy, per-category metrics.  |

### Tests

| File                                                                                           | Responsibility                     |
| ---------------------------------------------------------------------------------------------- | ---------------------------------- |
| [`BaselineEvaluationTests.cs`](TextToSqlAgent.Evaluation/Tests/BaselineEvaluationTests.cs)     | xUnit tests for baseline approach. |
| [`ReActAgentEvaluationTests.cs`](TextToSqlAgent.Evaluation/Tests/ReActAgentEvaluationTests.cs) | xUnit tests for ReAct agent.       |

### Validators

| File                                                                            | Responsibility                                    |
| ------------------------------------------------------------------------------- | ------------------------------------------------- |
| [`ResultValidator.cs`](TextToSqlAgent.Evaluation/Validators/ResultValidator.cs) | Validates query results against expected results. |

### Datasets

| File                                                                      | Responsibility                                |
| ------------------------------------------------------------------------- | --------------------------------------------- |
| [`SampleDataset.cs`](TextToSqlAgent.Evaluation/Datasets/SampleDataset.cs) | Sample test dataset with various query types. |

### Reports

| File                                                                         | Responsibility                             |
| ---------------------------------------------------------------------------- | ------------------------------------------ |
| [`ReportGenerator.cs`](TextToSqlAgent.Evaluation/Reports/ReportGenerator.cs) | Generates evaluation reports (HTML, JSON). |

## Metrics

| Metric             | Description                       |
| ------------------ | --------------------------------- |
| Execution Accuracy | Correct SQL execution (no errors) |
| Result Accuracy    | Correct result returned           |
| Schema Linking     | Accurate schema usage             |
| Complexity Score   | Query complexity rating           |
| Response Time      | Average response time             |

## Running Evaluation

```bash
# Run all evaluations
dotnet test TextToSqlAgent.Evaluation

# Run specific evaluator
dotnet test TextToSqlAgent.Evaluation --filter "FullyQualifiedName~ReActAgentEvaluator"
```

## Test Categories

The framework includes test datasets for:

- **Simple queries** - Single table SELECT
- **Complex joins** - Multi-table JOINs
- **Aggregations** - GROUP BY, HAVING
- **Nested queries** - Subqueries
- **Data modification** - INSERT, UPDATE, DELETE

## Dependencies

- `TextToSqlAgent.Core` - Core interfaces
- `TextToSqlAgent.Application` - Application services
- xUnit - Testing framework
