# Test SSE Streaming - Real Progress Reporting
# This script tests the real-time SSE streaming endpoint

param(
    [string]$ApiUrl = "https://localhost:7189",
    [string]$Token = "",
    [string]$ConnectionId = "",
    [string]$Question = "Show me all users"
)

Write-Host "🧪 Testing SSE Streaming with Real Progress" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrEmpty($Token)) {
    Write-Host "❌ Error: Token is required" -ForegroundColor Red
    Write-Host "Usage: .\test-sse-streaming.ps1 -Token 'your-token' -ConnectionId 'your-connection-id'" -ForegroundColor Yellow
    exit 1
}

if ([string]::IsNullOrEmpty($ConnectionId)) {
    Write-Host "❌ Error: ConnectionId is required" -ForegroundColor Red
    Write-Host "Usage: .\test-sse-streaming.ps1 -Token 'your-token' -ConnectionId 'your-connection-id'" -ForegroundColor Yellow
    exit 1
}

$url = "$ApiUrl/api/v2/agent/process/stream"
$body = @{
    question = $Question
    connectionId = $ConnectionId
} | ConvertTo-Json

Write-Host "📡 Endpoint: $url" -ForegroundColor Gray
Write-Host "❓ Question: $Question" -ForegroundColor Gray
Write-Host ""
Write-Host "⏱️  Timeline:" -ForegroundColor Yellow
Write-Host ""

$startTime = Get-Date

try {
    # Create HTTP request
    $request = [System.Net.HttpWebRequest]::Create($url)
    $request.Method = "POST"
    $request.ContentType = "application/json"
    $request.Headers.Add("Authorization", "Bearer $Token")
    $request.AllowReadStreamBuffering = $false
    
    # Write request body
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $request.ContentLength = $bytes.Length
    $requestStream = $request.GetRequestStream()
    $requestStream.Write($bytes, 0, $bytes.Length)
    $requestStream.Close()

    # Get response stream
    $response = $request.GetResponse()
    $stream = $response.GetResponseStream()
    $reader = New-Object System.IO.StreamReader($stream)

    $eventCount = 0
    $lastProgress = 0

    # Read SSE events
    while (-not $reader.EndOfStream) {
        $line = $reader.ReadLine()
        
        if ($line.StartsWith("event: ")) {
            $eventType = $line.Substring(7).Trim()
        }
        elseif ($line.StartsWith("data: ")) {
            $data = $line.Substring(6) | ConvertFrom-Json
            $elapsed = ((Get-Date) - $startTime).TotalSeconds
            $eventCount++

            if ($eventType -eq "stage_update") {
                $progressPercent = [math]::Round($data.progress * 100)
                $progressBar = "█" * [math]::Floor($progressPercent / 5)
                $progressBar = $progressBar.PadRight(20, "░")
                
                $elapsedStr = "{0:F1}s" -f $elapsed
                $progressDelta = $progressPercent - $lastProgress
                $deltaStr = if ($progressDelta -gt 0) { "+$progressDelta%" } else { "" }
                
                Write-Host ("[{0,5}] {1}% {2} {3,-20} {4} {5}" -f $elapsedStr, $progressPercent, $progressBar, $data.stage, $data.message, $deltaStr) -ForegroundColor Green
                
                $lastProgress = $progressPercent
            }
            elseif ($eventType -eq "result") {
                Write-Host ""
                Write-Host "✅ RESULT RECEIVED" -ForegroundColor Green
                Write-Host "   Success: $($data.success)" -ForegroundColor Gray
                Write-Host "   SQL: $($data.sql)" -ForegroundColor Gray
                Write-Host "   Rows: $($data.data.Count)" -ForegroundColor Gray
            }
            elseif ($eventType -eq "error") {
                Write-Host ""
                Write-Host "❌ ERROR: $($data.message)" -ForegroundColor Red
            }
        }
    }

    $reader.Close()
    $stream.Close()
    $response.Close()

    $totalTime = ((Get-Date) - $startTime).TotalSeconds
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "📊 Summary:" -ForegroundColor Cyan
    Write-Host "   Total Events: $eventCount" -ForegroundColor Gray
    Write-Host "   Total Time: $($totalTime.ToString('F2'))s" -ForegroundColor Gray
    Write-Host "   Avg Time/Event: $(($totalTime / $eventCount).ToString('F2'))s" -ForegroundColor Gray
    Write-Host ""
    Write-Host "✅ Test completed successfully!" -ForegroundColor Green

} catch {
    Write-Host ""
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
