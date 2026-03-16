# TextToSqlAgent API Configuration Guide

## Overview

The TextToSqlAgent API uses a hierarchical configuration system that supports multiple environments and secure secrets management. This guide explains how to configure the API for different deployment scenarios.

## Configuration Priority

Configuration values are loaded in the following order (highest to lowest priority):

1. **Environment Variables** (highest priority)
2. **.env file** (development convenience)
3. **appsettings.{Environment}.json** (environment-specific)
4. **appsettings.json** (base configuration)

## Required Configuration

### Security Configuration

#### JWT Secret Key
**Required for all environments**

```bash
# Environment Variable (recommended)
JWT_SECRET=your-secure-jwt-secret-key-minimum-32-characters-long

# Or in appsettings.json
{
  "Jwt": {
    "Key": "your-secure-jwt-secret-key-minimum-32-characters-long"
  }
}
```

**Security Requirements:**
- Minimum 32 characters
- Use cryptographically secure random generation
- Rotate regularly in production

#### Encryption Key
**Required for database password encryption**

```bash
# Environment Variable (recommended)
ENCRYPTION_KEY=your-secure-encryption-key-minimum-32-characters-long

# Or in appsettings.json
{
  "Encryption": {
    "Key": "your-secure-encryption-key-minimum-32-characters-long"
  }
}
```

### AI Provider Configuration

#### OpenAI (Default Provider)
```bash
# Environment Variable
OPENAI_API_KEY=sk-your-openai-api-key-here

# Or in appsettings.json
{
  "OpenAI": {
    "ApiKey": "sk-your-openai-api-key-here"
  }
}
```

#### Google Gemini (Alternative Provider)
```bash
# Environment Variable
GEMINI_API_KEY=your-gemini-api-key-here

# Or in appsettings.json
{
  "Gemini": {
    "ApiKey": "your-gemini-api-key-here"
  }
}
```

## Environment-Specific Configuration

### Development Environment

**File:** `appsettings.Development.json`

Key characteristics:
- Debug logging enabled
- Detailed error messages
- Development-friendly timeouts
- SQLite database by default

**Setup:**
1. Copy `.env.example` to `.env`
2. Fill in your API keys
3. Run: `dotnet run --environment Development`

### Staging Environment

**File:** `appsettings.Staging.json`

Key characteristics:
- Information-level logging
- Production-like settings with more verbose logging
- Intermediate performance settings
- Full error tracking

**Setup:**
1. Set environment: `ASPNETCORE_ENVIRONMENT=Staging`
2. Configure production database connection
3. Set secure JWT and encryption keys

### Production Environment

**File:** `appsettings.Production.json`

Key characteristics:
- Warning-level logging only
- Optimized performance settings
- Enhanced security features
- Minimal resource usage

**Setup:**
1. Set environment: `ASPNETCORE_ENVIRONMENT=Production`
2. Use environment variables for all secrets
3. Configure production database and vector store
4. Enable monitoring and telemetry

## Optional Configuration

### Database Configuration

```bash
# SQL Server (recommended for production)
DATABASE_CONNECTION_STRING="Server=localhost;Database=TextToSqlAgent;User Id=sa;Password=YourPassword;TrustServerCertificate=True;"

# PostgreSQL
DATABASE_CONNECTION_STRING="Host=localhost;Database=TextToSqlAgent;Username=user;Password=password;"

# MySQL
DATABASE_CONNECTION_STRING="Server=localhost;Database=TextToSqlAgent;Uid=user;Pwd=password;"

# SQLite (development only)
DATABASE_CONNECTION_STRING="Data Source=textosqlagent.db"
```

### Vector Database (Qdrant)

```bash
# Qdrant Configuration
QDRANT_URL=http://localhost:6333
QDRANT_API_KEY=your-qdrant-api-key-here
QDRANT_COLLECTION_NAME=schema_embeddings
```

**Note:** Qdrant is optional. The system will fall back to in-memory vector storage if Qdrant is unavailable.

### Performance Tuning

```bash
# Agent Configuration
AGENT__MAXITERATIONS=12
AGENT__ENABLEREFLECTION=true
AGENT__REASONINGDEPTH=deep

# Caching
PRODUCTION__ENABLECACHING=true
PRODUCTION__CACHETTLMINUTES=60

# Rate Limiting
PRODUCTION__ENABLERATELIMITING=true
PRODUCTION__RATELIMITMAXREQUESTS=100
```

## Configuration Validation

The API performs comprehensive configuration validation on startup:

### Validation Checks
- JWT key presence and length
- Encryption key presence and length
- AI provider API key validation
- Database connection string format
- Environment-specific security settings

### Validation Results
- **Errors:** Prevent application startup
- **Warnings:** Logged but allow startup
- **Info:** Configuration recommendations

## Logging Configuration

### Structured Logging with Serilog

The API uses Serilog for structured logging with environment-specific configuration:

#### Development
- Console output with colored formatting
- File logging with detailed information
- Debug-level logging enabled

#### Staging
- JSON-formatted logs for parsing
- Information-level logging
- Extended retention period

#### Production
- JSON-formatted logs only
- Warning-level logging minimum
- Optimized for performance
- Long-term retention

### Log Correlation

Every request gets a unique correlation ID for distributed tracing:
- Added to response headers: `X-Correlation-ID`
- Included in all log entries
- Useful for debugging across services

## Security Best Practices

### Secrets Management

1. **Never commit secrets to version control**
2. **Use environment variables in production**
3. **Rotate keys regularly**
4. **Use platform-specific secret management:**
   - Azure Key Vault
   - AWS Secrets Manager
   - Kubernetes Secrets
   - HashiCorp Vault

### Environment Variables

```bash
# Windows PowerShell
$env:JWT_SECRET="your-secret-here"
$env:OPENAI_API_KEY="sk-your-key-here"

# Windows Command Prompt
set JWT_SECRET=your-secret-here
set OPENAI_API_KEY=sk-your-key-here

# Linux/Mac
export JWT_SECRET="your-secret-here"
export OPENAI_API_KEY="sk-your-key-here"
```

### Production Checklist

- [ ] JWT_SECRET is unique and secure (32+ characters)
- [ ] ENCRYPTION_KEY is unique and secure (32+ characters)
- [ ] AI provider API keys are valid
- [ ] Database connection uses encrypted connection
- [ ] AllowedHosts is restricted (not "*")
- [ ] Rate limiting is enabled
- [ ] HTTPS is enforced
- [ ] Logging level is appropriate (Warning/Error)
- [ ] Secrets are not in configuration files

## Troubleshooting

### Common Configuration Issues

#### "JWT Key is not configured"
**Solution:** Set `JWT_SECRET` environment variable or `Jwt:Key` in configuration

#### "OpenAI API key is not configured"
**Solution:** Set `OPENAI_API_KEY` environment variable or `OpenAI:ApiKey` in configuration

#### "Configuration validation failed"
**Solution:** Check the startup logs for specific validation errors

#### "Cannot connect to database"
**Solution:** Verify `DATABASE_CONNECTION_STRING` and database server availability

#### "Qdrant connection failed"
**Solution:** This is optional - the system will work without Qdrant using fallback storage

### Health Checks

Use the health check endpoint to verify configuration:

```bash
curl http://localhost:5000/api/health
```

### Configuration Debugging

Enable debug logging to see configuration loading:

```bash
# Temporary debug logging
LOGGING__LOGLEVEL__DEFAULT=Debug dotnet run
```

## Example Configurations

### Minimal Development Setup

**.env file:**
```bash
JWT_SECRET=development-jwt-secret-key-32-chars-min
OPENAI_API_KEY=sk-your-openai-key-here
```

### Complete Production Setup

**Environment Variables:**
```bash
# Security
JWT_SECRET=prod-secure-jwt-key-generated-randomly-32-chars-minimum
ENCRYPTION_KEY=prod-secure-encryption-key-generated-randomly-32-chars-min

# AI Provider
OPENAI_API_KEY=sk-prod-openai-key-here

# Database
DATABASE_CONNECTION_STRING="Server=prod-db-server;Database=TextToSqlAgent;User Id=api_user;Password=secure_password;TrustServerCertificate=False;Encrypt=True;"

# Vector Database
QDRANT_URL=https://your-qdrant-cluster.qdrant.io
QDRANT_API_KEY=your-production-qdrant-key

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

## Support

For configuration issues:
1. Check the application logs for specific error messages
2. Verify environment variables are set correctly
3. Use the health check endpoint to validate configuration
4. Review this guide for required vs. optional settings