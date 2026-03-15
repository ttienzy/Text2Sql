using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.VectorDB;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.VectorDB;

/// <summary>
/// Tests to verify that all components generate identical collection names for the same database.
/// This validates Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6 from the design document.
/// </summary>
public class CollectionNameConsistencyTests
{
    [Theory]
    [InlineData("MyDatabase")]
    [InlineData("test-db")]
    [InlineData("Sales_DB-2024.Production")]
    [InlineData("db@server")]
    [InlineData("UPPERCASE")]
    public void AllComponents_WithSameDatabaseName_GenerateIdenticalCollectionNames(string databaseName)
    {
        // Arrange
        var config = new QdrantConfig
        {
            Host = "localhost",
            Port = 6333,
            VectorSize = 1536
        };

        // Act - Generate collection name using QdrantService
        var qdrantService = new QdrantService(config, NullLogger<QdrantService>.Instance);
        qdrantService.SetCollectionName(databaseName);
        var qdrantCollectionName = qdrantService.GetCurrentCollectionName();

        // Act - Generate collection name using shared utility
        var utilityCollectionName = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert - Both should produce identical results
        Assert.Equal(utilityCollectionName, qdrantCollectionName);
    }

    [Fact]
    public void CollectionNameGeneration_IsIdempotent()
    {
        // Arrange
        var databaseName = "TestDatabase-123";
        var config = new QdrantConfig
        {
            Host = "localhost",
            Port = 6333,
            VectorSize = 1536
        };

        // Act - Generate collection name multiple times
        var qdrantService1 = new QdrantService(config, NullLogger<QdrantService>.Instance);
        qdrantService1.SetCollectionName(databaseName);
        var result1 = qdrantService1.GetCurrentCollectionName();

        var qdrantService2 = new QdrantService(config, NullLogger<QdrantService>.Instance);
        qdrantService2.SetCollectionName(databaseName);
        var result2 = qdrantService2.GetCurrentCollectionName();

        var result3 = CollectionNameHelper.NormalizeCollectionName(databaseName);
        var result4 = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert - All results should be identical
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
        Assert.Equal(result3, result4);
    }



    [Fact]
    public void CollectionName_FollowsExpectedFormat()
    {
        // Arrange
        var databaseName = "MyDatabase";
        var expectedPrefix = "schema_embeddings_";

        // Act
        var collectionName = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.StartsWith(expectedPrefix, collectionName);
        Assert.Equal("schema_embeddings_mydatabase", collectionName);
    }

    [Fact]
    public void CollectionName_IsLowercase()
    {
        // Arrange
        var databaseName = "UPPERCASE_DATABASE";

        // Act
        var collectionName = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert
        Assert.Equal(collectionName.ToLowerInvariant(), collectionName);
    }

    [Fact]
    public void CollectionName_ContainsNoSpecialCharacters()
    {
        // Arrange
        var databaseName = "my-db@server.com:1234";

        // Act
        var collectionName = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert - Should only contain letters, digits, and underscores
        Assert.Matches("^[a-z0-9_]+$", collectionName);
    }

    [Fact]
    public void QdrantService_UsesSharedUtility_ForCollectionNameGeneration()
    {
        // Arrange
        var databaseName = "TestDB-2024";
        var config = new QdrantConfig
        {
            Host = "localhost",
            Port = 6333,
            VectorSize = 1536
        };

        // Act
        var qdrantService = new QdrantService(config, NullLogger<QdrantService>.Instance);
        qdrantService.SetCollectionName(databaseName);
        var qdrantResult = qdrantService.GetCurrentCollectionName();

        var utilityResult = CollectionNameHelper.NormalizeCollectionName(databaseName);

        // Assert - QdrantService should use the shared utility internally
        Assert.Equal(utilityResult, qdrantResult);
    }
}
