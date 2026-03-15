# Bugfix Requirements Document

## Introduction

The /api/agent/classify endpoint experiences critical hanging behavior where requests do not respond for 10+ minutes when processing queries. This severely impacts user experience and system reliability. The root cause analysis identified three critical issues: missing timeout configuration on OpenAI API calls (primary), unregistered LLM query classifier in the DI container (secondary), and blocking synchronous calls in SchemaScanner (tertiary). This bugfix addresses all three issues to ensure reliable, timely API responses.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the /api/agent/classify endpoint is called with a query THEN the system hangs for 10+ minutes without returning a response

1.2 WHEN OpenAI API calls are made through Semantic Kernel THEN the system uses infinite or very long default HttpClient timeout instead of the configured 120-second RequestTimeout

1.3 WHEN Vietnamese queries have low rule-based confidence AND require LLM fallback THEN the system fails silently because ILLMQueryClassifier is not registered in the DI container

1.4 WHEN SchemaScanner executes database operations THEN the system uses blocking synchronous calls (GetAwaiter().GetResult()) which can cause deadlocks in async contexts

### Expected Behavior (Correct)

2.1 WHEN the /api/agent/classify endpoint is called with a query THEN the system SHALL return a response within the configured timeout period (120 seconds) or return a timeout error

2.2 WHEN OpenAI API calls are made through Semantic Kernel THEN the system SHALL apply the configured RequestTimeout (120 seconds) to prevent indefinite hanging

2.3 WHEN Vietnamese queries have low rule-based confidence AND require LLM fallback THEN the system SHALL successfully use the registered ILLMQueryClassifier implementation for classification

2.4 WHEN SchemaScanner executes database operations THEN the system SHALL use proper async/await patterns without blocking synchronous calls to prevent deadlocks

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the /api/agent/classify endpoint receives valid queries that complete within timeout THEN the system SHALL CONTINUE TO return correct classification results

3.2 WHEN OpenAI API calls complete successfully within the timeout period THEN the system SHALL CONTINUE TO process and return LLM responses correctly

3.3 WHEN rule-based query classification has high confidence THEN the system SHALL CONTINUE TO use rule-based classification without invoking LLM fallback

3.4 WHEN SchemaScanner successfully retrieves database schema information THEN the system SHALL CONTINUE TO return complete and accurate schema metadata

3.5 WHEN other API endpoints make database or LLM calls THEN the system SHALL CONTINUE TO function correctly without being affected by these timeout fixes
