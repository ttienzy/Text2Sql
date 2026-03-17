# Start Development Environment
# Khởi động cả API và Frontend để test conversation-aware features

Write-Host "🚀 Starting TextToSQL Agent Development Environment..." -ForegroundColor Green
Write-Host ""

# Function to start API in background
function Start-API {
    Write-Host "📡 Starting API Server..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd TextToSqlAgent.API; dotnet run" -WindowStyle Normal
    Write-Host "✅ API Server starting at http://localhost:5251" -ForegroundColor Green
}

# Function to start Frontend in background  
function Start-Frontend {
    Write-Host "🎨 Starting Frontend..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd frontend; npm run dev" -WindowStyle Normal
    Write-Host "✅ Frontend starting at http://localhost:5173" -ForegroundColor Green
}

# Check if API directory exists
if (Test-Path "TextToSqlAgent.API") {
    Start-API
    Start-Sleep -Seconds 2
} else {
    Write-Host "❌ API directory not found!" -ForegroundColor Red
    exit 1
}

# Check if Frontend directory exists
if (Test-Path "frontend") {
    Start-Frontend
    Start-Sleep -Seconds 2
} else {
    Write-Host "❌ Frontend directory not found!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🎉 Development environment started!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Available Services:" -ForegroundColor Cyan
Write-Host "   • API Server: http://localhost:5251" -ForegroundColor White
Write-Host "   • Frontend:   http://localhost:5173" -ForegroundColor White
Write-Host "   • Health:     http://localhost:5251/health" -ForegroundColor White
Write-Host ""
Write-Host "🧪 Test Conversation Features:" -ForegroundColor Cyan
Write-Host "   1. Login to the frontend" -ForegroundColor White
Write-Host "   2. Select a database connection" -ForegroundColor White
Write-Host "   3. Start a new conversation" -ForegroundColor White
Write-Host "   4. Ask: 'Show me all users'" -ForegroundColor White
Write-Host "   5. Follow-up: 'How many users are there?'" -ForegroundColor White
Write-Host "   6. Check conversation analytics in right panel" -ForegroundColor White
Write-Host ""
Write-Host "📖 See frontend/test-conversation-features.md for detailed testing guide" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")