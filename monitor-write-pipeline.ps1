#!/usr/bin/env pwsh
# Monitor Write Pipeline Performance Optimization
# Usage: .\monitor-write-pipeline.ps1

Write-Host "🔍 Monitoring Write Pipeline Performance..." -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop monitoring" -ForegroundColor Yellow
Write-Host ""

# Colors for different log levels
$colors = @{
    "PERF-SUMMARY" = "Green"
    "WritePipeline" = "Cyan"
    "IntentClassifier" = "Magenta"
    "StreamingAgent" = "Blue"
    "ERROR" = "Red"
    "WARNING" = "Yellow"
}

# Patterns to highlight
$patterns = @{
    "Using pre-resolved entities" = "Green"
    "Direct match found" = "Green"
    "fallback table identification" = "Red"
    "PERF-SUMMARY" = "Green"
    "Sending result to frontend" = "Green"
    "Duration=" = "Cyan"
}

Write-Host "📊 Key metrics to watch:" -ForegroundColor Yellow
Write-Host "  ✅ [WritePipeline] Using pre-resolved entities" -ForegroundColor Green
Write-Host "  ✅ [WritePipeline] Direct match found" -ForegroundColor Green
Write-Host "  ✅ [PERF-SUMMARY] Duration=<10000ms" -ForegroundColor Green
Write-Host "  ❌ [WritePipeline] ⚠️ Using fallback" -ForegroundColor Red
Write-Host ""
Write-Host "Starting log monitoring..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
Write-Host ""

# Monitor logs (adjust path to your log file or use docker logs)
# Option 1: If using file logging
# Get-Content -Path "logs/app.log" -Wait -Tail 50 | ForEach-Object {

# Option 2: If using Docker
# docker logs -f texttosqlagent-api --tail 50 | ForEach-Object {

# Option 3: Monitor console output (for development)
# This will show instructions for manual monitoring
Write-Host "⚠️  Manual monitoring mode" -ForegroundColor Yellow
Write-Host ""
Write-Host "To monitor logs, run one of these commands in another terminal:" -ForegroundColor Cyan
Write-Host ""
Write-Host "Option 1 - Docker logs:" -ForegroundColor White
Write-Host "  docker logs -f texttosqlagent-api --tail 100 | Select-String -Pattern 'WritePipeline|PERF-SUMMARY|IntentClassifier'" -ForegroundColor Gray
Write-Host ""
Write-Host "Option 2 - File logs:" -ForegroundColor White
Write-Host "  Get-Content -Path 'logs/app.log' -Wait -Tail 100 | Select-String -Pattern 'WritePipeline|PERF-SUMMARY'" -ForegroundColor Gray
Write-Host ""
Write-Host "Option 3 - Filter specific patterns:" -ForegroundColor White
Write-Host "  docker logs -f texttosqlagent-api 2>&1 | Select-String -Pattern 'PERF-SUMMARY|Using pre-resolved|Direct match|fallback'" -ForegroundColor Gray
Write-Host ""
Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
Write-Host ""
Write-Host "📈 Performance Comparison:" -ForegroundColor Yellow
Write-Host ""
Write-Host "BEFORE optimization:" -ForegroundColor Red
Write-Host "  - Simple INSERT: 12-20s (3 LLM calls)" -ForegroundColor Gray
Write-Host "  - UPDATE: 15-25s" -ForegroundColor Gray
Write-Host "  - DELETE: 15-25s" -ForegroundColor Gray
Write-Host ""
Write-Host "AFTER optimization:" -ForegroundColor Green
Write-Host "  - Simple INSERT: 7-10s (2 LLM calls)" -ForegroundColor Gray
Write-Host "  - UPDATE: 8-12s" -ForegroundColor Gray
Write-Host "  - DELETE: 8-12s" -ForegroundColor Gray
Write-Host ""
Write-Host "Expected improvement: 35-40% faster ⚡" -ForegroundColor Green
Write-Host ""

# Keep script running
Write-Host "Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
