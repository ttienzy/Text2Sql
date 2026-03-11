# API Configuration - Quick Reference

## 🚀 Quick Start (3 Steps)

### 1. Set API Key

```bash
# Windows PowerShell
$env:OPENAI_API_KEY="sk-your-openai-key-here"

# Linux/Mac
export OPENAI_API_KEY="sk-your-openai-key-here"
```

### 2. Update Database Connection

Edit `appsettings.Development.json`:
```json
{
  "Database": {
    "ConnectionString": "Server=localhost;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;"
  }
}
```

### 3. Run

```bash
dotnet run
```

---

## ⚙️ Key Configuration Options

### Agent Mode (Phase 7)

```json
{
  "Agent": {
    "UseLegacyMode": false
  }
}
```

- `false` = **ReAct Agent** (autonomous, intelligent) ⭐ RECOMMENDED
- `true` = **Legacy Pipeline** (fixed steps, predictable)

### LLM Provider

```json
{
  "LLMProvider": "OpenAI"
}
```

Options: `"OpenAI"` or `"Gemini"`

### Rate Limiting

```json
{
  "Production": {
    "EnableRateLimiting": true,
    "RateLimitMaxRequests": 100
  }
}
```

---

## 📁 Configuration Files

- `appsettings.json` - Base config
- `appsettings.Development.json` - Dev overrides ⭐ EDIT THIS
- `appsettings.Production.example.json` - Production template
- Environment Variables - Secrets (highest priority)

---

## 🔐 Security (Production)

**Never commit secrets!** Use environment variables:

```bash
$env:OPENAI_API_KEY="sk-prod-key"
$env:Database__ConnectionString="Server=prod;..."
$env:Jwt__Key="your-secret-jwt-key-min-32-chars"
$env:Redis__ConnectionString="redis-server:6379"
```

---

## 📊 Check Configuration

```bash
# Health check
curl http://localhost:5251/api/agent/health

# Check agent mode
curl http://localhost:5251/api/agent/mode
```

---

## 📚 Full Documentation

See [API_CONFIGURATION_GUIDE.md](../docs/API_CONFIGURATION_GUIDE.md) for complete details.

---

## 🆘 Common Issues

### "LLM API key not configured"
→ Set `$env:OPENAI_API_KEY="sk-..."`

### "Cannot connect to database"
→ Check connection string and run `setup-database.ps1`

### "Qdrant connection failed"
→ Optional. Either start Qdrant or ignore (system works without it)

---

**Quick Links:**
- [Run Guide](../docs/HUONG_DAN_CHAY_DU_AN.md)
- [Phase 7 Docs](../docs/PHASE7_COMPLETE.md)
- [Full Config Guide](../docs/API_CONFIGURATION_GUIDE.md)
