# API Configuration - Quick Reference

## 🚀 Quick Start (3 Steps)

### 1. Set Required Environment Variables

```bash
# Windows PowerShell
$env:JWT_SECRET="your-secure-jwt-secret-key-minimum-32-characters"
$env:OPENAI_API_KEY="sk-your-openai-key-here"

# Linux/Mac
export JWT_SECRET="your-secure-jwt-secret-key-minimum-32-characters"
export OPENAI_API_KEY="sk-your-openai-key-here"
```

### 2. Optional: Update Database Connection

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

### Environment Configuration

The API supports multiple environments with different settings:

- **Development** (`appsettings.Development.json`) - Debug logging, SQLite database
- **Staging** (`appsettings.Staging.json`) - Production-like with verbose logging  
- **Production** (`appsettings.Production.json`) - Optimized performance, minimal logging

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

### Security & Performance

```json
{
  "Production": {
    "EnableRateLimiting": true,
    "RateLimitMaxRequests": 100,
    "EnableCaching": true,
    "EnableSqlInjectionPrevention": true
  }
}
```

---

## 📁 Configuration Files

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides ⭐ EDIT THIS
- `appsettings.Staging.json` - Staging environment settings
- `appsettings.Production.json` - Production environment settings
- `.env` - Environment variables (copy from `.env.example`)

**Configuration Priority:** Environment Variables > .env file > appsettings.{Environment}.json > appsettings.json

---

## 🔐 Security (Production)

**Never commit secrets!** Use environment variables:

```bash
# Required
$env:JWT_SECRET="your-secure-jwt-key-min-32-chars"
$env:ENCRYPTION_KEY="your-secure-encryption-key-min-32-chars"
$env:OPENAI_API_KEY="sk-prod-key"

# Optional
$env:DATABASE_CONNECTION_STRING="Server=prod;..."
$env:QDRANT_API_KEY="your-qdrant-key"
```

---

## 📊 Check Configuration

```bash
# Health check
curl http://localhost:5000/api/health

# Check configuration validation
# (Check startup logs for validation results)
```

---

## 🆘 Common Issues

### "Configuration validation failed"
→ Check startup logs for specific errors
→ Ensure JWT_SECRET and required API keys are set

### "JWT Key is not configured"
→ Set `$env:JWT_SECRET="your-secure-key-min-32-chars"`

### "OpenAI API key is not configured"
→ Set `$env:OPENAI_API_KEY="sk-..."`

### "Cannot connect to database"
→ Check connection string and run database setup

### "Qdrant connection failed"
→ Optional. System works without Qdrant (uses fallback storage)

---

## 📚 Complete Documentation

See [CONFIGURATION_GUIDE.md](CONFIGURATION_GUIDE.md) for comprehensive configuration details including:
- Environment-specific setup
- Security best practices
- Performance tuning
- Troubleshooting guide
- Production deployment checklist

---

**Quick Links:**
- [Complete Configuration Guide](CONFIGURATION_GUIDE.md) ⭐ **NEW**
- [Environment Variables Example](.env.example)
- [Run Guide](../docs/HUONG_DAN_CHAY_DU_AN.md)
- [Phase 7 Docs](../docs/PHASE7_COMPLETE.md)
