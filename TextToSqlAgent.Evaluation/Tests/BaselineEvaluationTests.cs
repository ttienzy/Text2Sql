using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Evaluation.Datasets;
using TextToSqlAgent.Evaluation.Metrics;
using TextToSqlAgent.Evaluation.Reports;
using TextToSqlAgent.Evaluation.Runners;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Factories;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Evaluation.Tests;

public class BaselineEvaluationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;

    public BaselineEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
        _serviceProvider = BuildServiceProvider();
    }

    [Fact]
    public async Task RunBaselineEvaluation()
    {
        // Arrange
        var evaluator = _serviceProvider.GetRequiredService<BaselineEvaluator>();
        var reportGenerator = _serviceProvider.GetRequiredService<ReportGenerator>();
        var examples = SampleDataset.GetExamples();

        // Act
        var report = await evaluator.RunEvaluationAsync(examples);

        // Generate reports
        var consoleReport = reportGenerator.GenerateConsoleReport(report);
        _output.WriteLine(consoleReport);

        // Save reports
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        await reportGenerator.SaveJsonReportAsync(report, $"baseline_report_{timestamp}.json");
        await reportGenerator.SaveCsvReportAsync(report, $"baseline_report_{timestamp}.csv");

        // Assert - Basic sanity checks
        Assert.True(report.TotalExamples > 0, "Should have examples");
        Assert.InRange(report.ExecutionAccuracy, 0, 100);

        _output.WriteLine($"\n✓ Baseline evaluation complete!");
        _output.WriteLine($"  Execution Accuracy: {report.ExecutionAccuracy:F2}%");
        _output.WriteLine($"  Avg Latency: {report.AvgLatencyMs:F0}ms");
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

        // Configuration (load from appsettings or environment)
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
                              "Server=localhost;Database=TestDB;Integrated Security=true;TrustServerCertificate=true;",
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

        // Plugins
        services.AddTransient<IntentAnalysisPlugin>();
        services.AddTransient<SqlGeneratorPlugin>();
        services.AddTransient<SqlCorrectorPlugin>();

        // Agent
        services.AddSingleton<TextToSqlAgentOrchestrator>();

        // Evaluation
        services.AddSingleton<MetricsCalculator>();
        services.AddSingleton<TextToSqlAgent.Evaluation.Validators.ResultValidator>(); // P1-08
        services.AddSingleton<BaselineEvaluator>();
        services.AddSingleton<ReportGenerator>();

        return services.BuildServiceProvider();
    }
}
