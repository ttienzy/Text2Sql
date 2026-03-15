using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.RAG;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.RAG;

/// <summary>
/// Unit tests for SchemaIndexer enhanced chunking functionality (Task 6)
/// </summary>
public class SchemaIndexerEnhancedChunkingTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<IEmbeddingClient> _mockEmbeddingClient;
    private readonly Mock<ILogger<SchemaIndexer>> _mockLogger;
    private readonly SchemaIndexer _indexer;

    public SchemaIndexerEnhancedChunkingTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        _mockEmbeddingClient = new Mock<IEmbeddingClient>();
        _mockLogger = new Mock<ILogger<SchemaIndexer>>();

        _indexer = new SchemaIndexer(
            _mockVectorStore.Object,
            _mockEmbeddingClient.Object,
            _mockLogger.Object);
    }

    private static SchemaFingerprint CreateTestFingerprint(DatabaseSchema schema)
    {
        return new SchemaFingerprint
        {
            Hash = "test-hash",
            ComputedAt = DateTime.UtcNow,
            TableCount = schema.Tables.Count,
            ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
            RelationshipCount = schema.Relationships.Count,
            TableNames = schema.Tables.Select(t => t.TableName).OrderBy(n => n).ToList()
        };
    }

    [Fact]
    public async Task IndexSchemaAsync_WithTableDescriptions_UsesProvidedDescriptions()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Customers",
                    Description = "Customer master data",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "Name", DataType = "nvarchar", Description = "Full customer name" }
                    }
                }
            },
            Relationships = new List<RelationshipInfo>()
        };

        var capturedPoints = new List<VectorPoint>();
        _mockVectorStore.Setup(x => x.EnsureCollectionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.UpsertPointsAsync(It.IsAny<List<VectorPoint>>(), It.IsAny<CancellationToken>()))
            .Callback<List<VectorPoint>, CancellationToken>((points, ct) => capturedPoints.AddRange(points))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.StoreSchemaFingerprintAsync(It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEmbeddingClient.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var fingerprint = CreateTestFingerprint(schema);

        // Act
        var result = await _indexer.IndexSchemaAsync(schema, fingerprint, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);
        Assert.NotEmpty(capturedPoints);

        // Verify table chunk contains description
        var tablePoint = capturedPoints.FirstOrDefault(p => p.Payload["type"].ToString() == "table");
        Assert.NotNull(tablePoint);
        var tableContent = tablePoint.Payload["content"].ToString();
        Assert.Contains("Customer master data", tableContent);
        Assert.Contains("Table: Customers", tableContent);
        Assert.Contains("Description: Customer master data", tableContent);

        // Verify column chunk contains description
        var columnPoint = capturedPoints.FirstOrDefault(p =>
            p.Payload["type"].ToString() == "column" &&
            p.Payload["column_name"].ToString() == "Name");
        Assert.NotNull(columnPoint);
        var columnContent = columnPoint.Payload["content"].ToString();
        Assert.Contains("Full customer name", columnContent);
        Assert.Contains("Column: Customers.Name", columnContent);
    }

    [Fact]
    public async Task IndexSchemaAsync_WithoutDescriptions_InfersPurpose()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Orders",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "CustomerId", DataType = "int", IsForeignKey = true }
                    }
                }
            },
            Relationships = new List<RelationshipInfo>()
        };

        var capturedPoints = new List<VectorPoint>();
        _mockVectorStore.Setup(x => x.EnsureCollectionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.UpsertPointsAsync(It.IsAny<List<VectorPoint>>(), It.IsAny<CancellationToken>()))
            .Callback<List<VectorPoint>, CancellationToken>((points, ct) => capturedPoints.AddRange(points))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.StoreSchemaFingerprintAsync(It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEmbeddingClient.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var fingerprint = CreateTestFingerprint(schema);

        // Act
        var result = await _indexer.IndexSchemaAsync(schema, fingerprint, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);
        Assert.NotEmpty(capturedPoints);

        // Verify table chunk infers purpose from name
        var tablePoint = capturedPoints.FirstOrDefault(p => p.Payload["type"].ToString() == "table");
        Assert.NotNull(tablePoint);
        var tableContent = tablePoint.Payload["content"].ToString();
        Assert.Contains("stores order information", tableContent);

        // Verify column chunk infers purpose
        var idColumnPoint = capturedPoints.FirstOrDefault(p =>
            p.Payload["type"].ToString() == "column" &&
            p.Payload["column_name"].ToString() == "Id");
        Assert.NotNull(idColumnPoint);
        var idColumnContent = idColumnPoint.Payload["content"].ToString();
        Assert.Contains("unique identifier", idColumnContent);
    }

    [Fact]
    public async Task IndexSchemaAsync_WithRelationships_GeneratesSemanticDescription()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Orders",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "CustomerId", DataType = "int", IsForeignKey = true }
                    }
                },
                new TableInfo
                {
                    TableName = "Customers",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Id", DataType = "int", IsPrimaryKey = true }
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

        var capturedPoints = new List<VectorPoint>();
        _mockVectorStore.Setup(x => x.EnsureCollectionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.UpsertPointsAsync(It.IsAny<List<VectorPoint>>(), It.IsAny<CancellationToken>()))
            .Callback<List<VectorPoint>, CancellationToken>((points, ct) => capturedPoints.AddRange(points))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.StoreSchemaFingerprintAsync(It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEmbeddingClient.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var fingerprint = CreateTestFingerprint(schema);

        // Act
        var result = await _indexer.IndexSchemaAsync(schema, fingerprint, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);
        Assert.NotEmpty(capturedPoints);

        // Verify relationship chunk has semantic description
        var relationshipPoint = capturedPoints.FirstOrDefault(p => p.Payload["type"].ToString() == "relationship");
        Assert.NotNull(relationshipPoint);
        var relationshipContent = relationshipPoint.Payload["content"].ToString();
        Assert.Contains("Relationship: Orders.CustomerId → Customers.Id", relationshipContent);
        Assert.Contains("Meaning:", relationshipContent);
        Assert.Contains("belongs to", relationshipContent);

        // Verify table chunk includes relationships
        var tablePoint = capturedPoints.FirstOrDefault(p =>
            p.Payload["type"].ToString() == "table" &&
            p.Payload["table_name"].ToString() == "Orders");
        Assert.NotNull(tablePoint);
        var tableContent = tablePoint.Payload["content"].ToString();
        Assert.Contains("Relationships:", tableContent);
        Assert.Contains("Orders.CustomerId → Customers.Id", tableContent);
    }

    [Fact]
    public async Task IndexSchemaAsync_ColumnChunk_IncludesParentTableContext()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Products",
                    Description = "Product catalog",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "Price", DataType = "decimal" }
                    }
                }
            },
            Relationships = new List<RelationshipInfo>()
        };

        var capturedPoints = new List<VectorPoint>();
        _mockVectorStore.Setup(x => x.EnsureCollectionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.UpsertPointsAsync(It.IsAny<List<VectorPoint>>(), It.IsAny<CancellationToken>()))
            .Callback<List<VectorPoint>, CancellationToken>((points, ct) => capturedPoints.AddRange(points))
            .Returns(Task.CompletedTask);
        _mockVectorStore.Setup(x => x.StoreSchemaFingerprintAsync(It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEmbeddingClient.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var fingerprint = CreateTestFingerprint(schema);

        // Act
        var result = await _indexer.IndexSchemaAsync(schema, fingerprint, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);
        var columnPoint = capturedPoints.FirstOrDefault(p =>
            p.Payload["type"].ToString() == "column" &&
            p.Payload["column_name"].ToString() == "Price");
        Assert.NotNull(columnPoint);
        var columnContent = columnPoint.Payload["content"].ToString();

        // Verify format: "Column: {table}.{column}, Description: {description}, Type: {data_type}, Table: {table_context}"
        Assert.Contains("Column: Products.Price", columnContent);
        Assert.Contains("Type: decimal", columnContent);
        Assert.Contains("Table: Products (Product catalog)", columnContent);
    }
}
