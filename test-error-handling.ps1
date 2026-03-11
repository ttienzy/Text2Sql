#!/usr/bin/env pwsh
# Test script for enhanced error handling

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Enhanced Error Handling" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean and rebuild
Write-Host "Step 1: Cleaning and rebuilding..." -ForegroundColor Yellow
dotnet clean TextToSqlAgent.Console/TextToSqlAgent.Console.csproj --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Clean failed" -ForegroundColor Red
    exit 1
}

dotnet build TextToSqlAgent.Console/TextToSqlAgent.Console.csproj --no-incremental --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build successful" -ForegroundColor Green
Write-Host ""

# Step 2: Instructions
Write-Host "Step 2: Manual Testing Instructions" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Gray
Write-Host ""
Write-Host "The Console app will start. Please follow these steps:" -ForegroundColor White
Write-Host ""
Write-Host "1. Select database connection:" -ForegroundColor White
Write-Host "   - Choose option 2 (db2 - TextToSqlTest)" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Enter test query:" -ForegroundColor White
Write-Host "   Show reviews with customer and product names" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Check the output for:" -ForegroundColor White
Write-Host "   ✓ Detailed error logs (exception type, message, stack trace)" -ForegroundColor Gray
Write-Host "   ✓ User-friendly error message (not 'Unknown error occurred')" -ForegroundColor Gray
Write-Host "   ✓ Processing steps showing where error occurred" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Copy the full error output and share it" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to start the Console app..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
Write-Host ""

# Step 3: Run Console
Write-Host "Step 3: Starting Console app..." -ForegroundColor Yellow
Write-Host ""

Set-Location TextToSqlAgent.Console
dotnet run

Set-Location ..

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
