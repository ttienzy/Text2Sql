using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;
using Xunit;

namespace TextToSqlAgent.Tests.Integration.Database;

public class SchemaScannerTests
{
    private readonly SchemaScanner _scanner;
    private readonly DatabaseConfig _config;

    public SchemaScannerTests()
    {
        // P1-07: Use docker-compose connection string from environment
        _config = TestConfiguration.CreateDatabaseConfig();

        var adapter = new SqlServerAdapter(NullLogger<SqlServerAdapter>.Instance);
        _scanner = new SchemaScanner(_config, adapter, NullLogger<SchemaScanner>.Instance);
    }

    [Fact]
    public async Task Should_Connect_To_Database()
    {
        // Act
        var canConnect = await _scanner.TestConnectionAsync();

        // Assert
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Scan_All_Tables()
    {
        // Act
        var schema = await _scanner.ScanAsync();

        // Assert
        schema.Tables.Should().NotBeEmpty();
        schema.Tables.Should().Contain(t => t.TableName == "Customers");
        schema.Tables.Should().Contain(t => t.TableName == "Orders");
        schema.Tables.Should().Contain(t => t.TableName == "Products");
    }

    [Fact]
    public async Task Should_Scan_Columns_For_Each_Table()
    {
        // Act
        var schema = await _scanner.ScanAsync();
        var customersTable = schema.Tables.First(t => t.TableName == "Customers");

        // Assert
        customersTable.Columns.Should().NotBeEmpty();
        customersTable.Columns.Should().Contain(c => c.ColumnName == "CustomerId");
        customersTable.Columns.Should().Contain(c => c.ColumnName == "FullName");
        customersTable.Columns.Should().Contain(c => c.ColumnName == "Email");
    }

    [Fact]
    public async Task Should_Detect_Primary_Keys()
    {
        // Act
        var schema = await _scanner.ScanAsync();
        var customersTable = schema.Tables.First(t => t.TableName == "Customers");

        // Assert
        customersTable.PrimaryKeys.Should().Contain("CustomerId");

        var idColumn = customersTable.Columns.First(c => c.ColumnName == "CustomerId");
        idColumn.IsPrimaryKey.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Detect_Relationships()
    {
        // Act
        var schema = await _scanner.ScanAsync();

        // Assert
        schema.Relationships.Should().NotBeEmpty();
        schema.Relationships.Should().Contain(r =>
            r.FromTable.Contains("Orders") && r.ToTable.Contains("Customers"));
        schema.Relationships.Should().Contain(r =>
            r.FromTable.Contains("OrderDetails") && r.ToTable.Contains("Orders"));
    }

    [Fact]
    public async Task Should_Include_Data_Types()
    {
        // Act
        var schema = await _scanner.ScanAsync();
        var customersTable = schema.Tables.First(t => t.TableName == "Customers");
        var idColumn = customersTable.Columns.First(c => c.ColumnName == "CustomerId");

        // Assert
        idColumn.DataType.Should().Be("int");
    }
}