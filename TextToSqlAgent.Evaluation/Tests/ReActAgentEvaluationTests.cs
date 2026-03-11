using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Evaluation.Datasets;
using TextToSqlAgent.Evaluation.Metrics;
using TextToSqlAgent.Evaluation.Reports;
using TextToSqlAgent.Evaluation.Runners;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Extensions;
using TextToSqlAgent.Infrastructure.Factories;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Evaluation.Tests;

public class ReActAgentEvaluationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;

    public ReActAgentEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
        _serviceProvider = BuildServiceProvider();
    }

    [Fact]
    public async Task RunReActAgentEvaluation()
    {
        // Arrange
        var evaluator = _serviceProvider.GetRequiredService<ReActAgentEvaluator>();
        var reportGenerator = _serviceProvider.GetRequiredService<ReportGenerator>();
        var examples = SampleDataset.GetExamples();

        _output.WriteLine("=== REACT AGENT EVALUATION ===");
        _output.WriteLine($"Testing {examples.Count} examples with ReAct Agent");
        _output.WriteLine("");

        // Act
        var report = await evaluator.RunEvaluationAsync(examples);

        // Generate reports
        var consoleReport = reportGenerator.GenerateConsoleReport(report);
        _output.WriteLine(consoleReport);

        // Save reports
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        await reportGenerator.SaveJsonReportAsync(report, $"react_agent_report_{timestamp}.json");
        await reportGenerator.SaveCsvReportAsync(report, $"react_agent_report_{timestamp}.csv");

        // Assert - Basic sanity checks
        Assert.True(report.TotalExamples > 0, "Should have examples");
        Assert.InRange(report.ExecutionAccuracy, 0, 100);

        _output.WriteLine($"\n✓ ReAct Agent evaluation complete!");
        _output.WriteLine($"  Execution Accuracy: {report.ExecutionAccuracy:F2}%");
        _output.WriteLine($"  Avg Latency: {report.AvgLatencyMs:F0}ms");
        _output.WriteLine($"  Total Steps: {report.Results.Sum(r => r.AgentResponse?.ProcessingSteps?.Count ?? 0)}");
    }

    [Fact]
    public async Task CompareBaselineVsReActAgent()
    {
        _output.WriteLine("=== BASELINE vs REACT AGENT COMPARISON ===");
        _output.WriteLine("");

        var examples = SampleDataset.GetExamples();
        var reportGenerator = _serviceProvider.GetRequiredService<ReportGenerator>();

        // Run Baseline
        _output.WriteLine("Running Baseline Evaluation...");
        var baselineEvaluator = _serviceProvider.GetRequiredService<BaselineEvaluator>();
        var baselineReport = await baselineEvaluator.RunEvaluationAsync(examples);

        // Run ReAct Agent
        _output.WriteLine("Running ReAct Agent Evaluation...");
        var reactEvaluator = _serviceProvider.GetRequiredService<ReActAgentEvaluator>();
        var reactReport = await reactEvaluator.RunEvaluationAsync(examples);

        // Compare
        _output.WriteLine("");
        _output.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        _output.WriteLine("║                    COMPARISON RESULTS                          ║");
        _output.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        _output.WriteLine("");
        _output.WriteLine($"{"Metric",-30} {"Baseline",15} {"ReAct Agent",15} {"Improvement",15}");
        _output.WriteLine(new string('-', 75));

        CompareMetric("Execution Accuracy", baselineReport.ExecutionAccuracy, reactReport.ExecutionAccuracy);
        CompareMetric("Exact Match Accuracy", baselineReport.ExactMatchAccuracy, reactReport.ExactMatchAccuracy);
        CompareMetric("Result Accuracy", baselineReport.ResultAccuracy, reactReport.ResultAccuracy);
        CompareMetric("Schema Linking F1", baselineReport.SchemaLinkingF1, reactReport.SchemaLinkingF1);
        CompareMetric("Avg Latency (ms)", baselineReport.AvgLatencyMs, reactReport.AvgLatencyMs, lowerIsBetter: true);
        CompareMetric("Avg Tokens/Query", baselineReport.AvgTokensPerQuery, reactReport.AvgTokensPerQuery, lowerIsBetter: true);

        _output.WriteLine("");
        _output.WriteLine($"Total Examples: {examples.Count}");
        _output.WriteLine($"Baseline Failed: {baselineReport.FailedExamples.Count}");
        _output.WriteLine($"ReAct Failed: {reactReport.FailedExamples.Count}");

        // Save comparison report
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        await SaveComparisonReport(baselineReport, reactReport, $"comparison_report_{timestamp}.json");

        void CompareMetric(string name, double baseline, double react, bool lowerIsBetter = false)
        {
            var improvement = lowerIsBetter
                ? ((baseline - react) / baseline * 100)
                : ((react - baseline) / baseline * 100);

            var improvementStr = improvement >= 0 ? $"+{improvement:F1}%" : $"{improvement:F1}%";
            var symbol = improvement > 0 ? "↑" : improvement < 0 ? "↓" : "=";

            _output.WriteLine($"{name,-30} {baseline,15:F2} {react,15:F2} {improvementStr,12} {symbol}");
        }
    }

    private async Task SaveComparisonReport(
        Evaluation.Models.EvaluationReport baseline,
        Evaluation.Models.EvaluationReport react,
        string filename)
    {
        var comparison = new
        {
            Timestamp = DateTime.UtcNow,
            Baseline = new
            {
                baseline.ExecutionAccuracy,
                baseline.ExactMatchAccuracy,
                baseline.ResultAccuracy,
                baseline.AvgLatencyMs,
                baseline.AvgTokensPerQuery,
                FailedCount = baseline.FailedExamples.Count
            },
            ReActAgent = new
            {
                react.ExecutionAccuracy,
                react.ExactMatchAccuracy,
                react.ResultAccuracy,
                react.AvgLatencyMs,
                react.AvgTokensPerQuery,
                FailedCount = react.FailedExamples.Count
            },
            Improvements = new
            {
                ExecutionAccuracy = react.ExecutionAccuracy - baseline.ExecutionAccuracy,
                ExactMatchAccuracy = react.ExactMatchAccuracy - baseline.ExactMatchAccuracy,
                ResultAccuracy = react.ResultAccuracy - baseline.ResultAccuracy,
                LatencyReduction = baseline.AvgLatencyMs - react.AvgLatencyMs,
                TokenReduction = baseline.AvgTokensPerQuery - react.AvgTokensPerQuery
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(comparison, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filename, json);
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configuration
        var geminiConfig = new GeminiConfig
        {
            ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "",
            Model = "gemini-2.0-flash-exp",
            EmbeddingModel = "text-embedding-004",
            MaxTokens = 8192,
            Temperature = 0.1
        };

        var databaseConfig = new DatabaseConfig
        {
            ConnectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION") ??
                              "Server=localhost;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;",
            Provider = Core.Enums.DatabaseProvider.SqlServer
        };

        var agentConfig = new AgentConfig
        {
            MaxSelfCorrectionAttempts = 3,
            EnableSQLExplanation = false
        };

        var qdrantConfig = new QdrantConfig
        {
            Host = "localhost",
            Port = 6334,
            VectorSize = 768,
            UseGrpc = true
        };

        var ragConfig = new RAGConfig
        {
            TopK = 5,
            MinimumScore = 0.7
        };

        services.AddSingleton(geminiConfig);
        services.AddSingleton(databaseConfig);
        services.AddSingleton(agentConfig);
        services.AddSingleton(qdrantConfig);
        services.AddSingleton(ragConfig);

        // Core
        services.AddTransient<NormalizePromptTask>();

        // Infrastructure - LLM
        services.AddSingleton<GeminiClient>();
        services.AddSingleton<GeminiEmbeddingClient>();
        services.AddSingleton<LLMClientFactory>();
        services.AddSingleton<EmbeddingClientFactory>();
        services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<LLMClientFactory>().CreateClient());
        services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<EmbeddingClientFactory>().CreateClient());

        // Infrastructure - Database
        services.AddSingleton<Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();
        services.AddSingleton<DatabaseAdapterFactory>();
        services.AddSingleton<IDatabaseAdapter>(sp => sp.GetRequiredService<DatabaseAdapterFactory>().CreateAdapter());
        services.AddSingleton<SchemaScanner>();
        services.AddSingleton<SqlExecutor>();

        // Infrastructure - RAG
        services.AddSingleton<QdrantService>();
        services.AddSingleton<SchemaIndexer>();
        services.AddSingleton<SchemaRetriever>();

        // Error Handlers
        Infrastructure.ErrorHandling.ErrorHandlerServiceExtensions.AddErrorHandlers(services);

        // Plugins (for baseline)
        services.AddTransient<Plugins.IntentAnalysisPlugin>();
        services.AddTransient<Plugins.SqlGeneratorPlugin>();
        services.AddTransient<Plugins.SqlCorrectorPlugin>();

        // Baseline Agent
        services.AddSingleton<Application.Services.TextToSqlAgentOrchestrator>();

        // ReAct Agent with Tools
        services.AddReActAgent();

        // Phase 2: Advanced RAG Components
        services.AddAdvancedRAG();

        // Evaluation
        services.AddSingleton<MetricsCalculator>();
        services.AddSingleton<TextToSqlAgent.Evaluation.Validators.ResultValidator>(); // P1-08
        services.AddSingleton<BaselineEvaluator>();
        services.AddSingleton<ReActAgentEvaluator>();
        services.AddSingleton<ReportGenerator>();

        return services.BuildServiceProvider();
    }
}
