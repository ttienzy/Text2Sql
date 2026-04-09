# Frontend WRITE Operation Integration Plan

**Date**: 2026-04-08  
**Issue**: Frontend không hiển thị INSERT/UPDATE preview modal khi nhận SSE result từ WRITE pipeline  
**Current State**: Backend gửi WRITE preview qua SSE, nhưng frontend chỉ handle QUERY results

---

## 🔍 Current Architecture

### Backend (✅ Working)
```
User: "Thêm khách hàng mới..."
  ↓
IntentClassifier → INSERT (95%)
  ↓
RouteToWritePipelineAsync
  ↓
WritePipeline.GeneratePreviewAsync
  ↓
SSE Events:
  - stage_update (AGENT_THINKING, SCHEMA_RETRIEVAL, BUILDING_RESPONSE)
  - result: { pipeline: 'Write', data: { preview: {...} }, requiresConfirmation: true }
```

### Frontend (❌ Not Handling WRITE)
```javascript
// ChatArea.jsx uses useStreamingQuery
const { result: sseResult } = useStreamingQuery();

// When sseResult arrives:
useEffect(() => {
  if (!sseResult) return;
  
  // ❌ PROBLEM: Only handles QUERY results
  // Extracts: answer, sqlQuery, results, rowCount
  // Does NOT check for pipelineType === 'Write'
  // Does NOT show WriteConfirmationModal
}, [sseResult]);
```

---

## ✅ Solution: Integrate WriteConfirmationModal into ChatArea

### Option 1: Minimal Integration (RECOMMENDED)
Add WRITE/DDL handling directly to ChatArea.jsx SSE result watcher.

**Pros**:
- Minimal code changes
- Keeps existing streaming architecture
- Reuses existing WriteConfirmationModal component

**Cons**:
- ChatArea becomes slightly more complex

### Option 2: Use IntentBasedChatInterface
Replace useStreamingQuery with useIntentBasedChat hook.

**Pros**:
- Cleaner separation of concerns
- Already handles all pipeline types

**Cons**:
- Requires refactoring ChatArea
- May lose real-time SSE progress updates

**Decision**: Go with Option 1 (minimal integration)

---

## 📝 Implementation Steps

### Step 1: Add State for WRITE/DDL Modals
```javascript
// ChatArea.jsx
const [showWriteModal, setShowWriteModal] = useState(false);
const [writePreview, setWritePreview] = useState(null);
const [pendingQuestion, setPendingQuestion] = useState('');
```

### Step 2: Update SSE Result Watcher
```javascript
useEffect(() => {
  if (!sseResult) return;

  // ✅ NEW: Check pipeline type
  const pipelineType = sseResult.pipeline || sseResult.Pipeline;

  if (pipelineType === 'Write') {
    // WRITE operation - show confirmation modal
    const preview = sseResult.data?.preview;
    if (preview) {
      setWritePreview(preview);
      setPendingQuestion(currentQuestion);
      setShowWriteModal(true);
      return; // Don't add to messages yet
    }
  }

  if (pipelineType === 'Ddl') {
    // DDL operation - show impact modal
    // Similar handling
    return;
  }

  // Default: QUERY operation - add to messages as before
  // ... existing code ...
}, [sseResult]);
```

### Step 3: Add Confirmation Handlers
```javascript
const handleConfirmWrite = async () => {
  if (!writePreview || !pendingQuestion) return;

  try {
    setIsExecuting(true);

    // Call WRITE execute endpoint
    const response = await axios.post('/api/v2/write/execute', {
      question: pendingQuestion,
      connectionId: activeConnection.id,
      conversationId: currentConversation.id,
      isConfirmed: true,
      preview: writePreview
    });

    // Add success message to chat
    const successMessage = {
      id: getUniqueId('assistant'),
      role: 'assistant',
      content: `✓ Successfully ${response.data.operationType?.toLowerCase()} ${response.data.actualAffectedRows} row(s)`,
      sqlQuery: response.data.sqlExecuted,
      success: true,
      createdAt: new Date().toISOString(),
    };

    setMessages([...messages, successMessage]);
    setShowWriteModal(false);
    setPendingQuestion('');
    message.success('Operation executed successfully');

  } catch (error) {
    message.error(error.response?.data?.message || 'Failed to execute operation');
  } finally {
    setIsExecuting(false);
  }
};

const handleCancelWrite = () => {
  setShowWriteModal(false);
  setPendingQuestion('');
  message.info('Operation cancelled');
};
```

### Step 4: Add Modal Component
```javascript
import WriteConfirmationModal from '../write/WriteConfirmationModal';

// In render:
<WriteConfirmationModal
  open={showWriteModal}
  preview={writePreview}
  onConfirm={handleConfirmWrite}
  onCancel={handleCancelWrite}
  loading={isExecuting}
/>
```

---

## 🎯 Expected User Flow

### INSERT Operation
1. User types: "Thêm khách hàng mới tên John Doe với email john@example.com"
2. Frontend sends via SSE
3. Backend classifies → INSERT → WRITE pipeline
4. Backend sends SSE events:
   - `stage_update`: "Generating INSERT preview..." (20%)
   - `stage_update`: "Identifying target table..." (40%)
   - `stage_update`: "Preview generated - awaiting confirmation" (90%)
   - `result`: { pipeline: 'Write', data: { preview: {...} } }
5. Frontend receives result → shows WriteConfirmationModal
6. Modal displays:
   - SQL: `INSERT INTO Customers (CustomerName, Email) VALUES ('John Doe', 'john@example.com')`
   - Estimated rows: 1
   - Target table: Customers
   - Buttons: [Cancel] [Confirm Insert]
7. User clicks "Confirm Insert"
8. Frontend calls `/api/v2/write/execute` with `isConfirmed: true`
9. Backend executes SQL
10. Frontend shows success message in chat

### UPDATE Operation
Similar flow, but modal shows:
- WHERE clause warning
- Estimated affected rows
- Affected columns
- [Cancel] [Confirm Update] (red button)

---

## 📁 Files to Modify

1. **frontend/src/components/layout/ChatArea.jsx**
   - Add state for WRITE/DDL modals
   - Update SSE result watcher to check pipeline type
   - Add confirmation handlers
   - Import and render WriteConfirmationModal

2. **frontend/src/api/write.js** (if not exists)
   - Create API functions for WRITE execute endpoint

---

## 🧪 Testing Checklist

- [ ] INSERT operation shows preview modal
- [ ] UPDATE operation shows preview modal with WHERE clause warning
- [ ] User can cancel operation
- [ ] User can confirm operation
- [ ] Success message appears in chat after execution
- [ ] Error handling works (e.g., validation errors)
- [ ] SSE progress updates show during preview generation
- [ ] Modal closes after confirmation/cancellation

---

## 🚀 Next Steps

1. Implement Step 1-4 in ChatArea.jsx
2. Test with INSERT operation
3. Test with UPDATE operation
4. Add DDL modal support (similar pattern)
5. Add error handling for edge cases

---

**Status**: READY TO IMPLEMENT  
**Estimated Time**: 1-2 hours  
**Priority**: HIGH - Blocks INSERT/UPDATE functionality

