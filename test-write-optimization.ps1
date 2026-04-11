#!/usr/bin/env pwsh
# Test Write Pipeline Optimization
# This script verifies that the optimization is working correctly

param(
    [string]$BaseUrl = "https://localhost:7189",
    [string]$Token = $env:BEARER_TOKEN,
    [string]$ConnectionId = $env:CONNECTION_ID
)

Write-Host "🧪 Testing Write Pipeline Performance Optimization" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Gray
Write-Host ""

if ([string]::IsNullOrEmpty($Token)) {
    Write-Host "❌ Error: BEARER_TOKEN environment variable not set" -ForegroundColor Red
    Write-Host "   Set it with: `$env:BEARER_TOKEN = 'your-token'" -ForegroundColor Yellow
    exit 1
}

if ([string]::IsNullOrEmpty($ConnectionId)) {
    Write-Host "❌ Error: CONNECTION_ID environment variable not set" -ForegroundColor Red
    Write-Host "   Set it with: `$env:CONNECTION_ID = 'your-connection-id'" -ForegroundColor Yellow
    exit 1
}

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Base URL: $BaseUrl" -ForegroundColor Gray
Write-Host "  Connection ID: $ConnectionId" -ForegroundColor Gray
Write-Host ""

# Test cases
$tests = @(
    @{
        Name = "Simple INSERT (English)"
        Question = "Add a new customer named John Smith"
        ExpectedTime = 10
        Description = "Should use PreResolvedEntities, no fallback LLM call"
    },
    @{
        Name = "Simple INSERT (Vietnamese)"
        Question = "Thêm khách hàng tên Nguyễn Văn A"
        ExpectedTime = 10
        Description = "Should extract 'khachhang' entity correctly"
    },
    @{
        Name = "UPDATE with WHERE"
        Question = "Update customer with ID 123 set email to test@example.com"
        ExpectedTime = 12
        Description = "Should include WHERE clause validation"
    }
)

$results = @()

foreach ($test in $tests) {
    Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
    Write-Host "Test: $($test.Name)" -ForegroundColor Cyan
    Write-Host "Question: $($test.Question)" -ForegroundColor White
    Write-Host "Expected: <$($test.ExpectedTime)s" -ForegroundColor Yellow
    Write-Host ""

    $body = @{
        question = $test.Question
        connectionId = $ConnectionId
        conversationId = $null
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $Token"
        "Content-Type" = "application/json"
    }

    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        Write-Host "⏳ Sending request..." -ForegroundColor Gray
        
        # Note: This is a streaming endpoint, so we need to handle SSE
        # For now, we'll just measure the initial response time
        $response = Invoke-WebRequest `
            -Uri "$BaseUrl/api/v2/agent/process/stream" `
            -Method POST `
            -Headers $headers `
            -Body $body `
            -TimeoutSec 120 `
            -SkipCertificateCheck
        
        $stopwatch.Stop()
        $duration = $stopwatch.Elapsed.TotalSeconds

        $result = @{
            Test = $test.Name
            Duration = $duration
            Expected = $test.ExpectedTime
            Status = if ($duration -le $test.ExpectedTime) { "✅ PASS" } else { "⚠️ SLOW" }
            Improvement = if ($duration -le $test.ExpectedTime) { 
                [math]::Round((1 - $duration / $test.ExpectedTime) * 100, 1) 
            } else { 
                0 
            }
        }

        $results += $result

        if ($duration -le $test.ExpectedTime) {
            Write-Host "✅ PASS: ${duration}s (target: <$($test.ExpectedTime)s)" -ForegroundColor Green
        } else {
            Write-Host "⚠️ SLOW: ${duration}s (target: <$($test.ExpectedTime)s)" -ForegroundColor Yellow
        }

    } catch {
        Write-Host "❌ FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $results += @{
            Test = $test.Name
            Duration = -1
            Expected = $test.ExpectedTime
            Status = "❌ FAIL"
            Improvement = 0
        }
    }

    Write-Host ""
    Start-Sleep -Seconds 2
}

# Summary
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Gray
Write-Host "📊 Test Summary" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Gray
Write-Host ""

$passed = ($results | Where-Object { $_.Status -eq "✅ PASS" }).Count
$total = $results.Count

foreach ($result in $results) {
    $color = if ($result.Status -eq "✅ PASS") { "Green" } 
             elseif ($result.Status -eq "⚠️ SLOW") { "Yellow" } 
             else { "Red" }
    
    Write-Host "$($result.Status) $($result.Test)" -ForegroundColor $color
    if ($result.Duration -gt 0) {
        Write-Host "   Duration: $($result.Duration)s / Expected: <$($result.Expected)s" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Results: $passed/$total tests passed" -ForegroundColor $(if ($passed -eq $total) { "Green" } else { "Yellow" })
Write-Host ""

if ($passed -eq $total) {
    Write-Host "🎉 All tests passed! Optimization is working correctly." -ForegroundColor Green
} else {
    Write-Host "⚠️ Some tests failed or were slow. Check logs for details." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "💡 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Check logs for '[WritePipeline] Using pre-resolved entities'" -ForegroundColor Gray
Write-Host "  2. Verify no '[WritePipeline] ⚠️ Using fallback' warnings" -ForegroundColor Gray
Write-Host "  3. Monitor [PERF-SUMMARY] Duration in production" -ForegroundColor Gray
Write-Host ""
