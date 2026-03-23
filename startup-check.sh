#!/bin/bash

# ✈️ Phase 1 Startup Check Script
# Automatically verifies all components before testing

echo "🚀 Phase 1 Implementation - Startup Check"
echo "========================================="
echo ""

ERROR_COUNT=0
WARNING_COUNT=0

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to check file exists
check_file() {
    if [ -f "$1" ]; then
        echo -e "${GREEN}✅ $2${NC}"
        return 0
    else
        echo -e "${RED}❌ $2 - NOT FOUND${NC}"
        ((ERROR_COUNT++))
        return 1
    fi
}

# Function to check string in file
check_string_in_file() {
    if [ -f "$1" ]; then
        if grep -q "$2" "$1"; then
            echo -e "${GREEN}✅ $3${NC}"
            return 0
        else
            echo -e "${RED}❌ $3 - NOT FOUND${NC}"
            ((ERROR_COUNT++))
            return 1
        fi
    else
        echo -e "${RED}❌ File not found: $1${NC}"
        ((ERROR_COUNT++))
        return 1
    fi
}

echo -e "${YELLOW}📁 Checking Backend Files...${NC}"
echo ""

# Core Models
check_file "TextToSqlAgent.Core/Models/IntentClassification.cs" "IntentClassification.cs"
check_file "TextToSqlAgent.Core/Models/ForbiddenOperationResult.cs" "ForbiddenOperationResult.cs"
check_file "TextToSqlAgent.Core/Models/WriteOperationModels.cs" "WriteOperationModels.cs"
check_file "TextToSqlAgent.Core/Models/DDLOperationModels.cs" "DDLOperationModels.cs"

# Interfaces
check_file "TextToSqlAgent.Core/Interfaces/IIntentClassifier.cs" "IIntentClassifier.cs"
check_file "TextToSqlAgent.Core/Interfaces/IForbiddenPipeline.cs" "IForbiddenPipeline.cs"
check_file "TextToSqlAgent.Core/Interfaces/IWritePipeline.cs" "IWritePipeline.cs"
check_file "TextToSqlAgent.Core/Interfaces/IDDLPipeline.cs" "IDDLPipeline.cs"

# Routing
check_file "TextToSqlAgent.Application/Routing/IntentClassifier.cs" "IntentClassifier.cs"

# Pipelines
check_file "TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs" "ForbiddenPipeline.cs"
check_file "TextToSqlAgent.Application/Pipelines/Write/WritePipeline.cs" "WritePipeline.cs"
check_file "TextToSqlAgent.Application/Pipelines/DDL/DDLPipeline.cs" "DDLPipeline.cs"

# DI & Integration
check_file "TextToSqlAgent.Application/DependencyInjection/IntentPipelineServiceExtensions.cs" "IntentPipelineServiceExtensions.cs"

# API Controllers
check_file "TextToSqlAgent.API/Controllers/WriteOperationController.cs" "WriteOperationController.cs"
check_file "TextToSqlAgent.API/Controllers/DDLOperationController.cs" "DDLOperationController.cs"

echo ""
echo -e "${YELLOW}🔧 Checking DI Registration...${NC}"
echo ""

# Check Program.cs for DI registration
check_string_in_file "TextToSqlAgent.API/Program.cs" "AddIntentBasedPipelines" "DI Registration in Program.cs"
check_string_in_file "TextToSqlAgent.API/Program.cs" "using TextToSqlAgent.Application.DependencyInjection" "DI Using Statement"

echo ""
echo -e "${YELLOW}🎨 Checking Frontend Files...${NC}"
echo ""

# Frontend Components
check_file "frontend/src/components/write/WriteConfirmationModal.jsx" "WriteConfirmationModal.jsx"
check_file "frontend/src/components/write/index.js" "write/index.js"
check_file "frontend/src/components/ddl/DDLImpactCard.jsx" "DDLImpactCard.jsx"
check_file "frontend/src/components/ddl/index.js" "ddl/index.js"
check_file "frontend/src/components/forbidden/ForbiddenAlert.jsx" "ForbiddenAlert.jsx"
check_file "frontend/src/components/forbidden/index.js" "forbidden/index.js"

# API Integration
check_file "frontend/src/api/write/index.js" "write API client"
check_file "frontend/src/api/ddl/index.js" "ddl API client"

# Hooks
check_file "frontend/src/hooks/useIntentBasedChat.js" "useIntentBasedChat hook"

# Examples
check_file "frontend/src/examples/IntentBasedChatExample.jsx" "Integration Example"

echo ""
echo -e "${YELLOW}📚 Checking Documentation...${NC}"
echo ""

check_file "IMPLEMENTATION-COMPLETE.md" "Implementation Complete Summary"
check_file "QUICK-START-TESTING-GUIDE.md" "Quick Start Testing Guide"
check_file "PHASE-1-COMPLETE-SUMMARY.md" "Phase 1 Complete Summary"
check_file "PHASE-1-README.md" "Phase 1 README"
check_file "PRE-FLIGHT-CHECKLIST.md" "Pre-Flight Checklist"

echo ""
echo "========================================="

if [ $ERROR_COUNT -eq 0 ]; then
    echo ""
    echo -e "${GREEN}✅ ALL CHECKS PASSED!${NC}"
    echo ""
    echo -e "${GREEN}🎉 Phase 1 implementation is complete and ready for testing!${NC}"
    echo ""
    echo -e "${YELLOW}Next steps:${NC}"
    echo "1. Build backend: cd TextToSqlAgent.API && dotnet build"
    echo "2. Run backend: dotnet run"
    echo "3. Follow QUICK-START-TESTING-GUIDE.md for testing"
    echo ""
    exit 0
else
    echo ""
    echo -e "${RED}❌ CHECKS FAILED: $ERROR_COUNT error(s) found${NC}"
    echo ""
    echo -e "${YELLOW}Please fix the errors above before proceeding.${NC}"
    echo -e "${YELLOW}See PRE-FLIGHT-CHECKLIST.md for troubleshooting.${NC}"
    echo ""
    exit 1
fi
