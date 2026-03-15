#!/usr/bin/env pwsh

# Test script to check suggestion feature
Write-Host "Testing suggestion feature..." -ForegroundColor Green

# Build the console app
Write-Host "Building console app..." -ForegroundColor Yellow
dotnet build TextToSqlAgent.Console --configuration Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green

# Test with a Vietnamese query that should generate suggestions
$testQuery = "Hiển thị số đơn hàng theo trạng thái và phương thức thanh toán"

Write-Host "Testing query: $testQuery" -ForegroundColor Cyan
Write-Host "Looking for suggestion debug output..." -ForegroundColor Yellow

# Run the console app with the test query
# Note: This will require manual input, but we can see the debug output
Write-Host "Run this command manually to test:" -ForegroundColor Magenta
Write-Host "cd TextToSqlAgent.Console && dotnet run" -ForegroundColor White
Write-Host "Then enter: $testQuery" -ForegroundColor White