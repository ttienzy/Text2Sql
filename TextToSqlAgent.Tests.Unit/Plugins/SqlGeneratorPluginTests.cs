using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Plugins;
using TextToSqlAgent.Tests.Unit.Mocks;
using Xunit;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;

namespace TextToSqlAgent.Tests.Unit.Plugins;

/// <summary>
/// P1-06: Refactored unit tests using mock LLM client (no real API calls)
/// </summary>
public class SqlGeneratorPluginTests
{
    private readonly MockLLMClient _mockLlm;
    private readonly SqlGeneratorPlugin _plugin;
    private readonly DatabaseSchema _testSchema;

    public SqlGeneratorPluginTests()
    {
        _mockLlm = new MockLLMClient();
        var adapter = new SqlServerAdapter(NullLogger<SqlServerAdapter>.Instance);
        _plugin = new SqlGeneratorPlugin(_mockLlm, adapter, NullLogger<SqlGeneratorPlugin>.Instance);

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
        _mockLlm.SetResponse("COUNT", "SELECT COUNT(*) AS Total FROM Customers");

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
        _mockLlm.SetResponse("LIST", "SELECT * FROM Customers");

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
        _mockLlm.SetResponse("SCHEMA", "SELECT * FROM INFORMATION_SCHEMA.TABLES");

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

    [Fact]
    public async Task Should_Generate_SQL_With_Suggestions()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.GROUP_BY,
            Target = "Orders"
        };

        var schemaContext = new RetrievedSchemaContext();
        schemaContext.RelevantTables.Add(_testSchema.Tables.First(t => t.TableName == "Orders"));

        // Mock LLM response with JSON format including suggestions
        var mockJsonResponse = @"{
            ""sql"": ""SELECT Status, PaymentMethod, COUNT(*) AS TotalOrders FROM Orders GROUP BY Status, PaymentMethod"",
            ""suggested_queries"": [
                ""Show orders by status only"",
                ""Compare revenue by payment method"",
                ""List top customers by order count""
            ]
        }";

        _mockLlm.SetResponse("GROUP_BY", mockJsonResponse);

        // Act
        var result = await _plugin.GenerateSqlWithContextAsync(intent, schemaContext, "Show orders by status and payment method");

        // Assert
        result.Should().NotBeNull();
        result.Sql.Should().NotBeNullOrEmpty();
        result.Sql.ToUpper().Should().Contain("SELECT");
        result.Sql.ToUpper().Should().Contain("GROUP BY");

        // ✅ Test suggestions
        result.SuggestedQueries.Should().NotBeNull();
        result.SuggestedQueries.Should().HaveCount(3);
        result.SuggestedQueries[0].Should().Be("Show orders by status only");
        result.SuggestedQueries[1].Should().Be("Compare revenue by payment method");
        result.SuggestedQueries[2].Should().Be("List top customers by order count");
    }

    [Fact]
    public async Task Should_Handle_Invalid_JSON_Response_Gracefully()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.LIST,
            Target = "Customers"
        };

        var schemaContext = new RetrievedSchemaContext();
        schemaContext.RelevantTables.Add(_testSchema.Tables.First(t => t.TableName == "Customers"));

        // Mock invalid JSON response (should fallback to treating as raw SQL)
        var invalidJsonResponse = "SELECT * FROM Customers";
        _mockLlm.SetResponse("LIST", invalidJsonResponse);

        // Act
        var result = await _plugin.GenerateSqlWithContextAsync(intent, schemaContext, "List all customers");

        // Assert
        result.Should().NotBeNull();
        result.Sql.Should().NotBeNullOrEmpty();
        result.Sql.Should().Be("SELECT * FROM Customers");

        // Should have empty suggestions when JSON parsing fails
        result.SuggestedQueries.Should().NotBeNull();
        result.SuggestedQueries.Should().BeEmpty();
    }
}
    [Fact]
    public async Task Should_Handle_Alternative_Suggestion_Keys()
    {
        // Arrange
        var intent = new IntentAnalysis
        {
            Intent = QueryIntent.COUNT,
            Target = "Orders"
        };

        var schemaContext = new RetrievedSchemaContext();
        schemaContext.RelevantTables.Add(_testSchema.Tables.First(t => t.TableName == "Orders"));

        // Mock LLM response with alternative key "suggestions" instead of "suggested_queries"
        var mockJsonResponse = @"{
            ""sql"": ""SELECT COUNT(*) AS TotalOrders FROM Orders"",
            ""suggestions"": [
                ""Show orders by status"",
                ""List recent orders"",
                ""Count orders by customer""
            ]
        }";

        _mockLlm.SetResponse("COUNT", mockJsonResponse);

        // Act
        var result = await _plugin.GenerateSqlWithContextAsync(intent, schemaContext, "Count total orders");

        // Assert
        result.Should().NotBeNull();
        result.Sql.Should().NotBeNullOrEmpty();
        result.Sql.ToUpper().Should().Contain("COUNT");

        // ✅ Should find suggestions under alternative key
        result.SuggestedQueries.Should().NotBeNull();
        result.SuggestedQueries.Should().HaveCount(3);
        result.SuggestedQueries[0].Should().Be("Show orders by status");
        result.SuggestedQueries[1].Should().Be("List recent orders");
        result.SuggestedQueries[2].Should().Be("Count orders by customer");
    }