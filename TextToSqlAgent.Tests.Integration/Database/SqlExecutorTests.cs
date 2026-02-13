using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;
using Xunit;

namespace TextToSqlAgent.Tests.Integration.Database;

public class SqlExecutorTests
{
    private readonly SqlExecutor _executor;

    public SqlExecutorTests()
    {
        var config = new DatabaseConfig
        {
            ConnectionString = "Server=.;Database=TextToSqlTest;User Id=TextToSqlReader;Password=@TextToSqlReader!;TrustServerCertificate=True;",
            CommandTimeout = 30,
            MaxRetryAttempts = 3
        };

        var adapter = new SqlServerAdapter(NullLogger<SqlServerAdapter>.Instance);
        _executor = new SqlExecutor(config, adapter, NullLogger<SqlExecutor>.Instance);
    }

    [Fact]
    public async Task Should_Validate_Connection()
    {
        // Act
        var isValid = await _executor.ValidateConnectionAsync();

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Execute_Simple_Select()
    {
        // Arrange
        var sql = "SELECT COUNT(*) AS TableCount FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";

        // Act
        var result = await _executor.ExecuteAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Rows.Should().HaveCount(1);
        result.Columns.Should().Contain("TableCount");
    }

    [Fact]
    public async Task Should_Execute_Multi_Column_Query()
    {
        // Arrange
        var sql = "SELECT TOP 5 Name, Email, City FROM Customers";

        // Act
        var result = await _executor.ExecuteAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Columns.Should().Contain(new[] { "Name", "Email", "City" });
        result.Rows.Should().NotBeEmpty();
        result.Rows.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Should_Handle_Invalid_SQL()
    {
        // Arrange
        var sql = "SELECT * FROM NonExistentTable";

        // Act
        var result = await _executor.ExecuteAsync(sql);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().Contain("Invalid object name");
    }

    [Fact]
    public async Task Should_Handle_Syntax_Error()
    {
        // Arrange
        var sql = "SELCT * FORM Customers";
        // Act
        var result = await _executor.ExecuteAsync(sql);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Incorrect syntax");
    }

    [Fact]
    public async Task Should_Return_Empty_Result_For_No_Rows()
    {
        // Arrange
        var sql = "SELECT * FROM Customers WHERE Id = -999999";

        // Act
        var result = await _executor.ExecuteAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Rows.Should().BeEmpty();
        result.Columns.Should().NotBeEmpty();
    }
}