# TextToSqlAgent Evaluation Framework

## Overview

Comprehensive evaluation framework for measuring Text-to-SQL agent performance.

## Metrics Tracked

### Accuracy Metrics
- **Execution Accuracy**: % of queries that execute successfully
- **Exact Match Accuracy**: % of SQL that exactly matches ground truth
- **Result Accuracy**: % of results that match expected output

### Schema Linking Metrics
- **Precision**: How many selected tables/columns are correct
- **Recall**: How many required tables/columns were found
- **F1 Score**: Harmonic mean of precision and recall

### Performance Metrics
- **Latency**: P50, P95, P99 response times
- **Token Usage**: Total and average tokens per query
- **Cost**: Estimated API cost per query

## Running Evaluation

### Prerequisites

1. Set environment variables:
```bash
# Windows PowerShell
$env:OPENAI_API_KEY="your_openai_api_key"
# Hoặc: $env:GEMINI_API_KEY="your_gemini_api_key"

$env:TEST_DB_CONNECTION="Server=localhost;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;"

# Linux/Mac
export OPENAI_API_KEY="your_openai_api_key"
# Hoặc: export GEMINI_API_KEY="your_gemini_api_key"

export TEST_DB_CONNECTION="Server=localhost;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;"
```

2. Ensure Qdrant is running:
```bash
docker run -d --name qdrant-test -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

3. Ensure test database exists with sample data

### Run Baseline Evaluation

```bash
cd TextToSqlAgent.Evaluation
dotnet test --logger "console;verbosity=detailed"
```

### Output

The evaluation will generate:
- Console report with formatted metrics
- JSON report: `baseline_report_YYYYMMDD_HHMMSS.json`
- CSV report: `baseline_report_YYYYMMDD_HHMMSS.csv`

## Sample Output

```
╔════════════════════════════════════════════════════════════════╗
║           TEXT-TO-SQL EVALUATION REPORT                        ║
╚════════════════════════════════════════════════════════════════╝

Version: baseline
Timestamp: 2026-03-07 10:30:00
Total Examples: 7

┌─────────────────────────────────────────────────────────────┐
│ ACCURACY METRICS                                            │
├─────────────────────────────────────────────────────────────┤
│ Execution Accuracy:     65.00%                              │
│ Exact Match Accuracy:   12.00%                              │
│ Result Accuracy:        58.00%                              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ PERFORMANCE METRICS                                         │
├─────────────────────────────────────────────────────────────┤
│ Avg Latency:          3200 ms                               │
│ P95 Latency:          5100 ms                               │
│ Total Tokens:         21000                                 │
│ Avg Tokens/Query:     3000                                  │
└─────────────────────────────────────────────────────────────┘
```

## Adding Custom Test Cases

Edit `Datasets/SampleDataset.cs`:

```csharp
new EvaluationExample
{
    Id = "custom_001",
    Question = "Your question here",
    DatabaseId = "test_db",
    GroundTruthSql = "SELECT ...",
    Difficulty = "Medium",
    RequiredTables = new List<string> { "Table1", "Table2" },
    RequiredColumns = new List<string> { "Column1", "Column2" }
}
```

## Next Steps

After establishing baseline:
1. Implement ReAct agent (Phase 1)
2. Run evaluation again
3. Compare metrics
4. Iterate and improve
