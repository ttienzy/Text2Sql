# TextToSQL Agent

AI-powered Natural Language to SQL Converter

## 🚀 Quick Setup

### 1. Clone Repository
```bash
git clone <repository-url>
cd TextToSqlAgent
```

### 2. Setup API Environment
```bash
# Copy environment file
cp TextToSqlAgent.API/.env.example TextToSqlAgent.API/.env

# Edit .env file with your settings:
# - OPENAI_API_KEY=sk-your-openai-api-key
# - DATABASE_CONNECTION_STRING=your-connection-string
```

### 3. Database Migration
```bash
# Run Entity Framework migrations
cd TextToSqlAgent.Infrastructure
dotnet ef database update

# Or setup test database (optional)
sqlcmd -S localhost -U sa -P 123 -i ../test-data/setup-test-db.sql
```

## 🗄️ Database Migrations

### Prerequisites
```bash
# Install EF Core tools (REQUIRED)
dotnet tool install --global dotnet-ef

# Verify installation
dotnet ef --version
```

### Migration Commands
```bash
# Navigate to Infrastructure project
cd TextToSqlAgent.Infrastructure

# Create new migration
dotnet ef migrations add MigrationName

# Update database with latest migrations
dotnet ef database update

# Update to specific migration
dotnet ef database update MigrationName

# Remove last migration (if not applied to database)
dotnet ef migrations remove

# Generate SQL script for migrations
dotnet ef migrations script

# Drop database (careful!)
dotnet ef database drop
```

### Troubleshooting

**Error: "Could not execute because the specified command or file was not found"**
```bash
# Install EF Core tools
dotnet tool install --global dotnet-ef

# If still not working, update tools
dotnet tool update --global dotnet-ef

# Check if tools are in PATH
dotnet tool list --global
```

**Error: "No DbContext was found"**
```bash
# Make sure you're in the Infrastructure project
cd TextToSqlAgent.Infrastructure

# Or specify the project explicitly
dotnet ef database update --project TextToSqlAgent.Infrastructure
```

**Error: "Connection string not found"**
```bash
# Make sure .env file exists in API project
ls ../TextToSqlAgent.API/.env

# Check connection string in .env
cat ../TextToSqlAgent.API/.env | grep DATABASE_CONNECTION_STRING
```

### Common Scenarios
```bash
# Initial setup - create database and apply all migrations
cd TextToSqlAgent.Infrastructure
dotnet ef database update

# After pulling new code with migrations
dotnet ef database update

# Create migration for model changes
dotnet ef migrations add AddNewFeature
dotnet ef database update

# Rollback to previous migration
dotnet ef database update PreviousMigrationName
```

### 4. Run Application
```bash
# Start API
dotnet run --project TextToSqlAgent.API

# Start Frontend (new terminal)
cd frontend
npm install
npm run dev
```

**Access:** http://localhost:5173

## 🔧 Environment Variables

**Required in TextToSqlAgent.API/.env:**
```
OPENAI_API_KEY=sk-your-openai-api-key
DATABASE_CONNECTION_STRING=Server=localhost;Database=YourDB;User Id=sa;Password=123;TrustServerCertificate=True;
```

**Optional:**
```
QDRANT_URL=http://localhost:6333
JWT_SECRET=your-secure-jwt-secret-32-chars-min
REDIS_CONNECTION_STRING=localhost:6379
```

## 📊 Features

- **AI-Powered:** OpenAI GPT-4o with 95% accuracy
- **Multi-Database:** SQL Server, PostgreSQL, MySQL, SQLite
- **Enterprise Ready:** Security, caching, monitoring
- **Web Interface:** React frontend with real-time results

## 🧪 Test Queries

```
"Show me all customers"
"Top 10 products by revenue"
"Monthly sales report for 2024"
"Customers who haven't ordered in 6 months"
```

## 🛠️ Tech Stack

- **Backend:** .NET 10, ASP.NET Core, Entity Framework
- **Frontend:** React 19, Vite, Ant Design
- **AI:** OpenAI GPT-4o, Semantic Kernel
- **Database:** SQL Server 2022, Qdrant (vector DB)