using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Test Controller for database connection and Qdrant indexing verification
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TextToSqlAgent.Infrastructure.Services.ITokenQuotaService _tokenQuotaService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWebHostEnvironment _environment;

    // Mock connection string
    private const string TEST_CONNECTION_STRING = "Server=.;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;";
    private const string TEST_USER_ID = "test-user-123";

    public TestController(
        ILogger<TestController> logger,
        IServiceProvider serviceProvider,
        TextToSqlAgent.Infrastructure.Services.ITokenQuotaService tokenQuotaService,
        IUnitOfWork unitOfWork,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _tokenQuotaService = tokenQuotaService;
        _unitOfWork = unitOfWork;
        _environment = environment;
    }

    /// <summary>
    /// Test API 1: Test database connection and schema indexing to Qdrant
    /// </summary>
    [HttpPost("test-schema-indexing")]
    public async Task<IActionResult> TestSchemaIndexing()
    {
        try
        {
            _logger.LogInformation("Starting schema indexing test for connection: {ConnectionString}",
                TEST_CONNECTION_STRING.Replace("Password=123", "Password=***"));

            // Step 1: Test database connection
            var connectionTestResult = await TestDatabaseConnectionAsync();
            if (!connectionTestResult.IsSuccess)
            {
                return Ok(new
                {
                    success = false,
                    step = "database_connection",
                    error = connectionTestResult.ErrorMessage,
                    connectionString = TEST_CONNECTION_STRING.Replace("Password=123", "Password=***")
                });
            }

            // Step 2: Scan database schema
            var schemaResult = await ScanDatabaseSchemaAsync();
            if (!schemaResult.IsSuccess)
            {
                return Ok(new
                {
                    success = false,
                    step = "schema_scanning",
                    error = schemaResult.ErrorMessage,
                    tablesFound = schemaResult.TablesCount
                });
            }

            // Step 3: Index schema to Qdrant
            var indexingResult = await IndexSchemaToQdrantAsync(schemaResult.SchemaData);

            return Ok(new
            {
                success = indexingResult.IsSuccess,
                step = indexingResult.IsSuccess ? "completed" : "qdrant_indexing",
                error = indexingResult.ErrorMessage,
                tablesFound = schemaResult.TablesCount,
                tablesIndexed = indexingResult.IndexedCount,
                collectionName = indexingResult.CollectionName,
                qdrantStatus = indexingResult.QdrantStatus,
                details = new
                {
                    connectionTest = connectionTestResult,
                    schemaScanning = schemaResult,
                    qdrantIndexing = indexingResult
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during schema indexing test");
            return Ok(new
            {
                success = false,
                step = "exception",
                error = ex.Message,
                stackTrace = _environment.IsDevelopment() ? ex.StackTrace : null // ✅ TASK 2.1: Only expose in Development
            });
        }
    }

    /// <summary>
    /// Test API 2: Test query processing with database schema retrieval
    /// </summary>
    [HttpPost("test-query")]
    public async Task<IActionResult> TestQuery([FromBody] TestQueryRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.Question))
            {
                return BadRequest(new { error = "Question is required" });
            }

            _logger.LogInformation("Testing query: {Question}", request.Question);

            // Step 1: Test database connection
            var connectionTestResult = await TestDatabaseConnectionAsync();
            if (!connectionTestResult.IsSuccess)
            {
                return Ok(new
                {
                    success = false,
                    step = "database_connection",
                    question = request.Question,
                    error = connectionTestResult.ErrorMessage
                });
            }

            // Step 2: Retrieve schema from Qdrant
            var schemaRetrievalResult = await RetrieveSchemaFromQdrantAsync(request.Question);

            // Step 3: Get available tables from database
            var tablesResult = await GetAvailableTablesAsync();

            return Ok(new
            {
                success = true,
                question = request.Question,
                databaseConnection = connectionTestResult.IsSuccess,
                tablesInDatabase = tablesResult.Tables,
                tablesCount = tablesResult.Count,
                qdrantSchemaRetrieval = new
                {
                    success = schemaRetrievalResult.IsSuccess,
                    schemasFound = schemaRetrievalResult.SchemasFound,
                    collectionExists = schemaRetrievalResult.CollectionExists,
                    error = schemaRetrievalResult.ErrorMessage
                },
                recommendation = GetQueryRecommendation(request.Question, tablesResult.Tables),
                connectionString = TEST_CONNECTION_STRING.Replace("Password=123", "Password=***")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during query test");
            return Ok(new
            {
                success = false,
                step = "exception",
                question = request?.Question,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test API 3: Clear Qdrant collection (for fixing dimension mismatch)
    /// </summary>
    [HttpPost("clear-qdrant")]
    public async Task<IActionResult> ClearQdrant()
    {
        try
        {
            _logger.LogInformation("Clearing Qdrant collection for dimension fix...");

            using var scope = _serviceProvider.CreateScope();
            var schemaIndexer = scope.ServiceProvider.GetRequiredService<SchemaIndexer>();

            await schemaIndexer.ClearIndexAsync();

            return Ok(new
            {
                success = true,
                message = "Qdrant collection cleared successfully. You can now re-run schema indexing with correct dimensions.",
                note = "Configuration updated: text-embedding-3-large (3072 dims) -> schema_embeddings_large collection"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing Qdrant collection");
            return Ok(new
            {
                success = false,
                error = ex.Message,
                message = "Failed to clear Qdrant collection"
            });
        }
    }
    /// <summary>
    /// Process a complete query like the Console project - generates SQL and executes it
    /// </summary>
    [HttpPost("process-query")]
    public async Task<IActionResult> ProcessQuery([FromBody] TestQueryRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.Question))
            {
                return BadRequest(new { error = "Question is required" });
            }

            _logger.LogInformation("Processing complete query: {Question}", request.Question);

            // Create a scoped service provider with overridden database configuration
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            // ✅ CRIT-2 + MULTI-DB: Use SetDatabaseContext (test controller defaults to SqlServer)
            using (DatabaseConfigContext.SetDatabaseContext(TEST_CONNECTION_STRING, TextToSqlAgent.Core.Enums.DatabaseProvider.SqlServer))
            {
                // Get the enhanced agent orchestrator and pipeline orchestrator from scoped DI
                var agent = scopedServices.GetRequiredService<EnhancedAgentOrchestrator>();
                var pipelineOrchestrator = scopedServices.GetRequiredService<TextToSqlAgent.Application.Pipeline.PipelineOrchestrator>();

                // Process the query using the new modular pipeline
                var response = await agent.ProcessQueryWithPipelineAsync(pipelineOrchestrator, request.Question);

                // Format response to match Console project output
                var result = new
                {
                    success = response.Success,
                    question = request.Question,
                    answer = response.Answer,
                    sqlGenerated = response.SqlGenerated,
                    queryResult = response.QueryResult != null ? new
                    {
                        success = response.QueryResult.Success,
                        columns = response.QueryResult.Columns,
                        rows = response.QueryResult.Rows,
                        rowCount = response.QueryResult.RowCount,
                        executionTimeMs = response.QueryResult.ExecutionTimeMs,
                        rowsAffected = response.QueryResult.RowsAffected,
                        errorMessage = response.QueryResult.ErrorMessage
                    } : null,
                    processingSteps = response.ProcessingSteps,
                    queryExplanation = response.QueryExplanation,
                    suggestedQueries = response.SuggestedQueries,
                    correctionHistory = response.CorrectionHistory?.Select(c => new
                    {
                        originalSql = c.OriginalSql,
                        correctedSql = c.CorrectedSql,
                        error = c.Error?.ErrorMessage,
                        reasoning = c.Reasoning,
                        success = c.Success,
                        attemptNumber = c.AttemptNumber,
                        timestamp = c.Timestamp
                    }).ToList(),
                    wasCorrected = response.WasCorrected,
                    correctionAttempts = response.CorrectionAttempts,
                    conversationId = response.ConversationId,
                    isFollowUp = response.IsFollowUp,
                    errorMessage = response.ErrorMessage,
                    connectionString = TEST_CONNECTION_STRING.Replace("Password=123", "Password=***")
                };

                return Ok(result);
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during complete query processing");
            return Ok(new
            {
                success = false,
                question = request?.Question,
                error = ex.Message,
                errorDetails = ex.InnerException?.Message,
                connectionString = TEST_CONNECTION_STRING.Replace("Password=123", "Password=***")
            });
        }
    }

    #region Private Helper Methods

    private async Task<ConnectionTestResult> TestDatabaseConnectionAsync()
    {
        try
        {
            using var connection = new SqlConnection(TEST_CONNECTION_STRING);
            await connection.OpenAsync();

            var command = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", connection);
            var tableCount = (int)await command.ExecuteScalarAsync();

            return new ConnectionTestResult
            {
                IsSuccess = true,
                TableCount = tableCount,
                Message = $"Connection successful. Found {tableCount} tables."
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<SchemaResult> ScanDatabaseSchemaAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var schemaScanner = scope.ServiceProvider.GetRequiredService<SchemaScanner>();

            // Create a mock connection entity for schema scanning
            var connection = new Connection
            {
                Id = "test-connection-id",
                UserId = TEST_USER_ID,
                Name = "Test Connection",
                Host = ".",
                Database = "TextToSqlTest",
                Username = "sa",
                EncryptedPassword = "123", // In real scenario, this would be encrypted
                Port = 1433,
                Provider = "SqlServer",
                CreatedAt = DateTime.UtcNow
            };

            var schema = await schemaScanner.ScanAsync();

            return new SchemaResult
            {
                IsSuccess = true,
                SchemaData = new[] { schema },
                TablesCount = schema?.Tables?.Count ?? 0,
                Message = $"Schema scanning successful. Found {schema?.Tables?.Count ?? 0} tables."
            };
        }
        catch (Exception ex)
        {
            return new SchemaResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                TablesCount = 0
            };
        }
    }

    private async Task<IndexingResult> IndexSchemaToQdrantAsync(IEnumerable<TextToSqlAgent.Core.Models.DatabaseSchema>? schemas)
    {
        try
        {
            if (schemas == null || !schemas.Any())
            {
                return new IndexingResult
                {
                    IsSuccess = false,
                    ErrorMessage = "No schemas to index",
                    QdrantStatus = "No data"
                };
            }

            using var scope = _serviceProvider.CreateScope();
            var schemaIndexer = scope.ServiceProvider.GetRequiredService<SchemaIndexer>();

            // Generate collection name for test user
            var collectionName = CollectionNameHelper.NormalizeUserCollectionName(TEST_USER_ID);

            // Index schemas to Qdrant with test connection ID
            var fingerprint = new SchemaFingerprint
            {
                Hash = "test-hash",
                ComputedAt = DateTime.UtcNow
            };

            var result = await schemaIndexer.IndexSchemaAsync(schemas.First(), fingerprint, "test-connection-id");

            return new IndexingResult
            {
                IsSuccess = result.Success,
                IndexedCount = result.PointsIndexed,
                CollectionName = collectionName,
                QdrantStatus = result.Success ? "Indexed successfully" : "Indexing failed",
                ErrorMessage = result.ErrorMessage,
                Message = result.Success
                    ? $"Successfully indexed {result.PointsIndexed} points to collection: {collectionName}"
                    : $"Indexing failed: {result.ErrorMessage}"
            };
        }
        catch (Exception ex)
        {
            return new IndexingResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                QdrantStatus = "Error during indexing"
            };
        }
    }

    private async Task<SchemaRetrievalResult> RetrieveSchemaFromQdrantAsync(string question)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var schemaRetriever = scope.ServiceProvider.GetRequiredService<SchemaRetriever>();

            // Try to retrieve relevant schemas for the question
            // Note: We need a full schema to use SchemaRetriever, but for testing we'll simulate
            var mockSchema = new TextToSqlAgent.Core.Models.DatabaseSchema
            {
                Tables = new List<TableInfo>(),
                Relationships = new List<RelationshipInfo>()
            };

            var relevantContext = await schemaRetriever.RetrieveAsync(
                question,
                mockSchema,
                "test-connection-id"
            );

            return new SchemaRetrievalResult
            {
                IsSuccess = true,
                SchemasFound = relevantContext?.RelevantTables?.Count ?? 0,
                CollectionExists = true,
                Message = $"Found {relevantContext?.RelevantTables?.Count ?? 0} relevant tables for question"
            };
        }
        catch (Exception ex)
        {
            return new SchemaRetrievalResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                CollectionExists = false,
                SchemasFound = 0
            };
        }
    }

    private async Task<TablesResult> GetAvailableTablesAsync()
    {
        try
        {
            using var connection = new SqlConnection(TEST_CONNECTION_STRING);
            await connection.OpenAsync();

            var command = new SqlCommand(@"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE' 
                ORDER BY TABLE_NAME", connection);

            var tables = new List<string>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            return new TablesResult
            {
                Tables = tables,
                Count = tables.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available tables");
            return new TablesResult
            {
                Tables = new List<string>(),
                Count = 0
            };
        }
    }

    private string GetQueryRecommendation(string question, List<string> availableTables)
    {
        var lowerQuestion = question.ToLower();

        if (lowerQuestion.Contains("customer"))
            return availableTables.Any(t => t.ToLower().Contains("customer"))
                ? "Try querying customer-related tables"
                : "No customer tables found in database";

        if (lowerQuestion.Contains("order"))
            return availableTables.Any(t => t.ToLower().Contains("order"))
                ? "Try querying order-related tables"
                : "No order tables found in database";

        if (lowerQuestion.Contains("product"))
            return availableTables.Any(t => t.ToLower().Contains("product"))
                ? "Try querying product-related tables"
                : "No product tables found in database";

        return $"Available tables: {string.Join(", ", availableTables)}";
    }

    /// <summary>
    /// Test token usage tracking by simulating a message creation
    /// </summary>
    [HttpPost("test-token-usage")]
    public async Task<IActionResult> TestTokenUsage([FromBody] TestTokenUsageRequest request)
    {
        try
        {
            _logger.LogInformation("Testing token usage tracking");

            // Use provided conversation ID or create new one
            var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
            var userId = "7557b9a1-ae71-4e07-a624-15f91a25ee66"; // Test user ID from login response

            // Check if conversation exists, create if not
            var existingConversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (existingConversation == null)
            {
                var conversation = new Conversation
                {
                    Id = conversationId,
                    UserId = userId,
                    Title = "Test Token Usage Conversation",
                    CreatedAt = DateTime.UtcNow,
                    LastActiveAt = DateTime.UtcNow,
                    ConnectionId = request.ConnectionId
                };

                await _unitOfWork.Conversations.AddAsync(conversation);
            }

            // Create user message
            var userMessage = new Message
            {
                ConversationId = conversationId,
                Role = "user",
                Content = request.Question,
                Success = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(userMessage);

            // Simulate AI response with token usage
            var inputTokens = EstimateTokens(request.Question);
            var outputTokens = EstimateTokens(request.SimulatedAnswer);
            var totalTokens = inputTokens + outputTokens;
            var model = request.Model ?? "gpt-4o";

            var assistantMessage = new Message
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = request.SimulatedAnswer,
                SqlQuery = request.SimulatedSql,
                Success = true,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens,
                Model = model,
                Cost = CalculateEstimatedCost(totalTokens, model),
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(assistantMessage);
            await _unitOfWork.SaveChangesAsync();

            // Update quota using TokenQuotaService
            await _tokenQuotaService.ConsumeTokenAsync(userId, inputTokens, outputTokens, model);

            _logger.LogInformation("Token usage tracked: {InputTokens} input + {OutputTokens} output = {TotalTokens} total tokens for user {UserId}",
                inputTokens, outputTokens, totalTokens, userId);

            return Ok(new
            {
                success = true,
                conversationId = conversationId,
                tokenUsage = new
                {
                    inputTokens = inputTokens,
                    outputTokens = outputTokens,
                    totalTokens = totalTokens,
                    model = model,
                    cost = assistantMessage.Cost
                },
                message = "Token usage tracked successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing token usage");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Estimate token count for text (rough approximation: 1 token ≈ 4 characters)
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough approximation: 1 token ≈ 4 characters for English text
        return Math.Max(1, text.Length / 4);
    }

    /// <summary>
    /// Calculate estimated cost based on token count and model
    /// </summary>
    private static decimal CalculateEstimatedCost(int totalTokens, string model)
    {
        // Approximate pricing per 1K tokens
        var costPer1KTokens = model.ToLowerInvariant() switch
        {
            var m when m.Contains("gpt-4") => 0.03m, // $0.03 per 1K tokens
            var m when m.Contains("gpt-3.5") => 0.002m, // $0.002 per 1K tokens
            var m when m.Contains("gemini") => 0.00025m, // $0.00025 per 1K tokens
            _ => 0.03m // Default to GPT-4 pricing
        };

        return (totalTokens / 1000m) * costPer1KTokens;
    }

    #endregion
}

#region Request/Response Models

public class TestTokenUsageRequest
{
    public string Question { get; set; } = string.Empty;
    public string SimulatedAnswer { get; set; } = string.Empty;
    public string? SimulatedSql { get; set; }
    public string? Model { get; set; }
    public string? ConversationId { get; set; }
    public string? ConnectionId { get; set; }
}

public class TestQueryRequest
{
    public string Question { get; set; } = string.Empty;
}

public class ConnectionTestResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TableCount { get; set; }
}

public class SchemaResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TablesCount { get; set; }
    public IEnumerable<TextToSqlAgent.Core.Models.DatabaseSchema>? SchemaData { get; set; }
}

public class IndexingResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public int IndexedCount { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public string QdrantStatus { get; set; } = string.Empty;
}

public class SchemaRetrievalResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SchemasFound { get; set; }
    public bool CollectionExists { get; set; }
}

public class TablesResult
{
    public List<string> Tables { get; set; } = new();
    public int Count { get; set; }
}

#endregion