# Retry Mechanism Analysis & Refactoring Recommendations

**Date**: 2026-04-08  
**Status**: ANALYSIS COMPLETE  
**Priority**: MEDIUM - Cần consolidate và standardize

---

## 🔍 Current State - Retry Mechanisms Found

### Backend (C#)

#### 1. **BaseErrorHandler** - Generic Retry Framework
**Location**: `TextToSqlAgent.Infrastructure/ErrorHandling/BaseErrorHandler.cs`

**Features**:
- Abstract base class cho error handling
- 5 retry strategies:
  - `NoRetry` - Fail immediately
  - `ImmediateRetry` - Retry ngay không delay
  - `ExponentialBackoff` - 2^attempt seconds delay
  - `WaitAndRetry` - Fixed 5s delay
  - `CircuitBreaker` - Fail fast after threshold
  - `Fallback` - Custom fallback logic

**Pros**:
- ✅ Flexible strategy pattern
- ✅ Configurable per error type
- ✅ Good logging
- ✅ Cancellation token support

**Cons**:
- ❌ Manual implementation (không dùng Polly)
- ❌ Circuit breaker simplified (comment: "can be enhanced with Polly")
- ❌ Không có metrics/monitoring
- ❌ Hardcoded delays (5s, 2^n)

**Usage**: Được extend bởi specific error handlers

---

#### 2. **QdrantService** - Polly v8 Resilience Pipeline
**Location**: `TextToSqlAgent.Infrastructure/VectorDB/QdrantService.cs`

**Features**:
- Polly v8 ResiliencePipeline
- Retry strategy:
  - MaxRetryAttempts: 3
  - Delay: 1s base
  - BackoffType: Exponential
  - Handles: HttpRequestException, TaskCanceledException, 5xx status
- Circuit Breaker:
  - FailureRatio: 0.8 (80% failures)
  - SamplingDuration: 30s
  - MinimumThroughput: 5 requests
  - BreakDuration: 30s

**Pros**:
- ✅ Modern Polly v8 syntax
- ✅ Comprehensive logging (OnRetry, OnOpened, OnClosed, OnHalfOpened)
- ✅ Circuit breaker prevents cascading failures
- ✅ Handles transient HTTP failures

**Cons**:
- ❌ Chỉ áp dụng cho Qdrant HTTP calls
- ❌ Hardcoded config (không externalize)

**Status**: ✅ GOOD - Modern, well-implemented

---

#### 3. **SqlCorrectorPlugin** - SQL Correction Retry
**Location**: `TextToSqlAgent.Plugins/SqlCorrectorPlugin.cs`

**Features**:
- `ShouldRetry()` method với logic:
  - Check max attempts
  - Check if error is recoverable
  - Detect repeated SQL (prevent infinite loops)

**Pros**:
- ✅ Smart detection of repeated SQL
- ✅ Respects error recoverability

**Cons**:
- ❌ Không có delay between retries
- ❌ Không có exponential backoff
- ❌ Tightly coupled với correction logic

**Status**: ⚠️ BASIC - Cần enhance với proper retry strategy

---

#### 4. **ToolCircuitBreaker** - Tool Execution Protection
**Location**: `TextToSqlAgent.Infrastructure/Resilience/ToolCircuitBreaker.cs`

**Features**:
- Polly v8 Circuit Breaker cho tool execution
- Per-tool circuit breaker instances
- Prevents cascading failures

**Pros**:
- ✅ Polly v8
- ✅ Per-tool isolation

**Cons**:
- ❌ Không có retry (chỉ có circuit breaker)

**Status**: ✅ GOOD - Focused on circuit breaking

---

### Frontend (JavaScript/React)

#### 1. **useStreamingQuery** - Token Refresh Retry
**Location**: `frontend/src/hooks/useStreamingQuery.js`

**Features**:
- Detects 401 Unauthorized
- Attempts token refresh
- Retries request with new token
- Single retry only

**Pros**:
- ✅ Handles auth expiration gracefully
- ✅ Automatic token refresh

**Cons**:
- ❌ Only 1 retry (no exponential backoff)
- ❌ Hardcoded logic (không reusable)
- ❌ Không handle other transient errors (network, 5xx)

**Status**: ⚠️ BASIC - Cần generalize

---

#### 2. **storageUtils** - LocalStorage Quota Retry
**Location**: `frontend/src/utils/storageUtils.js`

**Features**:
- Detects QuotaExceededError
- Clears old data
- Retries operation once

**Pros**:
- ✅ Handles storage quota gracefully

**Cons**:
- ❌ Single retry only
- ❌ Specific to storage errors

**Status**: ✅ ACCEPTABLE - Niche use case

---

#### 3. **ConnectionSteps** - Manual Retry UI
**Location**: `frontend/src/components/connections/ConnectionSteps.jsx`

**Features**:
- `handleRetry()` callback
- User-triggered retry
- Resets test/sync results

**Pros**:
- ✅ User control

**Cons**:
- ❌ Manual only (no automatic retry)

**Status**: ✅ ACCEPTABLE - UI pattern

---

#### 4. **ErrorRecovery** - User-Driven Retry
**Location**: `frontend/src/components/chat/ErrorRecovery.jsx`

**Features**:
- Shows retry button
- Suggests alternative queries
- User can rephrase and retry

**Pros**:
- ✅ User-friendly
- ✅ Provides alternatives

**Cons**:
- ❌ Manual only

**Status**: ✅ GOOD - UX pattern

---

#### 5. **QuotaProgress** - React Query Retry
**Location**: `frontend/src/components/dashboard/QuotaProgress.jsx`

**Features**:
```javascript
queryOptions: {
  retry: 2,
  retryDelay: 1000,
}
```

**Pros**:
- ✅ Uses React Query built-in retry
- ✅ Configurable

**Cons**:
- ❌ Fixed delay (không exponential)
- ❌ Inconsistent với other components

**Status**: ⚠️ BASIC - Cần standardize

---

## 📊 Gap Analysis

### Problems Identified

#### 1. **Inconsistency** ⚠️
- Backend: Mix of manual retry (BaseErrorHandler) và Polly (QdrantService)
- Frontend: Mix of manual retry, React Query retry, và custom hooks
- No unified retry policy

#### 2. **Lack of Standardization** ⚠️
- Different retry counts: 2, 3, maxAttempts
- Different delays: 1s, 5s, exponential
- Different strategies per component

#### 3. **Missing Features** ❌
- No distributed tracing for retries
- No metrics/monitoring
- No retry budget (prevent retry storms)
- No jitter in exponential backoff

#### 4. **Hardcoded Configuration** ❌
- Retry counts hardcoded
- Delays hardcoded
- Cannot adjust per environment

#### 5. **Limited Scope** ⚠️
- BaseErrorHandler: Chỉ SQL errors
- QdrantService: Chỉ Qdrant HTTP
- useStreamingQuery: Chỉ 401 auth
- No general-purpose retry for all HTTP calls

---

## ✅ Refactoring Recommendations

### Priority 1: Backend Consolidation (HIGH)

#### Recommendation 1.1: Migrate BaseErrorHandler to Polly
**Why**: Polly is industry-standard, battle-tested, feature-rich

**Action**:
```csharp
// Replace manual retry with Polly ResiliencePipeline
public abstract class BaseErrorHandler
{
    private readonly ResiliencePipeline _retryPipeline;
    
    protected BaseErrorHandler(ILogger logger, RetryConfig config)
    {
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = config.MaxRetries,
                Delay = TimeSpan.FromSeconds(config.BaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true, // ✅ Add jitter
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>(ex => IsTransient(ex))
                    .Handle<TimeoutException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .Build();
    }
    
    public async Task<T> HandleAsync<T>(Func<Task<T>> operation)
    {
        return await _retryPipeline.ExecuteAsync(async ct => await operation(), CancellationToken.None);
    }
}
```

**Benefits**:
- ✅ Consistent với QdrantService
- ✅ Jitter prevents thundering herd
- ✅ Circuit breaker prevents cascading failures
- ✅ Better logging/telemetry

---

#### Recommendation 1.2: Externalize Retry Configuration
**Why**: Different environments need different retry policies

**Action**:
```json
// appsettings.json
{
  "RetryPolicy": {
    "MaxRetries": 3,
    "BaseDelaySeconds": 1,
    "UseExponentialBackoff": true,
    "UseJitter": true,
    "CircuitBreaker": {
      "Enabled": true,
      "FailureRatio": 0.5,
      "SamplingDurationSeconds": 30,
      "BreakDurationSeconds": 30
    }
  },
  "QdrantRetryPolicy": {
    "MaxRetries": 3,
    "BaseDelaySeconds": 1
  },
  "SqlCorrectionRetryPolicy": {
    "MaxAttempts": 3,
    "PreventRepeatedSql": true
  }
}
```

```csharp
// Configuration model
public class RetryPolicyConfig
{
    public int MaxRetries { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 1;
    public bool UseExponentialBackoff { get; set; } = true;
    public bool UseJitter { get; set; } = true;
    public CircuitBreakerConfig CircuitBreaker { get; set; } = new();
}
```

**Benefits**:
- ✅ Environment-specific tuning
- ✅ Easy A/B testing
- ✅ No code changes for config updates

---

#### Recommendation 1.3: Add Retry Metrics
**Why**: Monitor retry effectiveness, detect retry storms

**Action**:
```csharp
public class RetryMetrics
{
    private readonly ILogger _logger;
    
    public void RecordRetry(string operation, int attemptNumber, TimeSpan delay)
    {
        _logger.LogInformation(
            "[Retry] Operation: {Operation}, Attempt: {Attempt}, Delay: {Delay}ms",
            operation, attemptNumber, delay.TotalMilliseconds);
        
        // TODO: Send to metrics system (Prometheus, AppInsights, etc.)
    }
    
    public void RecordRetryExhausted(string operation, int totalAttempts)
    {
        _logger.LogError(
            "[Retry] EXHAUSTED - Operation: {Operation}, TotalAttempts: {Attempts}",
            operation, totalAttempts);
    }
}
```

**Benefits**:
- ✅ Visibility into retry behavior
- ✅ Detect retry storms
- ✅ Optimize retry policies based on data

---

### Priority 2: Frontend Standardization (MEDIUM)

#### Recommendation 2.1: Create Unified Retry Hook
**Why**: Consistent retry behavior across all API calls

**Action**:
```javascript
// hooks/useRetryableRequest.js
import { useState, useCallback } from 'react';

export const useRetryableRequest = (config = {}) => {
  const {
    maxRetries = 3,
    baseDelay = 1000,
    exponentialBackoff = true,
    shouldRetry = (error) => error.response?.status >= 500 || error.code === 'NETWORK_ERROR',
  } = config;

  const [isRetrying, setIsRetrying] = useState(false);
  const [retryCount, setRetryCount] = useState(0);

  const executeWithRetry = useCallback(async (operation) => {
    let attempt = 0;
    let lastError;

    while (attempt < maxRetries) {
      try {
        attempt++;
        setRetryCount(attempt);
        
        if (attempt > 1) {
          setIsRetrying(true);
          const delay = exponentialBackoff 
            ? baseDelay * Math.pow(2, attempt - 1)
            : baseDelay;
          await new Promise(resolve => setTimeout(resolve, delay));
        }

        const result = await operation();
        setIsRetrying(false);
        setRetryCount(0);
        return result;

      } catch (error) {
        lastError = error;
        
        if (attempt >= maxRetries || !shouldRetry(error)) {
          setIsRetrying(false);
          setRetryCount(0);
          throw error;
        }
        
        console.log(`[Retry] Attempt ${attempt}/${maxRetries} failed, retrying...`);
      }
    }

    throw lastError;
  }, [maxRetries, baseDelay, exponentialBackoff, shouldRetry]);

  return { executeWithRetry, isRetrying, retryCount };
};
```

**Usage**:
```javascript
const { executeWithRetry, isRetrying } = useRetryableRequest({
  maxRetries: 3,
  baseDelay: 1000,
  exponentialBackoff: true,
});

const fetchData = async () => {
  return await executeWithRetry(async () => {
    const response = await axios.get('/api/data');
    return response.data;
  });
};
```

**Benefits**:
- ✅ Reusable across all components
- ✅ Consistent retry behavior
- ✅ Configurable per use case

---

#### Recommendation 2.2: Standardize React Query Retry
**Why**: React Query already has retry built-in

**Action**:
```javascript
// config/reactQueryConfig.js
export const defaultQueryConfig = {
  queries: {
    retry: (failureCount, error) => {
      // Don't retry on 4xx errors (client errors)
      if (error.response?.status >= 400 && error.response?.status < 500) {
        return false;
      }
      // Retry up to 3 times for 5xx and network errors
      return failureCount < 3;
    },
    retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000), // Exponential with cap
  },
};

// Apply globally
const queryClient = new QueryClient({
  defaultOptions: defaultQueryConfig,
});
```

**Benefits**:
- ✅ Consistent across all React Query usage
- ✅ Smart retry logic (don't retry 4xx)
- ✅ Exponential backoff with cap

---

### Priority 3: Cross-Cutting Concerns (LOW)

#### Recommendation 3.1: Add Retry Budget
**Why**: Prevent retry storms that amplify failures

**Concept**:
```csharp
public class RetryBudget
{
    private int _budget = 100; // 100 retries per minute
    private DateTime _windowStart = DateTime.UtcNow;
    
    public bool CanRetry()
    {
        if (DateTime.UtcNow - _windowStart > TimeSpan.FromMinutes(1))
        {
            _budget = 100;
            _windowStart = DateTime.UtcNow;
        }
        
        if (_budget > 0)
        {
            _budget--;
            return true;
        }
        
        return false; // Budget exhausted
    }
}
```

---

#### Recommendation 3.2: Add Distributed Tracing
**Why**: Track retries across services

**Action**:
- Add OpenTelemetry spans for retries
- Tag with: attempt_number, delay, outcome
- Correlate retries with original request

---

## 📋 Implementation Plan

### Phase 1: Backend Consolidation (2-3 days)
1. Create `RetryPolicyConfig` model
2. Add retry config to `appsettings.json`
3. Migrate `BaseErrorHandler` to Polly
4. Update `SqlCorrectorPlugin` to use Polly
5. Add retry metrics logging
6. Test with integration tests

### Phase 2: Frontend Standardization (1-2 days)
1. Create `useRetryableRequest` hook
2. Create global React Query config
3. Migrate `useStreamingQuery` to use new hook
4. Update components to use standardized retry
5. Test with E2E tests

### Phase 3: Monitoring & Optimization (1 day)
1. Add retry metrics dashboard
2. Implement retry budget
3. Add distributed tracing
4. Tune retry policies based on metrics

---

## 📊 Summary

### Current State
- ⚠️ **Inconsistent**: Mix of manual và Polly, different configs
- ⚠️ **Fragmented**: Retry logic scattered across codebase
- ❌ **Limited**: Missing metrics, monitoring, retry budget
- ❌ **Hardcoded**: Cannot tune per environment

### Recommended State
- ✅ **Unified**: All retry via Polly (backend) và standardized hooks (frontend)
- ✅ **Configurable**: Externalized retry policies
- ✅ **Observable**: Metrics, logging, tracing
- ✅ **Resilient**: Circuit breakers, retry budgets, jitter

### Effort Estimate
- **Total**: 4-6 days
- **Priority**: MEDIUM (not blocking, but improves reliability)
- **Risk**: LOW (incremental migration, backward compatible)

---

**Status**: READY FOR REVIEW  
**Next Step**: Discuss priorities with team, start Phase 1
