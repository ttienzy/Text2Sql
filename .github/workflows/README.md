# CI/CD Workflows

## P1-09: CI Gates Implementation

This directory contains GitHub Actions workflows for continuous integration and deployment.

## Workflows

### 1. CI Pipeline (`ci.yml`)

**Triggers**:
- Push to `main` or `develop` branches
- Pull requests to `main` or `develop` branches
- Manual trigger via workflow_dispatch

**Jobs**:

#### Build Gate
- Restores dependencies
- Builds solution in Release configuration
- Fails if build errors exist

#### Unit Test Gate
- Runs all unit tests (42 tests)
- Generates test results (TRX format)
- Collects code coverage (OpenCover format)
- Uploads test results and coverage as artifacts
- Fails if any test fails

#### Integration Test Gate
- Starts Docker services (SQL Server, Qdrant, Redis)
- Waits for services to be healthy
- Initializes test database with seed data
- Runs integration tests (12 tests)
- Uploads test results as artifacts
- Fails if any test fails

#### E2E API Smoke Test
- Starts SQL Server service
- Builds and starts API server
- Runs smoke tests:
  - Health check endpoint
  - Authentication endpoint
  - Query endpoint (auth required)
- Fails if any smoke test fails

#### Security Scan
- Checks for vulnerable NuGet packages
- Generates vulnerability report
- Warns about critical/high severity issues
- Does not fail build (warning only)

#### Code Quality
- Builds with strict checks
- Scans for TODO/FIXME comments
- Reports code quality issues

#### Test Summary
- Downloads all test results
- Generates summary in GitHub UI
- Shows pass/fail status for all test suites

#### CI Success
- Final gate that checks all jobs
- Fails if any required job failed
- Shows "Ready to merge" if all passed

**Environment Variables**:
- `TEST_SQL_CONNECTION_STRING`: SQL Server connection
- `TEST_QDRANT_URL`: Qdrant endpoint
- `TEST_REDIS_CONNECTION_STRING`: Redis connection

**Artifacts**:
- `unit-test-results`: Unit test TRX files
- `unit-test-coverage`: Code coverage reports
- `integration-test-results`: Integration test TRX files
- `vulnerability-report`: Security scan results

### 2. Quick Check (`quick-check.yml`)

**Triggers**:
- Push to any branch except `main` and `develop`
- Manual trigger via workflow_dispatch

**Purpose**:
- Fast feedback for feature branches
- Runs build + unit tests only
- Skips integration tests and E2E tests
- Completes in ~2-3 minutes

**Jobs**:
- Build in Debug configuration
- Run unit tests
- Show summary

## Usage

### For Developers

#### Before Creating PR
```bash
# Run locally to ensure CI will pass
dotnet build
dotnet test TextToSqlAgent.Tests.Unit
./run-integration-tests.ps1
```

#### Feature Branch Development
- Push to feature branch triggers `quick-check.yml`
- Fast feedback (~2-3 minutes)
- Fix any issues before creating PR

#### Creating PR
- Create PR to `main` or `develop`
- Full CI pipeline runs automatically
- All gates must pass before merge

### For Reviewers

#### Check CI Status
- Look for green checkmark on PR
- Review test results in artifacts
- Check security scan warnings

#### Required Checks
- ✅ Build must pass
- ✅ Unit tests must pass
- ✅ Integration tests must pass
- ✅ E2E smoke tests must pass
- ⚠️ Security scan (warning only)
- ⚠️ Code quality (warning only)

## CI Gates Explained

### Build Gate
**Purpose**: Ensure code compiles without errors

**Fails if**:
- Compilation errors
- Missing dependencies
- Invalid project references

**Fix**:
```bash
dotnet build
# Fix any errors shown
```

### Unit Test Gate
**Purpose**: Ensure unit tests pass

**Fails if**:
- Any unit test fails
- Test crashes or times out

**Fix**:
```bash
dotnet test TextToSqlAgent.Tests.Unit
# Fix failing tests
```

### Integration Test Gate
**Purpose**: Ensure integration with database works

**Fails if**:
- Docker services fail to start
- Database initialization fails
- Any integration test fails

**Fix**:
```bash
./run-integration-tests.ps1
# Fix failing tests
```

### E2E Smoke Test Gate
**Purpose**: Ensure API endpoints work end-to-end

**Fails if**:
- API fails to start
- Health check fails
- Endpoints return unexpected responses

**Fix**:
```bash
# Start API locally and test
dotnet run --project TextToSqlAgent.API
curl http://localhost:5000/api/agent/health
```

## Troubleshooting

### Build Fails in CI but Works Locally
- Check .NET version matches (10.0.x)
- Ensure all dependencies are committed
- Check for platform-specific code

### Unit Tests Fail in CI but Pass Locally
- Check for timing issues
- Ensure tests don't depend on local state
- Verify mock data is consistent

### Integration Tests Fail in CI
- Check Docker service health
- Verify connection strings
- Ensure test database initializes correctly

### E2E Tests Fail in CI
- Check API startup logs
- Verify port availability
- Ensure services are healthy

## Performance

### CI Pipeline Duration
- Build: ~30 seconds
- Unit Tests: ~10 seconds
- Integration Tests: ~45 seconds
- E2E Smoke Tests: ~30 seconds
- Security Scan: ~20 seconds
- Code Quality: ~15 seconds
- **Total**: ~2.5 minutes

### Quick Check Duration
- Build: ~20 seconds
- Unit Tests: ~10 seconds
- **Total**: ~30 seconds

## Artifacts

### Test Results (TRX)
- Format: Visual Studio Test Results
- Location: `**/TestResults/*.trx`
- Retention: 90 days
- Use: View detailed test results

### Code Coverage (OpenCover)
- Format: OpenCover XML
- Location: `**/TestResults/**/coverage.opencover.xml`
- Retention: 90 days
- Use: Analyze code coverage

### Vulnerability Report
- Format: Plain text
- Location: `vulnerability-report.txt`
- Retention: 90 days
- Use: Review security issues

## Branch Protection Rules

### Recommended Settings for `main` branch:
- ✅ Require pull request before merging
- ✅ Require status checks to pass:
  - Build
  - Unit Tests
  - Integration Tests
  - E2E API Smoke Test
- ✅ Require branches to be up to date
- ✅ Require conversation resolution before merging
- ✅ Require linear history (optional)

### Recommended Settings for `develop` branch:
- ✅ Require pull request before merging
- ✅ Require status checks to pass:
  - Build
  - Unit Tests
- ⚠️ Integration Tests (optional)
- ⚠️ E2E Smoke Test (optional)

## Maintenance

### Updating .NET Version
1. Update `DOTNET_VERSION` in both workflows
2. Update `global.json` if exists
3. Test locally before committing

### Adding New Tests
1. Add tests to appropriate project
2. Ensure tests run in CI environment
3. Update this README if new test categories added

### Modifying Gates
1. Update workflow YAML
2. Test changes on feature branch
3. Document changes in this README

## Security

### Secrets Required
None currently. All tests use:
- In-memory databases
- Mock services
- Test credentials (hardcoded for CI)

### Future Secrets (if needed)
- `GEMINI_API_KEY`: For LLM tests (optional)
- `OPENAI_API_KEY`: For OpenAI tests (optional)
- `PRODUCTION_DB_CONNECTION`: For production deployments

## Status Badges

Add to README.md:

```markdown
[![CI Pipeline](https://github.com/YOUR_USERNAME/TextToSqlAgent/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_USERNAME/TextToSqlAgent/actions/workflows/ci.yml)
[![Quick Check](https://github.com/YOUR_USERNAME/TextToSqlAgent/actions/workflows/quick-check.yml/badge.svg)](https://github.com/YOUR_USERNAME/TextToSqlAgent/actions/workflows/quick-check.yml)
```

## References

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [.NET Testing in CI/CD](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- [Docker in GitHub Actions](https://docs.github.com/en/actions/using-containerized-services)
