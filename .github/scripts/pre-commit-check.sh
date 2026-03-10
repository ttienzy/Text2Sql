#!/bin/bash
# P1-09: Pre-commit check script (Linux/Mac)
# Run this before committing to ensure CI will pass

set -e

SKIP_TESTS=false
SKIP_BUILD=false
QUICK=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --quick)
            QUICK=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--skip-tests] [--skip-build] [--quick]"
            exit 1
            ;;
    esac
done

echo -e "\033[36m=== Pre-Commit Check ===\033[0m"
echo ""

failed=false

# Step 1: Build
if [ "$SKIP_BUILD" = false ]; then
    echo -e "\033[36mStep 1: Building solution...\033[0m"
    if dotnet build --configuration Release; then
        echo -e "\033[32m✅ Build passed\033[0m"
    else
        echo -e "\033[31m❌ Build failed!\033[0m"
        failed=true
    fi
    echo ""
fi

# Step 2: Unit Tests
if [ "$SKIP_TESTS" = false ]; then
    echo -e "\033[36mStep 2: Running unit tests...\033[0m"
    if dotnet test TextToSqlAgent.Tests.Unit --configuration Release --no-build --logger "console;verbosity=minimal"; then
        echo -e "\033[32m✅ Unit tests passed\033[0m"
    else
        echo -e "\033[31m❌ Unit tests failed!\033[0m"
        failed=true
    fi
    echo ""
fi

# Step 3: Integration Tests (skip in quick mode)
if [ "$SKIP_TESTS" = false ] && [ "$QUICK" = false ]; then
    echo -e "\033[36mStep 3: Running integration tests...\033[0m"
    echo -e "\033[33m(This requires Docker services to be running)\033[0m"
    
    read -p "Run integration tests? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        if ./run-integration-tests.sh --skip-build; then
            echo -e "\033[32m✅ Integration tests passed\033[0m"
        else
            echo -e "\033[31m❌ Integration tests failed!\033[0m"
            failed=true
        fi
    else
        echo -e "\033[33m⚠️ Integration tests skipped\033[0m"
    fi
    echo ""
fi

# Step 4: Check for vulnerable packages
echo -e "\033[36mStep 4: Checking for vulnerable packages...\033[0m"
vulnerabilities=$(dotnet list package --vulnerable --include-transitive 2>&1)
if echo "$vulnerabilities" | grep -qi "critical\|high"; then
    echo -e "\033[33m⚠️ Warning: Vulnerable packages found!\033[0m"
    echo "$vulnerabilities"
else
    echo -e "\033[32m✅ No critical vulnerabilities\033[0m"
fi
echo ""

# Step 5: Check for TODO/FIXME
echo -e "\033[36mStep 5: Checking for TODO/FIXME comments...\033[0m"
todo_count=$(grep -r "TODO\|FIXME" --include="*.cs" --exclude-dir={bin,obj} . | wc -l || true)
if [ $todo_count -gt 0 ]; then
    echo -e "\033[33m⚠️ Found $todo_count TODO/FIXME comments\033[0m"
else
    echo -e "\033[32m✅ No TODO/FIXME comments\033[0m"
fi
echo ""

# Summary
echo -e "\033[36m=== Summary ===\033[0m"
if [ "$failed" = true ]; then
    echo -e "\033[31m❌ Pre-commit check failed!\033[0m"
    echo -e "\033[31mPlease fix the issues before committing.\033[0m"
    exit 1
else
    echo -e "\033[32m✅ All checks passed!\033[0m"
    echo -e "\033[32mReady to commit!\033[0m"
    exit 0
fi
