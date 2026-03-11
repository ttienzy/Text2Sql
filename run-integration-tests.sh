#!/bin/bash
# P1-07: Integration test runner script with docker-compose (Linux/Mac)
# Starts test infrastructure, initializes database, runs tests, and cleans up

set -e

SKIP_BUILD=false
KEEP_CONTAINERS=false
FILTER=""
VERBOSE=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --keep-containers)
            KEEP_CONTAINERS=true
            shift
            ;;
        --filter)
            FILTER="$2"
            shift 2
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--skip-build] [--keep-containers] [--filter <pattern>] [--verbose]"
            exit 1
            ;;
    esac
done

echo -e "\033[36m=== P1-07 Integration Test Runner ===\033[0m"
echo ""

# Check if docker is available
if ! command -v docker &> /dev/null || ! command -v docker-compose &> /dev/null; then
    echo -e "\033[31mERROR: Docker or docker-compose not found. Please install Docker.\033[0m"
    exit 1
fi

# Function to check if containers are healthy
wait_for_healthy() {
    echo -e "\033[33mWaiting for services to be healthy...\033[0m"
    
    for i in {1..30}; do
        if docker-compose -f docker-compose.test.yml ps | grep -q "healthy"; then
            all_healthy=true
            while IFS= read -r line; do
                if echo "$line" | grep -v "healthy" | grep -q "Up"; then
                    all_healthy=false
                    break
                fi
            done < <(docker-compose -f docker-compose.test.yml ps)
            
            if [ "$all_healthy" = true ]; then
                echo -e "\033[32mAll services are healthy!\033[0m"
                return 0
            fi
        fi
        
        echo "  Attempt $i/30 - Waiting..."
        sleep 2
    done
    
    echo -e "\033[31mERROR: Services did not become healthy in time\033[0m"
    return 1
}

# Cleanup function
cleanup() {
    if [ "$KEEP_CONTAINERS" = false ]; then
        echo ""
        echo -e "\033[36mCleaning up containers...\033[0m"
        docker-compose -f docker-compose.test.yml down
        echo -e "\033[32mCleanup complete!\033[0m"
    else
        echo ""
        echo -e "\033[33mContainers kept running\033[0m"
        echo -e "\033[90mTo stop manually: docker-compose -f docker-compose.test.yml down\033[0m"
    fi
}

trap cleanup EXIT

# Step 1: Start docker-compose services
echo -e "\033[36mStep 1: Starting test infrastructure...\033[0m"
docker-compose -f docker-compose.test.yml up -d

# Step 2: Wait for services to be healthy
echo ""
echo -e "\033[36mStep 2: Waiting for services...\033[0m"
wait_for_healthy

# Step 3: Initialize test database
echo ""
echo -e "\033[36mStep 3: Initializing test database...\033[0m"
cat test-data/init-db.sql | docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!"
echo -e "\033[32mDatabase initialized successfully!\033[0m"

# Step 4: Build solution (optional)
if [ "$SKIP_BUILD" = false ]; then
    echo ""
    echo -e "\033[36mStep 4: Building solution...\033[0m"
    dotnet build TextToSqlAgent.Tests.Integration
fi

# Step 5: Run integration tests
echo ""
echo -e "\033[36mStep 5: Running integration tests...\033[0m"

TEST_ARGS="test TextToSqlAgent.Tests.Integration"

if [ -n "$FILTER" ]; then
    TEST_ARGS="$TEST_ARGS --filter $FILTER"
fi

if [ "$VERBOSE" = true ]; then
    TEST_ARGS="$TEST_ARGS --logger console;verbosity=detailed"
fi

dotnet $TEST_ARGS
TEST_EXIT_CODE=$?

# Step 6: Show results
echo ""
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo -e "\033[32m=== All tests passed! ===\033[0m"
else
    echo -e "\033[31m=== Some tests failed ===\033[0m"
fi

exit $TEST_EXIT_CODE
