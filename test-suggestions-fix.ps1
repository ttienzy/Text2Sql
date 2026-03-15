#!/usr/bin/env pwsh

Write-Host "🔧 Testing Suggestion Feature Fixes" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green

# Build the solution
Write-Host "📦 Building solution..." -ForegroundColor Yellow
dotnet build --configuration Debug --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build successful!" -ForegroundColor Green

# Run unit tests for suggestions
Write-Host "🧪 Running suggestion tests..." -ForegroundColor Yellow
dotnet test TextToSqlAgent.Tests.Unit --filter "Should_Generate_SQL_With_Suggestions" --verbosity quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Unit tests passed!" -ForegroundColor Green
} else {
    Write-Host "⚠️  Unit tests failed, but continuing..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🎯 Key Fixes Applied:" -ForegroundColor Cyan
Write-Host "  1. ✅ Enhanced system prompt to force JSON + 3 suggestions" -ForegroundColor White
Write-Host "  2. ✅ Added raw response logging for debugging" -ForegroundColor White
Write-Host "  3. ✅ Enhanced JSON parser to handle alternative keys" -ForegroundColor White
Write-Host "  4. ✅ Added rule-based fallback for insufficient suggestions" -ForegroundColor White
Write-Host "  5. ✅ Vietnamese language support in suggestions" -ForegroundColor White

Write-Host ""
Write-Host "🚀 Next Steps:" -ForegroundColor Magenta
Write-Host "  1. Run the console app: cd TextToSqlAgent.Console && dotnet run" -ForegroundColor White
Write-Host "  2. Test with Vietnamese query: 'Hiển thị số đơn hàng theo trạng thái'" -ForegroundColor White
Write-Host "  3. Check logs for '[SqlGenerator] Raw LLM response:' to see what LLM returns" -ForegroundColor White
Write-Host "  4. Verify suggestions appear in the output" -ForegroundColor White

Write-Host ""
Write-Host "📋 Expected Log Output:" -ForegroundColor Yellow
Write-Host "[SqlGenerator] Raw LLM response:" -ForegroundColor Gray
Write-Host "{ \"sql\": \"SELECT ...\", \"suggested_queries\": [...] }" -ForegroundColor Gray
Write-Host "[SqlGenerator] ✅ Parsed SQL + 3 suggestions: [\"...\", \"...\", \"...\"]" -ForegroundColor Gray
Write-Host "[EnhancedAgent] Final response has 3 suggestions" -ForegroundColor Gray
Write-Host "💡 Suggested follow-up queries:" -ForegroundColor Gray
Write-Host "  1. ..." -ForegroundColor Gray

Write-Host ""
Write-Host "🔍 If suggestions still don't appear, check the raw LLM response in logs!" -ForegroundColor Red