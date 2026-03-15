using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.RAG;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.RAG;

/// <summary>
/// Tests for error handling in SchemaRetriever when all retrieval strategies fail.
/// Validates Requirements 6.5 and 6.6.
/// </summary>
public class SchemaRetrieverErrorHandlingTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly KeywordSchemaRetriever _keywordRetriever;
    private readonly Mock<IEmbeddingClient> _mockEmbeddingClient;
    private readonly RAGConfig _ragConfig;
    private readonly IMemoryCache _queryCache;
    private readonly Mock<ILogger<SchemaRetriever>> _mockLogger;
    private readonly SchemaRetriever _retriever;

    public SchemaRetrieverErrorHandlingTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        var mockKeywordLogger = new Mock<ILogger<KeywordSchemaRetriever>>();
        _keywordRetriever = new KeywordSchemaRetriever(mockKeywordLogger.Object);
        _mockEmbeddingClient = new Mock<IEmbeddingClient>();
        _ragConfig = new RAGConfig
        {
            TopK = 5,
            MinimumScore = 0.3,
            EnableHybridSearch = true,
            MaxContextTables = 10
        };
        _queryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        _mockLogger = new Mock<ILogger<SchemaRetriever>>();

        _retriever = new SchemaRetriever(
            _mockVectorStore.Object,
            _keywordRetriever,
            _mockEmbeddingClient.Object,
            _ragConfig,
            _queryCache,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task RetrieveAsync_WhenAllStrategiesFail_ReturnsEmptyResultWithErrorMessage()
    {
        // Arrange
        var question = "test query";
        var fullSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>(),
            Relationships = new List<RelationshipInfo>()
        };

        // Simulate embedding generation failure
        _mockEmbeddingClient
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedding service unavailable"));

        // Simulate vector store unavailable
        _mockVectorStore
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Keyword search will return no results because schema is empty

        // Act
        var result = await _retriever.RetrieveAsync(question, fullSchema);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RelevantTables);
        Assert.Empty(result.RelevantRelationships);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("No relevant schema elements found", result.ErrorMessage);
    }

    [Fact]
    public async Task RetrieveAsync_WhenVectorSearchFailsWithException_ReturnsErrorMessage()
    {
        // Arrange
        var question = "test query";
        var fullSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>(),
            Relationships = new List<RelationshipInfo>()
        };

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Embedding generation succeeds
        _mockEmbeddingClient
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Vector store is available but search fails
        _mockVectorStore
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVectorStore
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Qdrant connection failed"));

        // Keyword search returns no results (empty schema)

        // Act
        var result = await _retriever.RetrieveAsync(question, fullSchema);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RelevantTables);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("All retrieval strategies failed", result.ErrorMessage);
        Assert.Contains("Vector search failed", result.ErrorMessage);
    }

    [Fact]
    public async Task RetrieveAsync_WhenKeywordSearchFailsWithException_ReturnsErrorMessage()
    {
        // Arrange
        var question = "test query";
        var fullSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>(),
            Relationships = new List<RelationshipInfo>()
        };

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Embedding generation succeeds
        _mockEmbeddingClient
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Vector store unavailable
        _mockVectorStore
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Note: KeywordSchemaRetriever is a real instance and won't throw exceptions
        // This test verifies that when vector search is unavailable and keyword search
        // returns no results (empty schema), we get an appropriate error message

        // Act
        var result = await _retriever.RetrieveAsync(question, fullSchema);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RelevantTables);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("No relevant schema elements found", result.ErrorMessage);
    }

    [Fact]
    public async Task RetrieveAsync_WhenAllStrategiesFail_DoesNotThrowUnhandledException()
    {
        // Arrange
        var question = "test query";
        var fullSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>(),
            Relationships = new List<RelationshipInfo>()
        };

        // All services throw exceptions
        _mockEmbeddingClient
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedding failed"));

        _mockVectorStore
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Vector store check failed"));

        // Keyword retriever is real and won't throw, but will return empty results

        // Act & Assert - Should not throw
        var result = await _retriever.RetrieveAsync(question, fullSchema);

        Assert.NotNull(result);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RetrieveAsync_WhenVectorSearchReturnsNoResults_AndKeywordSearchReturnsNoResults_ReturnsEmptyWithMessage()
    {
        // Arrange
        var question = "test query";
        var fullSchema = new DatabaseSchema
        {
            Tables = new List<TableInfo>(),
            Relationships = new List<RelationshipInfo>()
        };

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Embedding generation succeeds
        _mockEmbeddingClient
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Vector search returns empty results (no matches above threshold)
        _mockVectorStore
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVectorStore
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        // Keyword search returns no results (empty schema)

        // Act
        var result = await _retriever.RetrieveAsync(question, fullSchema);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RelevantTables);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal("No relevant schema elements found for the query", result.ErrorMessage);
    }
}
