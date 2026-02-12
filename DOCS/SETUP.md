# 🔧 Detailed Setup Guide

## Step 1: Install Prerequisites

### 1.1 .NET 10 SDK

Download: https://dotnet.microsoft.com/download/dotnet/10.0

Verify:

```bash
dotnet --version
# Output: 10.0.xxx
```

### 1.2 Docker Desktop

Download: https://www.docker.com/products/docker-desktop

Verify:

```bash
docker --version
# Output: Docker version 2x.x.x
```

### 1.3 SQL Server

- **Option 1**: SQL Server Express (free) - https://www.microsoft.com/sql-server/sql-server-downloads
- **Option 2**: Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourPassword123!" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

### 1.4 Gemini API Key

1. Access: https://aistudio.google.com/app/apikey
2. Sign in with Google account
3. Click "Create API Key"
4. Copy key (format: `AIza...`)

---

## Step 2: Configure Project

### 2.1 Clone repository

```bash
git clone https://github.com/your-username/TextToSqlAgent.git
cd TextToSqlAgent
```

### 2.2 Configure Gemini API Key

**⚠️ IMPORTANT: DO NOT put the API key in appsettings.json!**

Use User Secrets (secure):

```bash
cd TextToSqlAgent.Console
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "YOUR_API_KEY_HERE"
```

Verify:

```bash
dotnet user-secrets list
# Output: Gemini:ApiKey = AIza***
```

> 📍 Secrets are stored at:
>
> - Windows: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
> - Mac/Linux: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

---

## Step 3: Start Qdrant

Qdrant is a vector database to store schema embeddings.

```bash
docker run -d --name qdrant-texttosql \
  -p 6333:6333 \
  -p 6334:6334 \
  -v qdrant_storage:/qdrant/storage \
  qdrant/qdrant
```

Verify:

- Open browser: http://localhost:6333/dashboard
- If you see Qdrant UI → OK ✅

---

## Step 4: Prepare Database

### 4.1 Create Test Database (Optional)

```sql
CREATE DATABASE TextToSqlTest;
GO

USE TextToSqlTest;
GO

CREATE TABLE Categories (
    CategoryId INT PRIMARY KEY IDENTITY,
    CategoryName NVARCHAR(100),
    Description NVARCHAR(MAX)
);

CREATE TABLE Products (
    ProductId INT PRIMARY KEY IDENTITY,
    ProductName NVARCHAR(100),
    CategoryId INT FOREIGN KEY REFERENCES Categories(CategoryId),
    Price DECIMAL(10,2),
    Stock INT
);

-- Insert sample data
INSERT INTO Categories VALUES (N'Electronics', N'Electronic products');
INSERT INTO Categories VALUES (N'Fashion', N'Clothing, footwear');

INSERT INTO Products VALUES (N'iPhone 15', 1, 25000000, 50);
INSERT INTO Products VALUES (N'Samsung Galaxy', 1, 20000000, 30);
INSERT INTO Products VALUES (N'T-shirt', 2, 200000, 100);
```

### 4.2 Create Read-Only User (Recommended)

```sql
CREATE LOGIN TextToSqlReader WITH PASSWORD = '@TextToSqlReader!';
USE TextToSqlTest;
CREATE USER TextToSqlReader FOR LOGIN TextToSqlReader;
GRANT SELECT TO TextToSqlReader;
```

---

## Step 5: Build and Run

```bash
cd TextToSqlAgent
dotnet build
cd TextToSqlAgent.Console
dotnet run
```

---

## Step 6: Database Connection

When the app starts, select **"🔧 Build New Connection"**:

```
1️⃣  Server: .
2️⃣  Database: TextToSqlTest
3️⃣  User ID: TextToSqlReader
4️⃣  Password: @TextToSqlReader!
```

---

## ✅ Verification Checklist

- [ ] `dotnet --version` shows 10.0.x
- [ ] `docker ps` shows qdrant-texttosql running
- [ ] http://localhost:6333/dashboard accessible
- [ ] `dotnet user-secrets list` shows Gemini:ApiKey
- [ ] `dotnet run` does not report API key error
- [ ] Can connect to database successfully

---

## ❓ FAQ

**Q: Where are User secrets stored?**
A: `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` (Windows)

**Q: How to delete secrets?**
A: `dotnet user-secrets clear`

**Q: Can I use Environment Variables?**
A: Yes! Set `GEMINI__APIKEY=your_key` (note: use `__` instead of `:`)

**Q: Where is Qdrant data stored?**
A: Docker volume `qdrant_storage`

**Q: What is Rate limit 429?**
A: Gemini free tier has limits. Wait 1 minute and try again.
