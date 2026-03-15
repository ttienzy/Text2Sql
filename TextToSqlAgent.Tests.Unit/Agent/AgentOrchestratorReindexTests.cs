namespace TextToSqlAgent.Tests.Unit.Agent;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TextToSqlAgent.Application.Metrics;
using TextToSqlAgent.Application.Pipelines;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using Xunit;

/// <summary>
/// Unit tests for AgentOrchestrator re-indexing functionality (Task 8)
/// Tests Requirements 8.5 and 8.7 from the enhanced-schema-rag-system spec
/// </summary>
public class AgentOrchestratorReindexTests
{
    private readonly Mock<ISchemaCache> _schemaCacheMock;
    private readonly Mock<IQueryClassifier> _queryClassifierMock;
    private readonly Mock<ISimpleQueryPipeline> _simplePipelineMock;
    private readonly Mock<IMediumQueryPipeline> _mediumPipelineMock;
    private readonly Mock<IComplexQueryPipeline> _complexPipelineMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;
    private readonly Mock<QdrantService> _qdrantServiceMock;
    private readonly Mock<SchemaScanner> _schemaScannerMock;
    private readonly Mock<SchemaIndexer> _schemaIndexerMock;
    private readonly AgentOrchestrator _orchestrator;

    public AgentOrchestratorReindexTests()
    {
        _schemaCacheMock = new Mock<ISchemaCache>();
        _queryClassifierMock = new Mock<IQueryClassifier>();
        _simplePipelineMock = new Mock<ISimpleQueryPipeline>();
        _mediumPipelineMock = new Mock<IMediumQueryPipeline>();
        _complexPipelineMock = new Mock<IComplexQueryPipeline>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();

        // Create mocks with constructor arguments
        var qdrantConfig = new QdrantConfig { Host = "localhost", Port = 6333, VectorSize = 1536, CollectionName = "schema_embeddings" };
        var qdrantLogger = NullLogger<QdrantService>.Instance;
        _qdrantServiceMock = new Mock<QdrantService>(qdrantConfig, qdrantLogger);

        var schemaAdapter = new Mock<IDatabaseAdapter>().Object;
        var databaseConfig = new DatabaseConfig { ConnectionString = "Server=localhost;Database=TestDB;" };
        var scannerLogger = NullLogger<SchemaScanner>.Instance;
        _schemaScannerMock = new Mock<SchemaScanner>(databaseConfig, schemaAdapter, scannerLogger);

        var vectorStore = new Mock<IVectorStore>().Object;
        var embeddingClient = new Mock<IEmbeddingClient>().Object;
        var indexerLogger = NullLogger<SchemaIndexer>.Instance;
        _schemaIndexerMock = new Mock<SchemaIndexer>(vectorStore, embeddingClient, indexerLogger);

        _orchestrator = new AgentOrchestrator(
            _schemaCacheMock.Object,
            _queryClassifierMock.Object,
            _simplePipelineMock.Object,
            _mediumPipelineMock.Object,
            _complexPipelineMock.Object,
            _metricsCollectorMock.Object,
            NullLogger<AgentOrchestrator>.Instance,
            _qdrantServiceMock.Object,
            _schemaScannerMock.Object,
            _schemaIndexerMock.Object
        );
    }

    /// <summary>
    /// Test Requirement 8.7: Force re-index flag triggers re-indexing regardless of fingerprint
    /// </summary>
    [Fact]
    public async Task ConnectToDatabaseAsync_WithForceReindex_TriggersReindexing()
    {
        // Arrange
        var connectionId = "test-connection";
        var connectionString = "Server=localhost;Database=TestDB;";
        var dbName = "TestDB";

        var schema = CreateTestSchema();
        var fingerprint = new SchemaFingerprint
        {
            Hash = "test-hash",
            ComputedAt = DateTime.UtcNow,
            TableCount = 2,
            ColumnCount = 4,
            RelationshipCount = 1,
            TableNames = new List<string> { "Orders", "Customers" }
        };

        // Setup mocks
        _qdrantServiceMock.Setup(x => x.SetCollectionName(It.IsAny<string>()));
        _qdrantServiceMock.Setup(x => x.GetCurrentCollectionName()).Returns($"schema_embeddings_{dbName.ToLower()}");
        _qdrantServiceMock.Setup(x => x.CollectionExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Setup GetPointCountAsync to return different values on subsequent calls
        var pointCountCallCount = 0;
        _qdrantServiceMock.Setup(x => x.GetPointCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => pointCountCallCount++ == 0 ? 100 : 75);

        _schemaScannerMock.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        _qdrantServiceMock.Setup(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _schemaIndexerMock.Setup(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionResult { Success = true, IndexingPerformed = true, PointsIndexed = 75 });

        // Act
        var result = await _orchestrator.ConnectToDatabaseAsync(connectionId, connectionString, forceReindex: true);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);

        // Verify that DeleteCollectionAsync was called (re-indexing happened)
        _qdrantServiceMock.Verify(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Verify that IndexSchemaAsync was called
        _schemaIndexerMock.Verify(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify that fingerprint comparison was NOT performed (GetStoredFingerprintAsync should not be called)
        _qdrantServiceMock.Verify(x => x.GetStoredFingerprintAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Test Requirement 8.5: Re-indexing deletes existing collection before creating new embeddings
    /// </summary>
    [Fact]
    public async Task ConnectToDatabaseAsync_WithSchemaChanges_DeletesCollectionBeforeReindexing()
    {
        // Arrange
        var connectionId = "test-connection";
        var connectionString = "Server=localhost;Database=TestDB;";
        var dbName = "TestDB";

        var currentSchema = CreateTestSchema();
        var currentFingerprint = new SchemaFingerprint
        {
            Hash = "new-hash",
            ComputedAt = DateTime.UtcNow,
            TableCount = 2,
            ColumnCount = 4,
            RelationshipCount = 1,
            TableNames = new List<string> { "Orders", "Customers" }
        };

        var storedFingerprint = new SchemaFingerprint
        {
            Hash = "old-hash",
            ComputedAt = DateTime.UtcNow.AddDays(-1),
            TableCount = 1,
            ColumnCount = 2,
            RelationshipCount = 0,
            TableNames = new List<string> { "Orders" }
        };

        // Setup mocks
        _qdrantServiceMock.Setup(x => x.SetCollectionName(It.IsAny<string>()));
        _qdrantServiceMock.Setup(x => x.GetCurrentCollectionName()).Returns($"schema_embeddings_{dbName.ToLower()}");
        _qdrantServiceMock.Setup(x => x.CollectionExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Setup GetPointCountAsync to return different values on subsequent calls
        var pointCountCallCount = 0;
        _qdrantServiceMock.Setup(x => x.GetPointCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => pointCountCallCount++ == 0 ? 50 : 75);

        _schemaScannerMock.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(currentSchema);
        _qdrantServiceMock.Setup(x => x.GetStoredFingerprintAsync(It.IsAny<CancellationToken>())).ReturnsAsync(storedFingerprint);
        _qdrantServiceMock.Setup(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _schemaIndexerMock.Setup(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionResult { Success = true, IndexingPerformed = true, PointsIndexed = 75 });

        // Track the order of operations
        var operationOrder = new List<string>();
        _qdrantServiceMock.Setup(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => operationOrder.Add("Delete"))
            .Returns(Task.CompletedTask);
        _schemaIndexerMock.Setup(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .Callback(() => operationOrder.Add("Index"))
            .ReturnsAsync(new ConnectionResult { Success = true, IndexingPerformed = true, PointsIndexed = 75 });

        // Act
        var result = await _orchestrator.ConnectToDatabaseAsync(connectionId, connectionString, forceReindex: false);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);

        // Verify that DeleteCollectionAsync was called before IndexSchemaAsync
        Assert.Equal(2, operationOrder.Count);
        Assert.Equal("Delete", operationOrder[0]);
        Assert.Equal("Index", operationOrder[1]);

        // Verify both operations were called exactly once
        _qdrantServiceMock.Verify(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _schemaIndexerMock.Verify(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test that re-indexing is NOT triggered when fingerprints match
    /// </summary>
    [Fact]
    public async Task ConnectToDatabaseAsync_WithMatchingFingerprints_SkipsReindexing()
    {
        // Arrange
        var connectionId = "test-connection";
        var connectionString = "Server=localhost;Database=TestDB;";
        var dbName = "TestDB";

        var schema = CreateTestSchema();

        // Compute the actual fingerprint that would be generated from this schema
        var fingerprint = _orchestrator.ComputeSchemaFingerprint(schema);

        // Setup mocks
        _qdrantServiceMock.Setup(x => x.SetCollectionName(It.IsAny<string>()));
        _qdrantServiceMock.Setup(x => x.GetCurrentCollectionName()).Returns($"schema_embeddings_{dbName.ToLower()}");
        _qdrantServiceMock.Setup(x => x.CollectionExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _qdrantServiceMock.Setup(x => x.GetPointCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(100);
        _schemaScannerMock.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        _qdrantServiceMock.Setup(x => x.GetStoredFingerprintAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fingerprint);

        // Act
        var result = await _orchestrator.ConnectToDatabaseAsync(connectionId, connectionString, forceReindex: false);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.IndexingPerformed);
        Assert.Equal(100, result.PointsIndexed);

        // Verify that DeleteCollectionAsync was NOT called
        _qdrantServiceMock.Verify(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Verify that IndexSchemaAsync was NOT called
        _schemaIndexerMock.Verify(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Test that initial indexing does NOT delete collection (collection doesn't exist)
    /// </summary>
    [Fact]
    public async Task ConnectToDatabaseAsync_WithNoCollection_PerformsInitialIndexingWithoutDelete()
    {
        // Arrange
        var connectionId = "test-connection";
        var connectionString = "Server=localhost;Database=TestDB;";
        var dbName = "TestDB";

        var schema = CreateTestSchema();

        // Setup mocks
        _qdrantServiceMock.Setup(x => x.SetCollectionName(It.IsAny<string>()));
        _qdrantServiceMock.Setup(x => x.GetCurrentCollectionName()).Returns($"schema_embeddings_{dbName.ToLower()}");
        _qdrantServiceMock.Setup(x => x.CollectionExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _schemaScannerMock.Setup(x => x.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        _schemaIndexerMock.Setup(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionResult { Success = true, IndexingPerformed = true, PointsIndexed = 50 });
        _qdrantServiceMock.Setup(x => x.GetPointCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(50);

        // Act
        var result = await _orchestrator.ConnectToDatabaseAsync(connectionId, connectionString, forceReindex: false);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.IndexingPerformed);

        // Verify that DeleteCollectionAsync was NOT called (initial indexing)
        _qdrantServiceMock.Verify(x => x.DeleteCollectionAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Verify that IndexSchemaAsync was called
        _schemaIndexerMock.Verify(x => x.IndexSchemaAsync(It.IsAny<DatabaseSchema>(), It.IsAny<SchemaFingerprint>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private DatabaseSchema CreateTestSchema()
    {
        return new DatabaseSchema
        {
            Tables = new List<TableInfo>
            {
                new TableInfo
                {
                    TableName = "Orders",
                    Description = "Customer orders",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "OrderId", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "CustomerId", DataType = "int", IsForeignKey = true }
                    }
                },
                new TableInfo
                {
                    TableName = "Customers",
                    Description = "Customer information",
                    Columns = new List<ColumnInfo>
                    {
                        new ColumnInfo { ColumnName = "CustomerId", DataType = "int", IsPrimaryKey = true },
                        new ColumnInfo { ColumnName = "CustomerName", DataType = "varchar(100)" }
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
                    ToColumn = "CustomerId"
                }
            }
        };
    }
}
