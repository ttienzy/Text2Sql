# Testing Strategy & Coverage

## 1. Current Test Coverage

### Overview
**Status**: ⚠️ Minimal coverage - needs significant improvement

**Existing Tests**:
- Unit Tests: ~20% coverage (TextToSqlAgent.Tests.Unit)
- Integration Tests: ~30% coverage (TextToSqlAgent.Tests.Integration)
- E2E Tests: None
- Load Tests: None
- Security Tests: None

### Coverage by Layer

| Layer | Coverage | Status |
|-------|----------|--------|
| API Controllers | 15% | ❌ Poor |
| Application Services | 25% | ⚠️ Low |
| Core Domain | 40% | ⚠️ Medium |
| Infrastructure | 10% | ❌ Poor |
| Frontend | 5% | ❌ Poor |

---

## 2. Unit Testing Strategy

### Test Structure

**Naming Convention**:
```csharp
// Pattern: [MethodName]_[Scenario]_[ExpectedResult]
[Fact]
public async Task GenerateSql_WithSimpleQuery_ReturnsValidSql()
{
    // Arrange
    var generator = CreateSqlGenerator();
    var intent = CreateSimpleIntent();
    
    // Act
    var result = await generator.GenerateAsync(intent, CancellationToken.None);
    
    // Assert
    Assert.NotNull(result.Sql);
    Assert.StartsWith("SELECT", result.Sql);
}
```

### Critical Components to Test

#### 1. Query Classifier
```csharp
public class QueryClassifierTests
{
    [Theory]
    [InlineData("Show me all customers", QueryComplexity.Simple)]
    [InlineData("Top 10 products by revenue", QueryComplexity.Medium)]
    [InlineData("Compare sales trends year over year", QueryComplexity.Complex)]
    public async Task ClassifyAsync_WithVariousQueries_ReturnsCorrectComplexity(
        string query, 
        QueryComplexity expected)
    {
        // Arrange
        var classifier = CreateClassifier();
        
        // Act
        var result = await classifier.ClassifyAsync(query);
        
        // Assert
        Assert.Equal(expected, result.Complexity);
    }
    
    [Fact]
    public async Task ClassifyAsync_WithAmbiguousQuery_UsesLLMFallback()
    {
        // Arrange
        var mockLLM = new Mock<ILLMQueryClassifier>();
        var classifier = CreateClassifier(mockLLM.Object);
        
        // Act
        await classifier.ClassifyAsync("ambiguous query");
        
        // Assert
        mockLLM.Verify(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

#### 2. Intent Classifier
```csharp
public class IntentClassifierTests
{
    [Theory]
    [InlineData("SELECT * FROM Users", IntentCategory.Select)]
    [InlineData("INSERT INTO Users VALUES (...)", IntentCategory.Insert)]
    [InlineData("UPDATE Users SET ...", IntentCategory.Update)]
    [InlineData("DELETE FROM Users WHERE ...", IntentCategory.Delete)]
    [InlineData("DROP TABLE Users", IntentCategory.Drop)]
    public async Task ClassifyAsync_WithVariousIntents_ReturnsCorrectCategory(
        string query,
        IntentCategory expected)
    {
        // Arrange
        var classifier = CreateIntentClassifier();
        
        // Act
        var result = await classifier.ClassifyAsync(query, "", "");
        
        // Assert
        Assert.Equal(expected, result.Intent);
    }
}
```

#### 3. SQL Generator
```csharp
public class SqlGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WithValidIntent_ReturnsSafeSQL()
    {
        // Arrange
        var generator = CreateSqlGenerator();
        var intent = CreateIntent("Show all customers");
        
        // Act
        var result = await generator.GenerateAsync(intent, schema, ct);
        
        // Assert
        Assert.NotNull(result.Sql);
        Assert.DoesNotContain("DROP", result.Sql);
        Assert.DoesNotContain("DELETE", result.Sql);
        Assert.StartsWith("SELECT", result.Sql);
    }
    
    [Fact]
    public async Task ValidateSql_WithDangerousKeywords_ReturnsFalse()
    {
        // Arrange
        var generator = CreateSqlGenerator();
        var sql = "DROP TABLE Users";
        
        // Act
        var isValid = generator.ValidateSql(sql);
        
        // Assert
        Assert.False(isValid);
    }
}
```

#### 4. Error Handlers
```csharp
public class LLMErrorHandlerTests
{
    [Fact]
    public async Task HandleLLMErrorAsync_WithRateLimit_WaitsAndRetries()
    {
        // Arrange
        var handler = CreateLLMErrorHandler();
        var callCount = 0;
        
        Func<Task<string>> operation = async () =>
        {
            callCount++;
            if (callCount == 1)
                throw new RateLimitException("Rate limit exceeded", 60);
            return "success";
        };
        
        // Act
        var result = await handler.HandleLLMErrorAsync(
            operation,
            new RateLimitException("Rate limit", 1),  // 1 second for test
            CancellationToken.None);
        
        // Assert
        Assert.Equal("success", result);
        Assert.Equal(2, callCount);
    }
    
    [Fact]
    public async Task HandleLLMErrorAsync_WithQuotaExceeded_ThrowsImmediately()
    {
        // Arrange
        var handler = CreateLLMErrorHandler();
        
        Func<Task<string>> operation = async () =>
        {
            throw new QuotaExceededException("Quota exceeded");
        };
        
        // Act & Assert
        await Assert.ThrowsAsync<QuotaExceededException>(
            () => handler.HandleLLMErrorAsync(operation, new Exception(), CancellationToken.None));
    }
}
```

#### 5. Schema Retriever
```csharp
public class SchemaRetrieverTests
{
    [Fact]
    public async Task RetrieveAsync_WithValidQuery_ReturnsRelevantTables()
    {
        // Arrange
        var retriever = CreateSchemaRetriever();
        var schema = CreateTestSchema();
        
        // Act
        var result = await retriever.RetrieveAsync("Show customer orders", schema);
        
        // Assert
        Assert.Contains(result.RelevantTables, t => t.TableName == "Customers");
        Assert.Contains(result.RelevantTables, t => t.TableName == "Orders");
    }
    
    [Fact]
    public async Task RetrieveAsync_WithQdrantUnavailable_FallsBackToKeywordSearch()
    {
        // Arrange
        var mockQdrant = new Mock<IVectorStore>();
        mockQdrant.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<ulong>(), It.IsAny<double>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VectorDBException("Qdrant unavailable"));
        
        var retriever = CreateSchemaRetriever(mockQdrant.Object);
        
        // Act
        var result = await retriever.RetrieveAsync("Show customers", schema);
        
        // Assert
        Assert.NotEmpty(result.RelevantTables);
        Assert.Contains("keyword", result.RetrievalStrategies);
    }
}
```

### Mocking Strategy

**Use Moq for External Dependencies**:
```csharp
// Mock LLM Client
var mockLLM = new Mock<ILLMClient>();
mockLLM.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync("SELECT * FROM Customers");

// Mock Vector Store
var mockVectorStore = new Mock<IVectorStore>();
mockVectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<ulong>(), It.IsAny<double>(), null, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new List<ScoredPoint>());

// Mock SQL Executor
var mockExecutor = new Mock<ISqlExecutor>();
mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new SqlExecutionResult { Success = true });
```

---

## 3. Integration Testing Strategy

### Test Database Setup

**Use In-Memory Database for Tests**:
```csharp
public class IntegrationTestBase : IDisposable
{
    protected AppDbContext DbContext { get; }
    
    public IntegrationTestBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        DbContext = new AppDbContext(options);
        SeedTestData();
    }
    
    private void SeedTestData()
    {
        DbContext.Connections.Add(new Connection
        {
            Id = "test-connection",
            Name = "Test DB",
            Provider = "SqlServer",
            // ...
        });
        
        DbContext.SaveChanges();
    }
    
    public void Dispose()
    {
        DbContext.Dispose();
    }
}
```

### Critical Integration Tests

#### 1. End-to-End Query Processing
```csharp
public class QueryProcessingIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ProcessQuery_WithSimpleQuery_ReturnsSuccessResult()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var request = new QueryRequest
        {
            Question = "Show all customers",
            ConnectionId = "test-connection"
        };
        
        // Act
        var result = await orchestrator.ExecuteAsync(request, CancellationToken.None);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.SqlGenerated);
        Assert.NotNull(result.QueryResultData);
    }
    
    [Fact]
    public async Task ProcessQuery_WithInvalidTable_ReturnsError()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var request = new QueryRequest
        {
            Question = "Show all nonexistent_table",
            ConnectionId = "test-connection"
        };
        
        // Act
        var result = await orchestrator.ExecuteAsync(request, CancellationToken.None);
        
        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
```

#### 2. Conversation Context
```csharp
public class ConversationIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ProcessQuery_WithFollowUpQuestion_UsesContext()
    {
        // Arrange
        var orchestrator = CreateConversationOrchestrator();
        var conversationId = Guid.NewGuid().ToString();
        
        // First query
        await orchestrator.ProcessQueryAsync(
            "Show customer with ID 1",
            conversationId);
        
        // Act - Follow-up query with pronoun
        var result = await orchestrator.ProcessQueryAsync(
            "Show their orders",  // "their" refers to customer from previous query
            conversationId);
        
        // Assert
        Assert.True(result.Success);
        Assert.Contains("Orders", result.SqlGenerated);
        Assert.Contains("CustomerID = 1", result.SqlGenerated);
    }
}
```

#### 3. Self-Correction
```csharp
public class SelfCorrectionIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ExecuteWithSelfCorrection_WithTypo_CorrectsSql()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sqlWithTypo = "SELECT * FROM Custmers";  // Typo: Custmers
        
        // Act
        var (result, corrections) = await orchestrator.ExecuteWithSelfCorrectionAsync(
            sqlWithTypo,
            schema,
            intent,
            CancellationToken.None);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(corrections);
        Assert.Contains("Customers", corrections.Last().CorrectedSql);
    }
}
```

---

## 4. E2E Testing Strategy

### Test Framework
**Use Playwright for E2E tests**:
```typescript
// tests/e2e/query-processing.spec.ts
import { test, expect } from '@playwright/test';

test.describe('Query Processing', () => {
  test('should process simple query successfully', async ({ page }) => {
    // Navigate to app
    await page.goto('http://localhost:5173');
    
    // Login
    await page.fill('[data-testid="email"]', 'test@example.com');
    await page.fill('[data-testid="password"]', 'password');
    await page.click('[data-testid="login-button"]');
    
    // Select connection
    await page.click('[data-testid="connection-select"]');
    await page.click('[data-testid="connection-test-db"]');
    
    // Enter query
    await page.fill('[data-testid="query-input"]', 'Show all customers');
    await page.click('[data-testid="submit-button"]');
    
    // Wait for result
    await page.waitForSelector('[data-testid="query-result"]');
    
    // Assert
    const result = await page.textContent('[data-testid="query-result"]');
    expect(result).toContain('customers');
  });
  
  test('should handle follow-up questions', async ({ page }) => {
    // ... setup ...
    
    // First query
    await page.fill('[data-testid="query-input"]', 'Show customer with ID 1');
    await page.click('[data-testid="submit-button"]');
    await page.waitForSelector('[data-testid="query-result"]');
    
    // Follow-up query
    await page.fill('[data-testid="query-input"]', 'Show their orders');
    await page.click('[data-testid="submit-button"]');
    await page.waitForSelector('[data-testid="query-result"]');
    
    // Assert
    const result = await page.textContent('[data-testid="query-result"]');
    expect(result).toContain('orders');
  });
});
```

### Critical E2E Scenarios

1. **Happy Path**: Login → Select Connection → Query → View Results
2. **Error Handling**: Invalid query → Error message → Retry
3. **Conversation**: Multiple follow-up questions
4. **Pagination**: Large result set → Load more pages
5. **Write Operations**: Preview → Confirm → Execute
6. **DDL Operations**: Preview → Confirm → Execute

---

## 5. Load Testing Strategy

### Test Framework
**Use k6 for load testing**:
```javascript
// tests/load/query-processing.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  stages: [
    { duration: '1m', target: 10 },   // Ramp up to 10 users
    { duration: '3m', target: 50 },   // Ramp up to 50 users
    { duration: '5m', target: 100 },  // Ramp up to 100 users
    { duration: '2m', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<10000'],  // 95% of requests < 10s
    http_req_failed: ['rate<0.05'],      // Error rate < 5%
  },
};

export default function () {
  const url = 'http://localhost:5251/api/agent/process';
  const payload = JSON.stringify({
    question: 'Show all customers',
    connectionId: 'test-connection',
  });
  
  const params = {
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${__ENV.ACCESS_TOKEN}`,
    },
  };
  
  const response = http.post(url, payload, params);
  
  check(response, {
    'status is 200': (r) => r.status === 200,
    'response time < 10s': (r) => r.timings.duration < 10000,
    'has SQL': (r) => JSON.parse(r.body).sqlGenerated !== null,
  });
  
  sleep(1);
}
```

### Load Test Scenarios

1. **Baseline**: 10 concurrent users, simple queries
2. **Normal Load**: 50 concurrent users, mixed queries
3. **Peak Load**: 100 concurrent users, mixed queries
4. **Stress Test**: 200+ concurrent users until failure
5. **Spike Test**: Sudden spike from 10 to 100 users

---

## 6. Security Testing Strategy

### SQL Injection Tests
```csharp
public class SqlInjectionTests
{
    [Theory]
    [InlineData("'; DROP TABLE Users; --")]
    [InlineData("' OR '1'='1")]
    [InlineData("admin' --")]
    [InlineData("1' UNION SELECT * FROM Users --")]
    public async Task ProcessQuery_WithSqlInjection_RejectsQuery(string maliciousInput)
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var request = new QueryRequest
        {
            Question = $"Show user {maliciousInput}",
            ConnectionId = "test-connection"
        };
        
        // Act
        var result = await orchestrator.ExecuteAsync(request, CancellationToken.None);
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("forbidden", result.ErrorMessage.ToLower());
    }
}
```

### Authentication Tests
```csharp
public class AuthenticationTests
{
    [Fact]
    public async Task ProcessMessage_WithoutToken_Returns401()
    {
        // Arrange
        var client = CreateTestClient();
        
        // Act
        var response = await client.PostAsync("/api/agent/process", content);
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task ProcessMessage_WithExpiredToken_Returns401()
    {
        // Arrange
        var client = CreateTestClient();
        var expiredToken = GenerateExpiredToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        
        // Act
        var response = await client.PostAsync("/api/agent/process", content);
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

---

## 7. Test Data Management

### Test Data Builders
```csharp
public class TestDataBuilder
{
    public static DatabaseSchema CreateTestSchema()
    {
        return new DatabaseSchema
        {
            Tables = new List<TableSchema>
            {
                new TableSchema
                {
                    TableName = "Customers",
                    Columns = new List<ColumnSchema>
                    {
                        new ColumnSchema { ColumnName = "CustomerID", DataType = "int", IsPrimaryKey = true },
                        new ColumnSchema { ColumnName = "Name", DataType = "nvarchar" },
                        new ColumnSchema { ColumnName = "Email", DataType = "nvarchar" },
                    }
                },
                new TableSchema
                {
                    TableName = "Orders",
                    Columns = new List<ColumnSchema>
                    {
                        new ColumnSchema { ColumnName = "OrderID", DataType = "int", IsPrimaryKey = true },
                        new ColumnSchema { ColumnName = "CustomerID", DataType = "int", IsForeignKey = true },
                        new ColumnSchema { ColumnName = "OrderDate", DataType = "datetime" },
                    }
                }
            }
        };
    }
}
```

---

## 8. Test Coverage Goals

### Short Term (1 month)
- Unit Tests: 60% coverage
- Integration Tests: 40% coverage
- E2E Tests: 5 critical flows
- Load Tests: 1 baseline scenario

### Medium Term (3 months)
- Unit Tests: 80% coverage
- Integration Tests: 60% coverage
- E2E Tests: 15 user flows
- Load Tests: 5 scenarios
- Security Tests: OWASP Top 10

### Long Term (6 months)
- Unit Tests: 90% coverage
- Integration Tests: 80% coverage
- E2E Tests: 30 user flows
- Load Tests: 10 scenarios
- Security Tests: Comprehensive
- Chaos Engineering: Service failure scenarios

---

## 9. CI/CD Integration

### GitHub Actions Workflow
```yaml
name: Test Suite

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'
      - name: Run Unit Tests
        run: dotnet test --filter Category=Unit --collect:"XPlat Code Coverage"
      - name: Upload Coverage
        uses: codecov/codecov-action@v2
  
  integration-tests:
    runs-on: ubuntu-latest
    services:
      qdrant:
        image: qdrant/qdrant:latest
        ports:
          - 6333:6333
    steps:
      - uses: actions/checkout@v2
      - name: Run Integration Tests
        run: dotnet test --filter Category=Integration
  
  e2e-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Install Playwright
        run: npx playwright install
      - name: Run E2E Tests
        run: npm run test:e2e
```

---

## 10. Test Maintenance

### Best Practices
1. **Keep Tests Fast**: Unit tests < 100ms, Integration tests < 5s
2. **Isolate Tests**: No shared state between tests
3. **Clear Names**: Test name describes scenario and expected result
4. **Arrange-Act-Assert**: Consistent test structure
5. **One Assert Per Test**: Focus on single behavior
6. **Mock External Dependencies**: LLM, Qdrant, SQL Server
7. **Use Test Data Builders**: Consistent test data creation
8. **Clean Up**: Dispose resources properly

### Code Review Checklist
- [ ] All new code has unit tests
- [ ] Critical paths have integration tests
- [ ] Tests are fast and isolated
- [ ] Test names are descriptive
- [ ] No hardcoded values
- [ ] Mocks are used appropriately
- [ ] Tests pass locally and in CI
