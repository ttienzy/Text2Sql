#!/usr/bin/env pwsh
# Manual test script for Console with DB2 (Northwind)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Console Manual Test - DB2 (Northwind)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Starting Console..." -ForegroundColor Yellow
Write-Host ""
Write-Host "Instructions:" -ForegroundColor Green
Write-Host "1. When prompted for database, select: 2 (Northwind)" -ForegroundColor White
Write-Host "2. Enter query: Show reviews with customer and product names" -ForegroundColor White
Write-Host "3. Verify the results" -ForegroundColor White
Write-Host "4. Type 'exit' to quit" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to start..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
Write-Host ""

# Run console
dotnet run --project TextToSqlAgent.Console
