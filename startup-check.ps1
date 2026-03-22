# ✈️ Phase 1 Startup Check Script
# Automatically verifies all components before testing

Write-Host "🚀 Phase 1 Implementation - Startup Check" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$ErrorCount = 0
$WarningCount = 0

# Function to check file exists
function Test-FileExists {
    param($Path, $Description)
    if (Test-Path $Path) {
        Write-Host "✅ $Description" -ForegroundColor Green
        return $true
    } else {
        Write-Host "❌ $Description - NOT FOUND" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    }
}

# Function to check string in file
function Test-StringInFile {
    param($Path, $SearchString, $Description)
    if (Test-Path $Path) {
        $content = Get-Content $Path -Raw
        if ($content -match [regex]::Escape($SearchString)) {
            Write-Host "✅ $Description" -ForegroundColor Green
            return $true
        } else {
            Write-Host "❌ $Description - NOT FOUND" -ForegroundColor Red
            $script:ErrorCount++
            return $false
        }
    } else {
        Write-Host "❌ File not found: $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    }
}

Write-Host "📁 Checking Backend Files..." -ForegroundColor Yellow
Write-Host ""

# Core Models
Test-FileExists "TextToSqlAgent.Core/Models/IntentClassification.cs" "IntentClassification.cs"
Test-FileExists "TextToSqlAgent.Core/Models/ForbiddenOperationResult.cs" "ForbiddenOperationResult.cs"
Test-FileExists "TextToSqlAgent.Core/Models/WriteOperationModels.cs" "WriteOperationModels.cs"
Test-FileExists "TextToSqlAgent.Core/Models/DDLOperationModels.cs" "DDLOperationModels.cs"

# Interfaces
Test-FileExists "TextToSqlAgent.Core/Interfaces/IIntentClassifier.cs" "IIntentClassifier.cs"
Test-FileExists "TextToSqlAgent.Core/Interfaces/IForbiddenPipeline.cs" "IForbiddenPipeline.cs"
Test-FileExists "TextToSqlAgent.Core/Interfaces/IWritePipeline.cs" "IWritePipeline.cs"
Test-FileExists "TextToSqlAgent.Core/Interfaces/IDDLPipeline.cs" "IDDLPipeline.cs"

# Routing
Test-FileExists "TextToSqlAgent.Application/Routing/IntentClassifier.cs" "IntentClassifier.cs"

# Pipelines
Test-FileExists "TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs" "ForbiddenPipeline.cs"
Test-FileExists "TextToSqlAgent.Application/Pipelines/Write/WritePipeline.cs" "WritePipeline.cs"
Test-FileExists "TextToSqlAgent.Application/Pipelines/DDL/DDLPipeline.cs" "DDLPipeline.cs"

# DI & Integration
Test-FileExists "TextToSqlAgent.Application/DependencyInjection/IntentPipelineServiceExtensions.cs" "IntentPipelineServiceExtensions.cs"

# API Controllers
Test-FileExists "TextToSqlAgent.API/Controllers/WriteOperationController.cs" "WriteOperationController.cs"
Test-FileExists "TextToSqlAgent.API/Controllers/DDLOperationController.cs" "DDLOperationController.cs"

Write-Host ""
Write-Host "🔧 Checking DI Registration..." -ForegroundColor Yellow
Write-Host ""

# Check Program.cs for DI registration
Test-StringInFile "TextToSqlAgent.API/Program.cs" "AddIntentBasedPipelines" "DI Registration in Program.cs"
Test-StringInFile "TextToSqlAgent.API/Program.cs" "using TextToSqlAgent.Application.DependencyInjection" "DI Using Statement"

Write-Host ""
Write-Host "🎨 Checking Frontend Files..." -ForegroundColor Yellow
Write-Host ""

# Frontend Components
Test-FileExists "frontend/src/components/write/WriteConfirmationModal.jsx" "WriteConfirmationModal.jsx"
Test-FileExists "frontend/src/components/write/index.js" "write/index.js"
Test-FileExists "frontend/src/components/ddl/DDLImpactCard.jsx" "DDLImpactCard.jsx"
Test-FileExists "frontend/src/components/ddl/index.js" "ddl/index.js"
Test-FileExists "frontend/src/components/forbidden/ForbiddenAlert.jsx" "ForbiddenAlert.jsx"
Test-FileExists "frontend/src/components/forbidden/index.js" "forbidden/index.js"

# API Integration
Test-FileExists "frontend/src/api/write/index.js" "write API client"
Test-FileExists "frontend/src/api/ddl/index.js" "ddl API client"

# Hooks
Test-FileExists "frontend/src/hooks/useIntentBasedChat.js" "useIntentBasedChat hook"

# Examples
Test-FileExists "frontend/src/examples/IntentBasedChatExample.jsx" "Integration Example"

Write-Host ""
Write-Host "📚 Checking Documentation..." -ForegroundColor Yellow
Write-Host ""

Test-FileExists "IMPLEMENTATION-COMPLETE.md" "Implementation Complete Summary"
Test-FileExists "QUICK-START-TESTING-GUIDE.md" "Quick Start Testing Guide"
Test-FileExists "PHASE-1-COMPLETE-SUMMARY.md" "Phase 1 Complete Summary"
Test-FileExists "PHASE-1-README.md" "Phase 1 README"
Test-FileExists "PRE-FLIGHT-CHECKLIST.md" "Pre-Flight Checklist"

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan

if ($ErrorCount -eq 0) {
    Write-Host ""
    Write-Host "✅ ALL CHECKS PASSED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "🎉 Phase 1 implementation is complete and ready for testing!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Build backend: cd TextToSqlAgent.API && dotnet build" -ForegroundColor White
    Write-Host "2. Run backend: dotnet run" -ForegroundColor White
    Write-Host "3. Follow QUICK-START-TESTING-GUIDE.md for testing" -ForegroundColor White
    Write-Host ""
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ CHECKS FAILED: $ErrorCount error(s) found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please fix the errors above before proceeding." -ForegroundColor Yellow
    Write-Host "See PRE-FLIGHT-CHECKLIST.md for troubleshooting." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}
