#!/usr/bin/env pwsh
# P1-09: Pre-commit check script
# Run this before committing to ensure CI will pass

param(
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [switch]$Quick
)

$ErrorActionPreference = "Stop"

Write-Host "=== Pre-Commit Check ===" -ForegroundColor Cyan
Write-Host ""

$failed = $false

# Step 1: Build
if (-not $SkipBuild) {
    Write-Host "Step 1: Building solution..." -ForegroundColor Cyan
    dotnet build --configuration Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed!" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "✅ Build passed" -ForegroundColor Green
    }
    Write-Host ""
}

# Step 2: Unit Tests
if (-not $SkipTests) {
    Write-Host "Step 2: Running unit tests..." -ForegroundColor Cyan
    dotnet test TextToSqlAgent.Tests.Unit --configuration Release --no-build --logger "console;verbosity=minimal"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Unit tests failed!" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "✅ Unit tests passed" -ForegroundColor Green
    }
    Write-Host ""
}

# Step 3: Integration Tests (skip in quick mode)
if (-not $SkipTests -and -not $Quick) {
    Write-Host "Step 3: Running integration tests..." -ForegroundColor Cyan
    Write-Host "(This requires Docker services to be running)" -ForegroundColor Yellow
    
    $runIntegration = Read-Host "Run integration tests? (y/n)"
    if ($runIntegration -eq "y") {
        ./run-integration-tests.ps1 -SkipBuild
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Integration tests failed!" -ForegroundColor Red
            $failed = $true
        } else {
            Write-Host "✅ Integration tests passed" -ForegroundColor Green
        }
    } else {
        Write-Host "⚠️ Integration tests skipped" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Step 4: Check for vulnerable packages
Write-Host "Step 4: Checking for vulnerable packages..." -ForegroundColor Cyan
$vulnerabilities = dotnet list package --vulnerable --include-transitive 2>&1
if ($vulnerabilities -match "critical|high") {
    Write-Host "⚠️ Warning: Vulnerable packages found!" -ForegroundColor Yellow
    Write-Host $vulnerabilities
} else {
    Write-Host "✅ No critical vulnerabilities" -ForegroundColor Green
}
Write-Host ""

# Step 5: Check for TODO/FIXME
Write-Host "Step 5: Checking for TODO/FIXME comments..." -ForegroundColor Cyan
$todos = Get-ChildItem -Recurse -Include *.cs -Exclude bin,obj | Select-String "TODO|FIXME" | Measure-Object
if ($todos.Count -gt 0) {
    Write-Host "⚠️ Found $($todos.Count) TODO/FIXME comments" -ForegroundColor Yellow
} else {
    Write-Host "✅ No TODO/FIXME comments" -ForegroundColor Green
}
Write-Host ""

# Summary
Write-Host "=== Summary ===" -ForegroundColor Cyan
if ($failed) {
    Write-Host "❌ Pre-commit check failed!" -ForegroundColor Red
    Write-Host "Please fix the issues before committing." -ForegroundColor Red
    exit 1
} else {
    Write-Host "✅ All checks passed!" -ForegroundColor Green
    Write-Host "Ready to commit!" -ForegroundColor Green
    exit 0
}
