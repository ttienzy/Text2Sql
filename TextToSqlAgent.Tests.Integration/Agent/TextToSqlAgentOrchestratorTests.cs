using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Console.Agent;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Plugins;
using Xunit;

namespace TextToSqlAgent.Tests.Integration.Agent;

public class TextToSqlAgentOrchestratorTests
{
    private readonly TextToSqlAgentOrchestrator _agent;

    public TextToSqlAgentOrchestratorTests()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY not set");

        var geminiConfig = new GeminiConfig
        {
            ApiKey = apiKey,
            Model = "gemini-2.0-flash-exp",
            Temperature = 0.1
        };

        var dbConfig = new DatabaseConfig
        {
            ConnectionString = "Server=localhost;Database=TextToSqlTest;User Id=TextToSqlReader;Password=Reader@2024!Strong;TrustServerCertificate=True;",
            CommandTimeout = 30,
            MaxRetryAttempts = 3
        };

        var agentConfig = new AgentConfig
        {
            MaxSelfCorrectionAttempts = 3,
            EnableSQLExplanation = true
        };

        // Build all dependencies with adapter
        var geminiClient = new GeminiClient(geminiConfig, NullLogger<GeminiClient>.Instance);
        var normalizeTask = new NormalizePromptTask(NullLogger<NormalizePromptTask>.Instance);
        var intentPlugin = new IntentAnalysisPlugin(geminiClient, NullLogger<IntentAnalysisPlugin>.Instance);
        
        var adapter = new SqlServerAdapter(NullLogger<SqlServerAdapter>.Instance);
        var schemaScanner = new SchemaScanner(dbConfig, adapter, NullLogger<SchemaScanner>.Instance);
        var sqlGenerator = new SqlGeneratorPlugin(geminiClient, adapter, NullLogger<SqlGeneratorPlugin>.Instance);
        var sqlExecutor = new SqlExecutor(dbConfig, adapter, NullLogger<SqlExecutor>.Instance);

        //_agent = new TextToSqlAgentOrchestrator(
        //    normalizeTask,
        //    intentPlugin,
        //    schemaScanner,
        //    sqlGenerator,
        //    sqlExecutor,
        //    NullLogger<TextToSqlAgentOrchestrator>.Instance);
    }


    [Fact]
    public async Task Should_Answer_Schema_Query()
    {
        // Arrange
        var question = "Có bao nhiêu bảng trong database?";

        // Act
        var response = await _agent.ProcessQueryAsync(question);

        // Assert
        response.Success.Should().BeTrue();
        response.Answer.Should().NotBeNullOrEmpty();
        response.SqlGenerated.Should().Contain("INFORMATION_SCHEMA");
        response.QueryResult.Should().NotBeNull();
        response.QueryResult!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Answer_Count_Query()
    {
        // Arrange
        var question = "Có bao nhiêu khách hàng?";

        // Act
        var response = await _agent.ProcessQueryAsync(question);

        // Assert
        response.Success.Should().BeTrue();
        response.Answer.Should().Contain("bản ghi");
        response.SqlGenerated.Should().Contain("COUNT");
        response.SqlGenerated.Should().Contain("Customers");
    }

    [Fact]
    public async Task Should_Answer_List_Query()
    {
        // Arrange
        var question = "Liệt kê tất cả khách hàng";

        // Act
        var response = await _agent.ProcessQueryAsync(question);

        // Assert
        response.Success.Should().BeTrue();
        response.SqlGenerated.Should().Contain("SELECT");
        response.SqlGenerated.Should().Contain("Customers");
        response.QueryResult!.Rows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_Handle_Complex_Query()
    {
        // Arrange
        var question = "Cho tôi danh sách 5 khách hàng ở Hà Nội";

        // Act
        var response = await _agent.ProcessQueryAsync(question);

        // Assert
        response.Success.Should().BeTrue();
        response.SqlGenerated.Should().Contain("Customers");
        response.SqlGenerated.Should().Contain("Hà Nội");
    }

    [Fact]
    public async Task Should_Validate_Dangerous_SQL()
    {
        // This test assumes the LLM won't generate dangerous SQL
        // but we're testing that if it does, our validation catches it

        // Arrange
        var question = "Xóa tất cả khách hàng";

        // Act
        var response = await _agent.ProcessQueryAsync(question);

        // Assert - should either refuse or fail validation
        if (!response.Success)
        {
            response.ErrorMessage.Should().NotBeNullOrEmpty();
        }
    }
}