#!/usr/bin/env pwsh
# P1-07: Integration test runner script with docker-compose
# Starts test infrastructure, initializes database, runs tests, and cleans up

param(
    [switch]$SkipBuild,
    [switch]$KeepContainers,
    [string]$Filter = "",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

Write-Host "=== P1-07 Integration Test Runner ===" -ForegroundColor Cyan
Write-Host ""

# Check if docker is available
try {
    docker --version | Out-Null
    docker-compose --version | Out-Null
} catch {
    Write-Host "ERROR: Docker or docker-compose not found. Please install Docker Desktop." -ForegroundColor Red
    exit 1
}

# Function to check if containers are healthy
function Wait-ForHealthy {
    param([int]$MaxAttempts = 30)
    
    Write-Host "Waiting for services to be healthy..." -ForegroundColor Yellow
    
    for ($i = 1; $i -le $MaxAttempts; $i++) {
        $status = docker-compose -f docker-compose.test.yml ps --format json | ConvertFrom-Json
        $allHealthy = $true
        
        foreach ($service in $status) {
            if ($service.Health -ne "healthy") {
                $allHealthy = $false
                break
            }
        }
        
        if ($allHealthy) {
            Write-Host "All services are healthy!" -ForegroundColor Green
            return $true
        }
        
        Write-Host "  Attempt $i/$MaxAttempts - Waiting..." -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
    
    Write-Host "ERROR: Services did not become healthy in time" -ForegroundColor Red
    return $false
}

try {
    # Step 1: Start docker-compose services
    Write-Host "Step 1: Starting test infrastructure..." -ForegroundColor Cyan
    docker-compose -f docker-compose.test.yml up -d
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start docker-compose services"
    }
    
    # Step 2: Wait for services to be healthy
    Write-Host ""
    Write-Host "Step 2: Waiting for services..." -ForegroundColor Cyan
    
    if (-not (Wait-ForHealthy)) {
        throw "Services failed to become healthy"
    }
    
    # Step 3: Initialize test database
    Write-Host ""
    Write-Host "Step 3: Initializing test database..." -ForegroundColor Cyan
    
    Get-Content test-data/init-db.sql | docker exec -i texttosql-test-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Test@Pass123!"
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize test database"
    }
    
    Write-Host "Database initialized successfully!" -ForegroundColor Green
    
    # Step 4: Build solution (optional)
    if (-not $SkipBuild) {
        Write-Host ""
        Write-Host "Step 4: Building solution..." -ForegroundColor Cyan
        dotnet build TextToSqlAgent.Tests.Integration
        
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
    }
    
    # Step 5: Run integration tests
    Write-Host ""
    Write-Host "Step 5: Running integration tests..." -ForegroundColor Cyan
    
    $testArgs = @(
        "test",
        "TextToSqlAgent.Tests.Integration"
    )
    
    if ($Filter) {
        $testArgs += "--filter"
        $testArgs += $Filter
    }
    
    if ($Verbose) {
        $testArgs += "--logger"
        $testArgs += "console;verbosity=detailed"
    }
    
    & dotnet @testArgs
    
    $testExitCode = $LASTEXITCODE
    
    # Step 6: Show results
    Write-Host ""
    if ($testExitCode -eq 0) {
        Write-Host "=== All tests passed! ===" -ForegroundColor Green
    } else {
        Write-Host "=== Some tests failed ===" -ForegroundColor Red
    }
    
    exit $testExitCode
    
} catch {
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
    
} finally {
    # Cleanup
    if (-not $KeepContainers) {
        Write-Host ""
        Write-Host "Cleaning up containers..." -ForegroundColor Cyan
        docker-compose -f docker-compose.test.yml down
        Write-Host "Cleanup complete!" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "Containers kept running (use -KeepContainers:$false to stop)" -ForegroundColor Yellow
        Write-Host "To stop manually: docker-compose -f docker-compose.test.yml down" -ForegroundColor Gray
    }
}
