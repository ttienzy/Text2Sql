using TextToSqlAgent.Infrastructure.VectorDB;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.VectorDB;

public class CollectionNameHelperTests
{
    [Fact]
    public void NormalizeCollectionName_WithSimpleDatabaseName_ReturnsCorrectFormat()
    {
        // Arrange
        var databaseName = "MyDatabase";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_mydatabase", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithUppercaseName_ConvertsToLowercase()
    {
        // Arrange
        var databaseName = "TESTDB";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_testdb", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithSpecialCharacters_ReplacesWithUnderscores()
    {
        // Arrange
        var databaseName = "my-database.test";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_my_database_test", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithMultipleSpecialChars_RemovesConsecutiveUnderscores()
    {
        // Arrange
        var databaseName = "my---database";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_my_database", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithLeadingTrailingSpecialChars_TrimsUnderscores()
    {
        // Arrange
        var databaseName = "-mydb-";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_mydb", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithMixedCaseAndNumbers_NormalizesCorrectly()
    {
        // Arrange
        var databaseName = "MyDB123";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_mydb123", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithSpaces_ReplacesWithUnderscores()
    {
        // Arrange
        var databaseName = "my database name";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_my_database_name", result);
    }

    [Fact]
    public void NormalizeCollectionName_WithNullOrEmpty_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            CollectionNameHelper.NormalizeCollectionName(null!));

        Assert.Throws<ArgumentException>(() =>
            CollectionNameHelper.NormalizeCollectionName(""));

        Assert.Throws<ArgumentException>(() =>
            CollectionNameHelper.NormalizeCollectionName("   "));
    }

    [Fact]
    public void NormalizeCollectionName_WithComplexName_ProducesConsistentResult()
    {
        // Arrange
        var databaseName = "Sales_DB-2024.Production";

        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal("schema_embeddings_sales_db_2024_production", result);
    }

    [Theory]
    [InlineData("TestDB", "schema_embeddings_testdb")]
    [InlineData("test_db", "schema_embeddings_test_db")]
    [InlineData("TEST-DB", "schema_embeddings_test_db")]
    [InlineData("test.db", "schema_embeddings_test_db")]
    [InlineData("123db", "schema_embeddings_123db")]
    [InlineData("db@server", "schema_embeddings_db_server")]
    public void NormalizeCollectionName_WithVariousInputs_ProducesExpectedOutput(
        string input,
        string expected)
    {
        // Act
        var result = CollectionNameHelper.NormalizeCollectionName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeCollectionName_CalledMultipleTimes_ProducesIdenticalResults()
    {
        // Arrange
        var databaseName = "MyDatabase-Test";

        // Act
        var result1 = CollectionNameHelper.NormalizeCollectionName(databaseName);
        var result2 = CollectionNameHelper.NormalizeCollectionName(databaseName);
        var result3 = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert - Idempotency check
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }
}
