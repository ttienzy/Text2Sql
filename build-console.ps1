# =============================================
# Build Script for TextToSqlAgent Console
# Creates self-contained executable
# =============================================

Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     TextToSqlAgent Console - Build Script                     ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Configuration
$ProjectPath = "TextToSqlAgent.Console"
$OutputDir = "dist"
$Runtime = "win-x64"  # Change to linux-x64 or osx-x64 for other platforms

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Project: $ProjectPath" -ForegroundColor White
Write-Host "  Runtime: $Runtime" -ForegroundColor White
Write-Host "  Output:  $OutputDir" -ForegroundColor White
Write-Host ""

# Clean previous build
if (Test-Path $OutputDir) {
    Write-Host "Cleaning previous build..." -ForegroundColor Yellow
    Remove-Item -Path $OutputDir -Recurse -Force
}

# Build
Write-Host "Building self-contained executable..." -ForegroundColor Yellow
Write-Host ""

$buildArgs = @(
    "publish",
    $ProjectPath,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $OutputDir
)

dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Build successful!" -ForegroundColor Green
Write-Host ""

# Copy additional files
Write-Host "Copying additional files..." -ForegroundColor Yellow

# Copy appsettings.json
Copy-Item -Path "$ProjectPath/appsettings.json" -Destination $OutputDir -Force
Write-Host "  ✓ appsettings.json" -ForegroundColor Green

# Copy setup-test-db.sql if exists
if (Test-Path "setup-test-db.sql") {
    Copy-Item -Path "setup-test-db.sql" -Destination $OutputDir -Force
    Write-Host "  ✓ setup-test-db.sql" -ForegroundColor Green
}

# Create README.txt
$readmeContent = @"
TextToSqlAgent Console - Quick Start
=====================================

Version: 2.0.0
Date: $(Get-Date -Format "yyyy-MM-dd")

QUICK START
-----------

1. Run TextToSqlAgent.Console.exe

2. First-time setup wizard will guide you through:
   - Enter your OpenAI API key
   - Connect to your database

3. Start querying!
   Example: "Show me all customers"

COMMANDS
--------

Configuration:
  /config          View and update configuration
  /api-key         Update OpenAI API key
  /reset           Reset all configuration

Database:
  switch db        Change database connection
  show db          Show current connection

Schema:
  index            Index database schema
  reindex          Re-index schema
  check index      Check index status

Help:
  help             Show all commands
  examples         Show example questions

REQUIREMENTS
------------

- OpenAI API Key (get from https://platform.openai.com)
- SQL Server database (or other supported database)
- Qdrant vector database (optional, for better performance)
  Docker: docker run -d -p 6333:6333 qdrant/qdrant

CONFIGURATION
-------------

API keys are stored securely at:
  Windows: %APPDATA%\TextToSqlAgent\
  Linux:   ~/.config/TextToSqlAgent/

You can also set environment variable:
  OPENAI_API_KEY=sk-your-key

TROUBLESHOOTING
---------------

"OpenAI API key not configured"
  → Run /config command to set your API key

"Cannot connect to database"
  → Check your connection string
  → Verify SQL Server is running

"Qdrant connection failed"
  → Qdrant is optional, app works without it
  → Or start Qdrant: docker run -d -p 6333:6333 qdrant/qdrant

SUPPORT
-------

For more information, visit:
  https://github.com/your-repo/TextToSqlAgent

Happy Querying! 🚀
"@

$readmeContent | Out-File -FilePath "$OutputDir/README.txt" -Encoding UTF8
Write-Host "  ✓ README.txt" -ForegroundColor Green

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "✓ Build Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Output location:" -ForegroundColor Yellow
Write-Host "  $((Get-Item $OutputDir).FullName)" -ForegroundColor White
Write-Host ""
Write-Host "Files:" -ForegroundColor Yellow
Get-ChildItem $OutputDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { "{0:N2} MB" -f ($_.Length / 1MB) } else { "{0:N2} KB" -f ($_.Length / 1KB) }
    Write-Host "  $($_.Name) ($size)" -ForegroundColor White
}
Write-Host ""
Write-Host "To test:" -ForegroundColor Cyan
Write-Host "  cd $OutputDir" -ForegroundColor White
Write-Host "  .\TextToSqlAgent.Console.exe" -ForegroundColor White
Write-Host ""
Write-Host "To distribute:" -ForegroundColor Cyan
Write-Host "  Zip the $OutputDir folder and share!" -ForegroundColor White
Write-Host ""
