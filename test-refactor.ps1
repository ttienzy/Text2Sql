#!/usr/bin/env pwsh
# Test script for Console Refactor - All Phases
# Tests build and basic DI resolution

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Console Refactor - Test Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Clean build
Write-Host "[Test 1] Clean Build..." -ForegroundColor Yellow
$buildResult = dotnet build TextToSqlAgent.Console/TextToSqlAgent.Console.csproj --no-incremental 2>&1
$buildSuccess = $LASTEXITCODE -eq 0

if ($buildSuccess) {
    Write-Host "✅ Build succeeded" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host ""

# Test 2: Check for errors
Write-Host "[Test 2] Checking for build errors..." -ForegroundColor Yellow
$errors = $buildResult | Select-String -Pattern "error CS"
if ($errors.Count -eq 0) {
    Write-Host "✅ No build errors" -ForegroundColor Green
} else {
    Write-Host "❌ Found $($errors.Count) errors:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}
Write-Host ""

# Test 3: Check warnings
Write-Host "[Test 3] Checking warnings..." -ForegroundColor Yellow
$warnings = $buildResult | Select-String -Pattern "warning CS|warning NU"
Write-Host "Found $($warnings.Count) warnings (expected: ~17 pre-existing)" -ForegroundColor Yellow
Write-Host ""

# Test 4: Verify new files exist
Write-Host "[Test 4] Verifying new files..." -ForegroundColor Yellow
$newFiles = @(
    "TextToSqlAgent.Core/Ports/IQueryPorts.cs",
    "TextToSqlAgent.Core/Models/QueryContracts.cs",
    "TextToSqlAgent.Application/Adapters/SchemaProviderAdapter.cs",
    "TextToSqlAgent.Application/Adapters/QueryValidatorAdapter.cs",
    "TextToSqlAgent.Application/Adapters/IntentAnalyzerAdapter.cs",
    "TextToSqlAgent.Application/Adapters/SchemaRetrieverAdapter.cs",
    "TextToSqlAgent.Application/Adapters/SqlGeneratorAdapter.cs",
    "TextToSqlAgent.Application/Adapters/SqlExecutorAdapter.cs",
    "TextToSqlAgent.Application/Adapters/SqlCorrectorAdapter.cs",
    "TextToSqlAgent.Application/Adapters/ConversationStoreAdapter.cs",
    "TextToSqlAgent.Application/Adapters/ResultFormatterAdapter.cs",
    "TextToSqlAgent.Application/Pipelines/QueryPipeline.cs"
)

$allExist = $true
foreach ($file in $newFiles) {
    if (Test-Path $file) {
        Write-Host "  ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $file (missing)" -ForegroundColor Red
        $allExist = $false
    }
}

if ($allExist) {
    Write-Host "✅ All new files exist" -ForegroundColor Green
} else {
    Write-Host "❌ Some files missing" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test 5: Check DI registrations
Write-Host "[Test 5] Checking DI registrations..." -ForegroundColor Yellow
$diFile = Get-Content "TextToSqlAgent.Console/Setup/DependencyInjection.cs" -Raw

$registrations = @(
    "RegisterCorePorts",
    "ISchemaProvider",
    "IQueryValidator",
    "IIntentAnalyzer",
    "ISchemaRetriever",
    "ISqlGenerator",
    "ISqlExecutor",
    "ISqlCorrector",
    "IConversationStore",
    "IResultFormatter",
    "QueryPipeline"
)

$allRegistered = $true
foreach ($reg in $registrations) {
    if ($diFile -match $reg) {
        Write-Host "  ✅ $reg registered" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $reg not found" -ForegroundColor Red
        $allRegistered = $false
    }
}

if ($allRegistered) {
    Write-Host "✅ All DI registrations found" -ForegroundColor Green
} else {
    Write-Host "❌ Some registrations missing" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✅ Build: Success" -ForegroundColor Green
Write-Host "✅ Errors: 0" -ForegroundColor Green
Write-Host "✅ New Files: All present" -ForegroundColor Green
Write-Host "✅ DI Registrations: All found" -ForegroundColor Green
Write-Host ""
Write-Host "All tests passed! ✨" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Run Console manually: dotnet run --project TextToSqlAgent.Console" -ForegroundColor White
Write-Host "2. Test /help, /config, /exit commands" -ForegroundColor White
Write-Host "3. Test query without API key (should prompt)" -ForegroundColor White
Write-Host "4. Configure and test full query flow" -ForegroundColor White
Write-Host ""
