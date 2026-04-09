# FORBIDDEN Pipeline Analysis - Gap Analysis

**Date**: 2026-04-08  
**Issue**: FORBIDDEN operations (DELETE, DROP, TRUNCATE) chưa được handle đầy đủ ở Frontend  
**Status**: ⚠️ BACKEND READY, FRONTEND MISSING

---

## 🔍 Current State Analysis

### ✅ Backend - FULLY IMPLEMENTED

#### 1. Intent Classification (IntentClassifier.cs)
**Location**: `TextToSqlAgent.Application/Routing/IntentClassifier.cs`

**Pattern Detection** (Lines 38-49):
```csharp
private static readonly (string Pattern, Regex Regex, double Weight)[] ForbiddenPatterns = new[]
{
    // SQL dangerous operations
    (@"\bdrop\s+table\b", new Regex(@"\bdrop\s+table\b", RegexOptions.IgnoreCase), 1.0),
    (@"\bdrop\s+database\b", new Regex(@"\bdrop\s+database\b", RegexOptions.IgnoreCase), 1.0),
    (@"\btruncate\s+table\b", new Regex(@"\btruncate\s+table\b", RegexOptions.IgnoreCase), 1.0),
    (@"\btruncate\b", new Regex(@"\btruncate\b", RegexOptions.IgnoreCase), 0.95),
    (@"\bdelete\s+from\b", new Regex(@"\bdelete\s+from\b", RegexOptions.IgnoreCase), 1.0),
    
    // Vietnamese
    (@"\bxóa\s+bảng\b", new Regex(@"\bxóa\s+bảng\b", RegexOptions.IgnoreCase), 1.0),
    // ... more patterns
};
```

**Quick-Block Logic** (Lines 375-396):
```csharp
foreach (var (pattern, regex, weight) in ForbiddenPatterns)
{
    if (regex.IsMatch(lower))
    {
        _logger.LogWarning(
            "[IntentClassifier] FORBIDDEN pattern detected: {Pattern} (weight: {Weight})",
            pattern, weight);

        return new IntentClassificationResult
        {
            Intent = IntentCategory.Forbidden,
            Route = PipelineRoute.Forbidden,
            Confidence = 0.99,
            Reasoning = $"Quick-block detected dangerous pattern: '{pattern}'",
            Method = ClassificationMethod.RuleBased,
            MatchedKeywords = new List<string> { pattern },
            ForbiddenReason = $"Detected data deletion operation: {pattern}",
            SafeAlternatives = GetSafeAlternatives()
        };
    }
}
```

**LLM Classification** (Lines 561-595):
```
| FORBIDDEN | DELETE data: DELETE, DROP TABLE, TRUNCATE, PURGE |

## FORBIDDEN - Absolute Rules (NO EXCEPTIONS)

Any request with intent to permanently delete data -> classify as FORBIDDEN:
- Direct SQL: DELETE, DROP, TRUNCATE, PURGE
- Natural language: "delete records", "remove users", "clear table", "delete all"
- Even if user says "just testing", "demo only" -> still FORBIDDEN
```

#### 2. ForbiddenPipeline Implementation
**Location**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`

**Features**:
- ✅ Hard rejection - NO SQL generation, NO bypass
- ✅ AI-generated warning messages (bilingual: EN/VI)
- ✅ Safe alternatives suggestions
- ✅ Detailed logging

**Flow**:
```
F1. Detect delete/destroy intent → BLOCK
F2. Hard reject - no SQL generation, no bypass
F3. Suggest safe alternatives (AI-generated message)
```

**Example Output**:
```csharp
var result = new ForbiddenOperationResult
{
    IsBlocked = true,
    OriginalQuestion = "Delete customer with ID 123",
    RejectionReason = "This operation would permanently delete data and is not allowed",
    DetectedPatterns = ["delete", "customer"],
    SafeAlternatives = [
        { Title: "Soft Delete", Description: "UPDATE Customers SET IsDeleted = 1 WHERE ID = 123" },
        { Title: "Archive", Description: "INSERT INTO CustomersArchive SELECT * FROM Customers WHERE ID = 123" }
    ],
    UserFacingMessage = "⚠️ This operation is not allowed as it would permanently delete data..."
};
```

#### 3. Routing in EnhancedAgentOrchestrator
**Location**: `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs`

**Routing Logic** (Lines 1495-1497):
```csharp
PipelineRoute.Forbidden => await RoutToForbiddenPipeline(
    userQuestion, intentResult, stopwatch, cancellationToken),
```

**Pipeline Method** (Lines 1819-1851):
```csharp
private async Task<UnifiedPipelineResponse> RoutToForbiddenPipeline(
    string userQuestion,
    IntentClassificationResult intentResult,
    System.Diagnostics.Stopwatch stopwatch,
    CancellationToken cancellationToken = default)
{
    _logger.LogWarning("[EnhancedAgent] → Routing to FORBIDDEN pipeline (BLOCKED)");

    if (_forbiddenPipeline == null)
    {
        // Fallback if pipeline not configured
        var fallbackResult = new ForbiddenOperationResult { ... };
        return _responseBuilder.BuildForbiddenResponse(fallbackResult, intentResult, stopwatch);
    }

    var result = await _forbiddenPipeline.RejectAsync(
        userQuestion,
        intentResult,
        cancellationToken);

    return _responseBuilder.BuildForbiddenResponse(result, intentResult, stopwatch);
}
```

#### 4. DI Registration
**Location**: `TextToSqlAgent.Application/DependencyInjection/IntentPipelineServiceExtensions.cs`

```csharp
services.AddScoped<IForbiddenPipeline, ForbiddenPipeline>();
```

#### 5. Response Model
**Location**: `TextToSqlAgent.Core/Models/UnifiedPipelineResponse.cs`

```csharp
public enum PipelineType
{
    Query,
    Write,
    Ddl,
    Forbidden,  // ✅ Defined
    Reject
}
```

**Location**: `TextToSqlAgent.Core/Models/PipelineDataModels.cs`

```csharp
[JsonDerivedType(typeof(ForbiddenPipelineData), "forbidden")]
public interface IPipelineData { }

public class ForbiddenPipelineData : IPipelineData
{
    public ForbiddenOperationResult Result { get; set; } = null!;
}
```

---

### ⚠️ Frontend - PARTIALLY IMPLEMENTED

#### ✅ What EXISTS:

1. **Type Definitions** (`frontend/src/types/responses.js`):
```javascript
export const isForbiddenPipeline = (response) => {
    return response?.pipeline === 'Forbidden';
};

export const isForbiddenData = (data) => {
    return data && 'result' in data && data.result?.isBlocked === true;
};

export const isIntentForbidden = (response) => {
    return response?.intent?.type === 'Forbidden';
};
```

2. **ForbiddenAlert Component** (`frontend/src/components/forbidden/ForbiddenAlert.jsx`):
```javascript
const ForbiddenAlert = ({ open, result, onClose }) => {
    // Shows:
    // - Rejection reason
    // - Safe alternatives
    // - Warning icon
    // - Close button
};
```

3. **useIntentBasedChat Hook** (`frontend/src/hooks/useIntentBasedChat.js`):
```javascript
case 'Forbidden': {
    // FORBIDDEN operation - show alert with safe alternatives
    if (isForbiddenData(data.data)) {
        setForbiddenResult(data.data.result);
    }
    return {
        type: 'forbidden',
        data: data.data,
        pipeline,
        intent
    };
}
```

4. **Example Implementation** (`frontend/src/examples/IntentBasedChatExample.jsx`):
```javascript
<ForbiddenAlert
    open={!!forbiddenResult}
    result={forbiddenResult}
    onClose={handleForbiddenClose}
/>
```

#### ❌ What's MISSING:

1. **ChatArea.jsx DOES NOT handle FORBIDDEN pipeline**
   - SSE result watcher only checks for 'Write' and 'Ddl'
   - No state for FORBIDDEN result
   - No ForbiddenAlert import or render
   - FORBIDDEN responses are treated as regular QUERY responses

2. **No FORBIDDEN state in ChatArea**:
```javascript
// MISSING:
const [showForbiddenAlert, setShowForbiddenAlert] = useState(false);
const [forbiddenResult, setForbiddenResult] = useState(null);
```

3. **SSE result watcher doesn't check for FORBIDDEN**:
```javascript
// Current code only checks:
if (pipelineType === 'Write') { ... }
if (pipelineType === 'Ddl') { ... }

// MISSING:
if (pipelineType === 'Forbidden') { ... }
```

---

## 🎯 Expected User Flow (SHOULD BE)

### Test Case: "Delete customer with ID 123"

```
1. User types: "Delete customer with ID 123"
   ↓
2. Frontend sends via SSE to /api/v2/agent/stream
   ↓
3. Backend: IntentClassifier detects "delete" pattern
   - Quick-block: Confidence 0.99
   - Intent: FORBIDDEN
   - Route: FORBIDDEN pipeline
   ↓
4. Backend: ForbiddenPipeline.RejectAsync()
   - Blocks operation
   - Generates AI warning message
   - Suggests safe alternatives:
     * Soft delete: UPDATE Customers SET IsDeleted = 1 WHERE ID = 123
     * Archive: INSERT INTO CustomersArchive SELECT * FROM Customers WHERE ID = 123
   ↓
5. Backend sends SSE result:
   {
     "pipeline": "Forbidden",
     "success": false,
     "message": "⚠️ This operation is not allowed...",
     "data": {
       "result": {
         "isBlocked": true,
         "rejectionReason": "This operation would permanently delete data",
         "safeAlternatives": [...],
         "userFacingMessage": "⚠️ AI-generated warning..."
       }
     }
   }
   ↓
6. Frontend: ChatArea receives SSE result
   ❌ CURRENT: Treats as QUERY → adds to messages as normal response
   ✅ SHOULD: Detects pipeline='Forbidden' → shows ForbiddenAlert modal
   ↓
7. Frontend: ForbiddenAlert displays:
   - ⛔ Warning icon
   - Rejection reason
   - Safe alternatives with SQL examples
   - [Close] button
   ↓
8. User clicks Close
   ↓
9. Alert closes, user can try safe alternative
```

---

## 🐛 Current Bug Behavior

### What Happens NOW:

```
User: "Delete customer with ID 123"
  ↓
Backend: Correctly detects FORBIDDEN → blocks → sends rejection
  ↓
Frontend ChatArea: Receives SSE result
  ↓
SSE watcher: Does NOT check for pipeline='Forbidden'
  ↓
Falls through to default QUERY handling
  ↓
Adds to messages as assistant response:
  "⚠️ This operation is not allowed as it would permanently delete data..."
  ↓
Result: Shows as normal chat message instead of alert modal
```

**Problems**:
1. ❌ No visual distinction (should be alert/modal, not chat message)
2. ❌ Safe alternatives not prominently displayed
3. ❌ User might not understand it's BLOCKED (looks like normal response)
4. ❌ No clear call-to-action for safe alternatives

---

## ✅ Solution: Integrate ForbiddenAlert into ChatArea

### Required Changes:

#### 1. Add Imports
```javascript
import ForbiddenAlert from '../forbidden/ForbiddenAlert';
import { isForbiddenPipeline } from '../../types/responses';
```

#### 2. Add State
```javascript
const [showForbiddenAlert, setShowForbiddenAlert] = useState(false);
const [forbiddenResult, setForbiddenResult] = useState(null);
```

#### 3. Update SSE Result Watcher
```javascript
useEffect(() => {
  if (!sseResult) return;

  const pipelineType = sseResult.pipeline || sseResult.Pipeline;

  // FORBIDDEN operation - show alert
  if (pipelineType === 'Forbidden') {
    const result = sseResult.data?.result;
    if (result && result.isBlocked) {
      setForbiddenResult(result);
      setShowForbiddenAlert(true);
      
      // Add user message to chat
      const filteredMessages = messages.filter(m => !m.isOptimistic && !m.isPending);
      const userMessage = {
        id: getUniqueId('user'),
        conversationId: currentConversation?.id,
        role: 'user',
        content: currentQuestion,
        createdAt: new Date().toISOString(),
      };
      setMessages([...filteredMessages, userMessage]);
      
      resetStream();
      return; // Don't add assistant message - alert will handle display
    }
  }

  // WRITE operation - show confirmation modal
  if (pipelineType === 'Write') { ... }

  // Default: QUERY operation
  // ... existing code ...
}, [sseResult]);
```

#### 4. Add Alert Handler
```javascript
const handleForbiddenClose = () => {
  setShowForbiddenAlert(false);
  
  // Optionally add rejection message to chat
  if (forbiddenResult) {
    const rejectionMessage = {
      id: getUniqueId('assistant'),
      conversationId: currentConversation?.id,
      role: 'assistant',
      content: `⛔ ${forbiddenResult.rejectionReason}`,
      success: false,
      isForbidden: true,
      createdAt: new Date().toISOString(),
    };
    
    const currentMessages = messages.filter(m => !m.isPending);
    setMessages([...currentMessages, rejectionMessage]);
  }
  
  setForbiddenResult(null);
  setCurrentQuestion('');
};
```

#### 5. Render Alert Component
```javascript
{/* FORBIDDEN Alert */}
<ForbiddenAlert
  open={showForbiddenAlert}
  result={forbiddenResult}
  onClose={handleForbiddenClose}
/>
```

---

## 📊 Test Cases to Verify

### Test 10.1 - DELETE Single Record
**Input**: "Delete customer with ID 123"  
**Expected**:
- ✅ Backend detects FORBIDDEN pattern
- ✅ Backend blocks operation
- ✅ Frontend shows ForbiddenAlert modal
- ✅ Modal displays rejection reason
- ✅ Modal shows safe alternatives
- ✅ NO SQL executed

### Test 10.2 - DROP TABLE
**Input**: "Drop the Customers table"  
**Expected**:
- ✅ Backend detects FORBIDDEN pattern
- ✅ Backend blocks operation
- ✅ Frontend shows ForbiddenAlert modal
- ✅ Modal displays critical warning
- ✅ Modal explains why dangerous
- ✅ NO SQL executed

### Test 10.3 - Vietnamese DELETE
**Input**: "Xóa khách hàng có ID 123"  
**Expected**:
- ✅ Backend detects Vietnamese FORBIDDEN pattern
- ✅ Backend generates Vietnamese warning message
- ✅ Frontend shows ForbiddenAlert with Vietnamese text
- ✅ Safe alternatives in Vietnamese

---

## 🚀 Implementation Priority

**Priority**: HIGH  
**Estimated Time**: 30 minutes  
**Complexity**: LOW (similar to WRITE modal integration)

**Steps**:
1. Add imports and state to ChatArea.jsx
2. Update SSE result watcher to detect FORBIDDEN
3. Add handleForbiddenClose handler
4. Render ForbiddenAlert component
5. Test with DELETE and DROP operations

---

## 📝 Summary

### Backend Status: ✅ COMPLETE
- Intent classification with pattern detection
- ForbiddenPipeline implementation
- AI-generated warning messages
- Safe alternatives suggestions
- Proper routing and DI registration

### Frontend Status: ⚠️ INCOMPLETE
- ✅ ForbiddenAlert component exists
- ✅ Type definitions exist
- ✅ useIntentBasedChat hook handles FORBIDDEN
- ❌ ChatArea.jsx does NOT handle FORBIDDEN
- ❌ No state for FORBIDDEN result
- ❌ No alert rendering

### Action Required:
Integrate ForbiddenAlert into ChatArea.jsx following the same pattern as WriteConfirmationModal.

---

**Status**: READY TO IMPLEMENT  
**Next Step**: Add FORBIDDEN handling to ChatArea.jsx
