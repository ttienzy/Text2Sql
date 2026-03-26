# Infrastructure Integrations

## 1. LLM Integration

### Google Gemini

**Configuration** (`GeminiConfig.cs`):
```csharp
public class GeminiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public string EmbeddingModel { get; set; } = "gemini-embedding-1.0";
    public int MaxTokens { get; set; } = 8192;
    public double Temperature { get; set; } = 0.1;
}
```

**Client Implementation** (`GeminiClient.cs`):
- Sử dụng **Semantic Kernel** để gọi Gemini API
- Validate API key trước khi khởi tạo
- Error handling với `LLMErrorHandler`
- Retry logic cho transient errors

**Use Cases**:
- SQL generation
- Intent classification
- Query explanation
- Natural language formatting
- Embedding generation (768 dimensions)

**Error Handling**:
- Rate limit detection và automatic retry
- Quota exceeded detection
- Invalid API key detection
- Service unavailable fallback

### OpenAI

**Configuration** (`OpenAIConfig.cs`):
```csharp
public class OpenAIConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int MaxTokens { get; set; } = 8192;
    public double Temperature { get; set; } = 0.1;
}
```

**Provider Selection**:
```csharp
// LLMClientFactory.cs
public ILLMClient CreateClient()
{
    return _llmProvider.ToLowerInvariant() switch
    {
        "openai" => new OpenAIClient(_openAIConfig, _logger, _llmErrorHandler),
        "gemini" => new GeminiClient(_geminiConfig, _logger, _llmErrorHandler),
        _ => throw new ArgumentException($"Unknown LLM provider: {_llmProvider}")
    };
}
```

**Embedding Dimensions**:
- Gemini: 768 dimensions
- OpenAI: 1536 dimensions
- **CRITICAL**: Phải match với Qdrant collection configuration

### LLM Call Optimization

**Caching Strategy**:
- Query embeddings cached in memory (1000 entries, 60min TTL)
- Schema embeddings cached in Qdrant
- Conversation context reused across turns

**Prompt Engineering**:
- System prompts optimized cho từng task
- JSON output format để parse dễ dàng
- Few-shot examples trong complex queries

## 2. Vector Database (Qdrant)

### Configuration

**QdrantConfig.cs**:
```csharp
public class QdrantConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6333;  // REST API port
    public string CollectionName { get; set; } = "schema_embeddings";
    public int VectorSize { get; set; } = 768;  // Must match embedding model
    public string Distance { get; set; } = "Cosine";
}
```

### Collection Management

**Schema Fingerprinting**:
```csharp
public class SchemaFingerprint
{
    public string Hash { get; set; }  // SHA256 của schema structure
    public DateTime ComputedAt { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public int RelationshipCount { get; set; }
    public List<string> TableNames { get; set; }
}
```

**Auto Re-indexing**:
- Detect schema changes bằng fingerprint comparison
- Automatic collection recreation nếu vector size mismatch
- Incremental indexing cho schema updates

### Vector Search

**Search Parameters**:
```csharp
var results = await _qdrantService.SearchAsync(
    queryVector: embedding,
    limit: 5,  // Top K results
    scoreThreshold: 0.3,  // Minimum cosine similarity
    filter: new Dictionary<string, object> 
    { 
        ["connection_id"] = connectionId 
    },
    cancellationToken: ct
);
```

**Scoring Thresholds**:
- `0.7+`: High relevance (strict)
- `0.5-0.7`: Medium relevance
- `0.3-0.5`: Low relevance (broad recall)
- `< 0.3`: Likely irrelevant

### Fallback Strategy

**In-Memory Vector Store**:
```csharp
// VectorDBErrorHandler.cs
protected override async Task<T> RetryWithFallbackAsync<T>(
    Func<Task<T>> operation,
    SqlError error,
    CancellationToken cancellationToken)
{
    try
    {
        return await operation();
    }
    catch (VectorDBException)
    {
        _logger.LogWarning("Qdrant unavailable, falling back to in-memory store");
        return await _inMemoryStore.SearchAsync(...);
    }
}
```

**Fallback Behavior**:
- Automatic switch khi Qdrant unavailable
- In-memory store sử dụng brute-force cosine similarity
- Performance degradation nhưng system vẫn hoạt động

### Common Issues

**Issue 1: Vector Dimension Mismatch**
```
Error: Vector dimension (1536) doesn't match collection (768)
```
**Solution**: 
- Check embedding model configuration
- Recreate collection với correct dimension
- Ensure LLM provider matches Qdrant config

**Issue 2: No Search Results**
```
Warning: No results found. Collection has 100 points.
```
**Solution**:
- Lower `scoreThreshold` (try 0.3)
- Check query embedding quality
- Verify schema indexing completed

**Issue 3: Connection Timeout**
```
Error: Cannot connect to Qdrant at http://localhost:6333
```
**Solution**:
- Verify Qdrant is running: `docker ps | grep qdrant`
- Check port mapping: `6333:6333`
- Test connection: `curl http://localhost:6333/collections`

## 3. RAG System

### Hybrid Search Strategy

**SchemaRetriever.cs**:
```csharp
public async Task<RetrievedSchemaContext> RetrieveAsync(
    string question,
    DatabaseSchema fullSchema,
    string? connectionId,
    CancellationToken cancellationToken)
{
    // 1. Vector similarity search (Qdrant)
    var vectorResults = await _vectorStore.SearchAsync(
        queryVector: embedding,
        limit: _ragConfig.TopK,
        scoreThreshold: _ragConfig.MinimumScore,
        filter: connectionId != null ? new() { ["connection_id"] = connectionId } : null
    );

    // 2. Keyword matching
    var keywordResults = _keywordRetriever.Search(question, fullSchema);

    // 3. Graph traversal (related tables via foreign keys)
    var graphResults = TraverseSchemaGraph(vectorResults, fullSchema);

    // 4. Weighted scoring and ranking
    var combined = CombineResults(
        vectorResults,   // weight: 0.5
        keywordResults,  // weight: 0.3
        graphResults     // weight: 0.2
    );

    return BuildContext(combined);
}
```

### RAG Configuration

**RAGConfig.cs**:
```csharp
public class RAGConfig
{
    public int TopK { get; set; } = 5;  // Number of results to retrieve
    public double MinimumScore { get; set; } = 0.3;  // Cosine similarity threshold
    public bool EnableHybridSearch { get; set; } = false;
    public int MaxContextTables { get; set; } = 10;
    
    // Hybrid search weights
    public float VectorWeight { get; set; } = 0.5f;
    public float KeywordWeight { get; set; } = 0.3f;
    public float GraphWeight { get; set; } = 0.2f;
}
```

### Keyword Matching

**KeywordSchemaRetriever.cs**:
- Exact table name matching
- Column name matching
- Fuzzy matching với Levenshtein distance
- Vietnamese keyword normalization

### Graph Traversal

**Schema Graph**:
```
Customers (PK: CustomerID)
    ↓ FK
Orders (PK: OrderID, FK: CustomerID)
    ↓ FK
OrderDetails (PK: OrderDetailID, FK: OrderID, ProductID)
    ↓ FK
Products (PK: ProductID)
```

**Traversal Strategy**:
- Start từ matched tables
- Follow foreign key relationships
- Max depth: 2 levels
- Avoid circular references

## 4. Embedding Generation

### Embedding Pipeline

```
1. Text Input (query or schema element)
   ↓
2. Normalize Text
   ├─ Remove special characters
   ├─ Lowercase
   └─ Trim whitespace
   ↓
3. Generate Embedding (LLM API call)
   ├─ Gemini: 768 dimensions
   └─ OpenAI: 1536 dimensions
   ↓
4. Cache Embedding (Memory Cache)
   ├─ Key: SHA256(text)
   ├─ TTL: 60 minutes
   └─ Max entries: 1000
   ↓
5. Store in Qdrant (for schema elements)
```

### Embedding Caching

**Memory Cache**:
```csharp
private async Task<float[]> GetOrGenerateQueryEmbeddingAsync(
    string question,
    CancellationToken cancellationToken)
{
    var cacheKey = $"embedding:{ComputeHash(question)}";
    
    if (_queryCache.TryGetValue(cacheKey, out float[]? cached))
    {
        return cached;
    }

    var embedding = await _embeddingClient.GenerateEmbeddingAsync(
        question, 
        cancellationToken);

    _queryCache.Set(cacheKey, embedding, TimeSpan.FromMinutes(60));
    
    return embedding;
}
```

### Schema Indexing

**SchemaIndexer.cs**:
```csharp
public async Task<int> IndexSchemaAsync(
    DatabaseSchema schema,
    string connectionId,
    CancellationToken cancellationToken)
{
    var points = new List<PointStruct>();
    
    foreach (var table in schema.Tables)
    {
        // Generate embedding for table + columns
        var text = $"{table.TableName}: {string.Join(", ", table.Columns.Select(c => c.ColumnName))}";
        var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken);
        
        // Create Qdrant point
        var point = new PointStruct
        {
            Id = new PointId { Uuid = Guid.NewGuid().ToString() },
            Vectors = new Vectors { Vector = new Vector { Data = embedding } },
            Payload = new Dictionary<string, Value>
            {
                ["table_name"] = new Value { StringValue = table.TableName },
                ["connection_id"] = new Value { StringValue = connectionId },
                ["columns"] = new Value { StringValue = string.Join(",", table.Columns.Select(c => c.ColumnName)) }
            }
        };
        
        points.Add(point);
    }
    
    await _qdrantService.UpsertPointsAsync(points, cancellationToken);
    
    return points.Count;
}
```

## 5. Secrets Management

### Environment Variables

**Priority Order**:
1. Environment variables (highest)
2. `.env` file
3. `appsettings.json`
4. User secrets (development only)

**Required Secrets**:
```bash
# LLM API Keys
OPENAI_API_KEY=sk-...
GEMINI_API_KEY=...

# Database
DATABASE_CONNECTION_STRING=Server=...;Database=...;User Id=...;Password=...;

# JWT
JWT_SECRET=your-secure-jwt-secret-32-chars-min
JWT_ISSUER=TextToSqlAgentAPI
JWT_AUDIENCE=TextToSqlAgentClient

# Encryption
ENCRYPTION_KEY=DefaultEncryptionKey32CharactersLong!

# Qdrant
QDRANT_URL=http://localhost:6333

# Redis (optional)
REDIS_CONNECTION_STRING=localhost:6379
```

### Configuration Loading

**Program.cs**:
```csharp
// Load .env file first
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}

// ConfigurationService validates all required settings
var configService = new ConfigurationService(configuration, logger, environment);
var validationResult = configService.ValidateConfiguration();

if (!validationResult.IsValid)
{
    foreach (var error in validationResult.Errors)
    {
        logger.Fatal("Configuration error: {Error}", error);
    }
    throw new InvalidOperationException("Configuration validation failed");
}
```

### Security Best Practices

**DO**:
- ✅ Use environment variables for production
- ✅ Use user secrets for development
- ✅ Encrypt connection strings in database
- ✅ Rotate JWT secrets regularly
- ✅ Use strong encryption keys (32+ characters)

**DON'T**:
- ❌ Commit `.env` files to git
- ❌ Hardcode API keys in code
- ❌ Use default/weak secrets in production
- ❌ Share secrets via email/chat
- ❌ Log sensitive information

## 6. Connection Pooling

### SQL Server Connection Pool

**Configuration**:
```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(30);  // 30 seconds
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null
            );
        }
    )
);
```

**Pool Settings**:
- Min Pool Size: 5
- Max Pool Size: 100
- Connection Lifetime: 300 seconds
- Connection Timeout: 30 seconds

### HTTP Client Pool

**Configuration**:
```csharp
services.AddHttpClient<QdrantService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

## 7. Monitoring & Observability

### Structured Logging

**Serilog Configuration**:
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console()
    .WriteTo.File(
        "logs/api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7
    )
    .CreateLogger();
```

### Correlation ID

**CorrelationIdMiddleware.cs**:
```csharp
public async Task InvokeAsync(HttpContext context)
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
    
    context.Response.Headers.Add("X-Correlation-ID", correlationId);
    
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await _next(context);
    }
}
```

### Health Checks

**Endpoints**:
- `/health` - Overall health status
- `/health/ready` - Readiness probe (K8s)
- `/health/live` - Liveness probe (K8s)

**Checks**:
- Database connectivity
- Qdrant availability
- Redis connectivity (if configured)
- LLM API reachability
