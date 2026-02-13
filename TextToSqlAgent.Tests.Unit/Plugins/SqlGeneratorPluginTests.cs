using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Plugins;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.Plugins;

public class SqlGeneratorPluginTests
{
    private readonly SqlGeneratorPlugin _plugin;
    private readonly DatabaseSchema _testSchema;

    public SqlGeneratorPluginTests()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY not set");

        var config = new GeminiConfig
        {
            ApiKey = apiKey,
            Model = "gemini-2.5-flash",
            Temperature = 0.1
        };

        var geminiClient = new GeminiClient(config, NullLogger<GeminiClient>.Instance);
        //_plugin = new SqlGeneratorPlugin(geminiClient, NullLogger<SqlGeneratorPlugin>.Instance);

        // Setup test schema
        _testSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Customers",
                    Schema = "dbo",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnInfo { ColumnName = "Name", DataType = "nvarchar", MaxLength = 100, IsNullable = false },
                        new ColumnInfo { ColumnName = "Email", DataType = "nvarchar", MaxLength = 100, IsNullable = true },
                        new ColumnInfo { ColumnName = "City", DataType = "nvarchar", MaxLength = 50, IsNullable = true }
                    },
                    PrimaryKeys = new List<string> { "Id" }
                },
                new TableInfo
                {
                    TableName = "Orders",
                    Schema = "dbo",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "CustomerId", DataType = "int", IsForeignKey = true },
                        new ColumnInfo { ColumnName = "OrderDate", DataType = "datetime" },
                        new ColumnInfo { ColumnName = "TotalAmount", DataType = "decimal" }
                    }
                }
            },
            Relationships = new List<RelationshipInfo>
            {
                new RelationshipInfo
                {
                    FromTable = "Orders",
                    FromColumn = "CustomerId",
                    ToTable = "Customers",
                    ToColumn = "Id"
                }
            }
        };
    }

    [Fact]
    public async Task Should_Generate_SQL_For_Count_Intent()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.COUNT,
            Target = "Customers"
        };

        // Act
        var sql = await _plugin.GenerateSqlAsync(intent, _testSchema);

        // Assert
        sql.Should().NotBeNullOrEmpty();
        sql.ToUpper().Should().Contain("SELECT");
        sql.ToUpper().Should().Contain("COUNT");
        sql.ToUpper().Should().Contain("CUSTOMERS");
    }

    [Fact]
    public async Task Should_Generate_SQL_For_List_Intent()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.LIST,
            Target = "Customers"
        };

        // Act
        var sql = await _plugin.GenerateSqlAsync(intent, _testSchema);

        // Assert
        sql.Should().NotBeNullOrEmpty();
        sql.ToUpper().Should().Contain("SELECT");
        sql.ToUpper().Should().Contain("CUSTOMERS");
    }

    [Fact]
    public async Task Should_Generate_SQL_For_Schema_Intent()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.SCHEMA,
            Target = "TABLES"
        };

        // Act
        var sql = await _plugin.GenerateSqlAsync(intent, _testSchema);

        // Assert
        sql.Should().NotBeNullOrEmpty();
        sql.ToUpper().Should().Contain("INFORMATION_SCHEMA");
        sql.ToUpper().Should().Contain("TABLES");
    }

    [Theory]
    [InlineData("SELECT * FROM Customers", true)]
    [InlineData("SELECT COUNT(*) FROM Orders", true)]
    [InlineData("DELETE FROM Customers", false)]
    [InlineData("DROP TABLE Customers", false)]
    [InlineData("UPDATE Customers SET Name = 'Test'", false)]
    [InlineData("INSERT INTO Customers VALUES ('Test')", false)]
    public void Should_Validate_SQL_Safety(string sql, bool expectedValid)
    {
        // Act
        var isValid = _plugin.ValidateSql(sql);

        // Assert
        isValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("SELECT * FROM Customers", "SELECT TOP 100 * FROM Customers")]
    [InlineData("SELECT TOP 10 * FROM Customers", "SELECT TOP 10 * FROM Customers")]
    [InlineData("SELECT COUNT(*) FROM Customers", "SELECT COUNT(*) FROM Customers")]
    public void Should_Ensure_Limit(string input, string expected)
    {
        // Act
        var result = _plugin.EnsureLimit(input, 100);

        // Assert
        result.Should().Be(expected);
    }
}