# 🤖 Text To SQL Agent

Convert natural language questions into SQL queries using AI (Gemini 2.5 Flash).

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Gemini](https://img.shields.io/badge/AI-Gemini%202.5-green)
![Qdrant](https://img.shields.io/badge/VectorDB-Qdrant-red)

## ✨ Features

- 🗣️ **Natural Language Support** - Ask questions in natural language
- 🤖 **AI-Powered** - Uses Google Gemini 2.5 Flash
- 🔍 **RAG** - Semantic search with Qdrant vector database
- 🔄 **Self-Correction** - Automatically corrects SQL errors
- 📊 **Beautiful Results** - Professional table presentation

## 📋 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for Qdrant)
- SQL Server (local or remote)
- [Gemini API Key](https://aistudio.google.com/app/apikey)

---

## 🚀 Installation

### 1. Clone repository

```bash
git clone https://github.com/your-username/TextToSqlAgent.git
cd TextToSqlAgent
```

### 2. Configure Gemini API Key

**⚠️ IMPORTANT**: Never commit the API key to Git!

Use **User Secrets** (secure, not committed):

```bash
cd TextToSqlAgent.Console
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY"
```

> 💡 Get a free API key at: https://aistudio.google.com/app/apikey

### 3. Start Qdrant (Vector Database)

```bash
docker run -d --name qdrant-texttosql \
  -p 6333:6333 -p 6334:6334 \
  qdrant/qdrant
```

Verify: Open http://localhost:6333/dashboard

### 4. Build project

```bash
dotnet build
```

### 5. Run application

```bash
cd TextToSqlAgent.Console
dotnet run
```

---

## 🔧 Database Configuration

When running the app, you will be asked for connection information:

```
🔧 Database Connection Setup

1️⃣  Server: localhost        (or . or .\SQLEXPRESS)
2️⃣  Database: YourDbName
3️⃣  User ID: your_username
4️⃣  Password: ********
```

App automatically:

- ✅ Sets `TrustServerCertificate=True`
- ✅ Saves connection for future use

---

## 💬 Usage

### Example Questions

```
💬 Question #1: How many customers are there?
💬 Question #2: List top 5 best-selling products
💬 Question #3: Orders from customer Nguyen Van A
💬 Question #4: Total revenue this month
```

### Useful Commands

| Command       | Description              |
| ------------- | ------------------------ |
| `help`        | Show list of commands    |
| `examples`    | View example questions   |
| `show db`     | View current database    |
| `switch db`   | Switch to other database |
| `clear cache` | Clear schema cache       |
| `exit`        | Exit                     |

---

## 📁 Project Structure

```
TextToSqlAgent/
├── TextToSqlAgent.Console/     # Main console application
├── TextToSqlAgent.Core/        # Business logic
├── TextToSqlAgent.Infrastructure/  # Database, AI, Vector DB
├── TextToSqlAgent.Plugins/     # SK Plugins (Intent, SQL Gen, Correction)
└── TextToSqlAgent.Tests.*/     # Unit & Integration tests
```

---

## ⚙️ Advanced Configuration

File `appsettings.json`:

```json
{
  "Gemini": {
    "Model": "gemini-2.5-flash-lite",
    "EmbeddingModel": "gemini-embedding-001",
    "MaxTokens": 8192,
    "Temperature": 0.1
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "VectorSize": 3072
  },
  "Agent": {
    "MaxSelfCorrectionAttempts": 3,
    "EnableSQLExplanation": true
  }
}
```

---

## 🔒 Security

- ✅ **API Key in User Secrets** - Not committed to Git
- ✅ **Hidden Password** when typing in console
- ✅ **Masked Connection strings** when displayed
- ✅ Connections saved at `%AppData%\TextToSqlAgent\`

---

## ❓ Troubleshooting

### "GEMINI_API_KEY not found"

```bash
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY"
```

### "Cannot connect to Qdrant"

```bash
docker start qdrant-texttosql
# or create new:
docker run -d --name qdrant-texttosql -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

### "429 Too Many Requests"

Gemini API has rate limits. Wait 1 minute and try again.

### "Vector size mismatch"

Delete old collection:

```powershell
Invoke-WebRequest -Uri "http://localhost:6333/collections/schema_embeddings" -Method DELETE
```

---

## 📝 License

MIT License

---

## 🤝 Contributing

1. Fork repository
2. Create feature branch: `git checkout -b feature/amazing-feature`
3. Commit changes: `git commit -m 'Add amazing feature'`
4. Push: `git push origin feature/amazing-feature`
5. Open Pull Request

---

## 📧 Contact

- GitHub Issues: [Create new issue](https://github.com/your-username/TextToSqlAgent/issues)
