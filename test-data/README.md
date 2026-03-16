# Integration Test Setup with Docker Compose

## P1-07: Integration Test Standardization

This directory contains the setup for running integration tests with reproducible test data using Docker Compose.

## Quick Start

### 1. Start Test Infrastructure

```bash
# Start all test services (SQL Server, Qdrant, Redis)
docker-compose -f docker-compose.test.yml up -d

# Wait for services to be healthy (about 30 seconds)
docker-compose -f docker-compose.test.yml ps

# Check SQL Server logs to confirm database initialization
docker logs texttosql-test-db
```

### 2. Initialize Test Database

The database is automatically initialized when the container starts using `init-db.sql`. However, SQL Server's docker image doesn't run init scripts automatically. You need to run it manually:

```bash
# Run the initialization script
docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!" < test-data/init-db.sql
```

Or on Windows PowerShell:

```powershell
Get-Content test-data/init-db.sql | docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!"
```

### 3. Run Integration Tests

```bash
# Run all integration tests
dotnet test TextToSqlAgent.Tests.Integration

# Run specific test class
dotnet test TextToSqlAgent.Tests.Integration --filter "FullyQualifiedName~SqlExecutorTests"

# Run with verbose output
dotnet test TextToSqlAgent.Tests.Integration --logger "console;verbosity=detailed"
```

### 4. Stop Test Infrastructure

```bash
# Stop and remove containers (keeps volumes)
docker-compose -f docker-compose.test.yml down

# Stop and remove everything including volumes
docker-compose -f docker-compose.test.yml down -v
```

## Test Services

### SQL Server 2022
- **Port**: 1433
- **Container**: texttosql-test-db
- **SA Password**: Test@Pass123!
- **Test Database**: TextToSqlTest
- **Test User**: TextToSqlReader / Reader@2024!Strong
- **Health Check**: Every 10s

### Qdrant Vector Database
- **HTTP Port**: 6333
- **gRPC Port**: 6334
- **Container**: texttosql-test-qdrant
- **Health Check**: Every 10s
- **Volume**: qdrant-test-data

### Redis Cache
- **Port**: 6379
- **Container**: texttosql-test-redis
- **Health Check**: Every 10s

## Test Database Schema

The `init-db.sql` script creates:

### Tables
- **Customers** (10 rows): CustomerId, FullName, Email, Phone, City, Country, CreatedAt
- **Categories** (5 rows): CategoryId, CategoryName, Description
- **Products** (10 rows): ProductId, ProductName, CategoryId, UnitPrice, UnitsInStock, Discontinued
- **Orders** (10 rows): OrderId, CustomerId, OrderDate, ShipDate, TotalAmount, Status
- **OrderDetails** (13 rows): OrderDetailId, OrderId, ProductId, Quantity, UnitPrice, Discount

### Relationships
- Orders.CustomerId → Customers.CustomerId
- OrderDetails.OrderId → Orders.OrderId
- OrderDetails.ProductId → Products.ProductId
- Products.CategoryId → Categories.CategoryId

### Indexes
- IX_Orders_CustomerId
- IX_Orders_OrderDate
- IX_OrderDetails_OrderId
- IX_OrderDetails_ProductId
- IX_Products_CategoryId

## Environment Variables

Tests read connection strings from environment variables (optional):

```bash
# SQL Server connection string
export TEST_SQL_CONNECTION_STRING="Server=localhost,1433;Database=TextToSqlTest;User Id=sa;Password=Test@Pass123!;TrustServerCertificate=True;"

# Qdrant URL
export TEST_QDRANT_URL="http://localhost:6333"

# Redis connection string
export TEST_REDIS_CONNECTION_STRING="localhost:6379"
```

If not set, tests use default docker-compose values.

## Troubleshooting

### SQL Server not starting
```bash
# Check logs
docker logs texttosql-test-db

# Common issues:
# - Port 1433 already in use (stop local SQL Server)
# - Insufficient memory (Docker needs at least 2GB)
```

### Database not initialized
```bash
# Manually run init script
docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!" < test-data/init-db.sql

# Verify tables exist
docker exec -it texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!" -Q "USE TextToSqlTest; SELECT name FROM sys.tables;"
```

### Connection refused errors
```bash
# Wait for health checks to pass
docker-compose -f docker-compose.test.yml ps

# All services should show "healthy" status
# If not, wait 30 seconds and check again
```

### Tests fail with "Invalid object name"
```bash
# Database might not be initialized
# Run the init script manually (see step 2 above)
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      
      - name: Start test infrastructure
        run: |
          docker-compose -f docker-compose.test.yml up -d
          sleep 30  # Wait for services to be healthy
      
      - name: Initialize test database
        run: |
          docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!" < test-data/init-db.sql
      
      - name: Run integration tests
        run: dotnet test TextToSqlAgent.Tests.Integration --logger "trx;LogFileName=test-results.trx"
      
      - name: Stop test infrastructure
        if: always()
        run: docker-compose -f docker-compose.test.yml down -v
```

## Test Data Maintenance

### Reset Test Data
```bash
# Stop containers
docker-compose -f docker-compose.test.yml down -v

# Start fresh
docker-compose -f docker-compose.test.yml up -d
sleep 30

# Re-initialize
docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!" < test-data/init-db.sql
```

### Modify Test Data
1. Edit `test-data/init-db.sql`
2. Reset test data (see above)
3. Run tests to verify

## Performance

- Container startup: ~20 seconds
- Database initialization: ~5 seconds
- Test execution: ~2-5 seconds (6 tests)
- Total time: ~30 seconds

## Security Notes

- Test credentials are hardcoded for reproducibility
- **DO NOT** use these credentials in production
- Test database is isolated in docker network
- Containers should only be used for testing
