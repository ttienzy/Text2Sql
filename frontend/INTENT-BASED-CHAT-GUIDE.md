# 🎯 Intent-Based Chat - Frontend Integration Guide

## 📋 Overview

Frontend đã được refactor để hỗ trợ đầy đủ intent-based routing với 4 pipelines:
- **QUERY** - Standard queries (existing)
- **WRITE** - INSERT/UPDATE with confirmation
- **DDL** - CREATE INDEX/ALTER with impact analysis
- **FORBIDDEN** - DELETE/DROP/TRUNCATE (blocked)

---

## 🏗️ Architecture

### File Structure

```
frontend/src/
├── api/
│   ├── write/
│   │   └── index.js          # WRITE API client + hook
│   ├── ddl/
│   │   └── index.js          # DDL API client + hook
│   └── agent/
│       └── index.js          # Main agent API
├── components/
│   ├── write/
│   │   ├── WriteConfirmationModal.jsx
│   │   └── index.js
│   ├── ddl/
│   │   ├── DDLImpactCard.jsx
│   │   └── index.js
│   ├── forbidden/
│   │   ├── ForbiddenAlert.jsx
│   │   └── index.js
│   └── chat/
│       ├── IntentBasedChatInterface.jsx  # NEW: Integrated component
│       └── index.js
├── hooks/
│   └── useIntentBasedChat.js  # Unified hook
└── constants/
    └── api.js                 # API endpoints & constants
```

---

## 🚀 Quick Start

### Option 1: Use Unified Hook (Recommended)

```javascript
import { useIntentBasedChat } from '../hooks/useIntentBasedChat';

function ChatPage() {
    const {
        queryResponse,
        forbiddenResult,
        writePreview,
        ddlPreview,
        currentPipeline,
        loading,
        error,
        send,
        executeWrite,
        executeDDL,
        reset
    } = useIntentBasedChat(connectionId, conversationId);

    const handleSend = async (question) => {
        const result = await send(question);
        
        // result.type can be: 'query', 'write', 'ddl', 'forbidden', 'error'
        // result.pipeline: 'QUERY', 'WRITE', 'DDL', 'FORBIDDEN'
        
        if (result.type === 'query') {
            // Display query results
            console.log(result.data);
        }
        // Modals will show automatically for write/ddl/forbidden
    };

    return (
        <div>
            {/* Your chat UI */}
            <button onClick={() => handleSend('Show all users')}>
                Send Query
            </button>

            {/* Modals */}
            <WriteConfirmationModal
                open={!!writePreview}
                preview={writePreview}
                onConfirm={() => executeWrite(question, writePreview)}
                onCancel={reset}
                loading={loading}
            />

            <DDLImpactCard
                open={!!ddlPreview}
                preview={ddlPreview}
                onConfirm={() => executeDDL(question, ddlPreview)}
                onCancel={reset}
                loading={loading}
            />

            <ForbiddenAlert
                open={!!forbiddenResult}
                result={forbiddenResult}
                onClose={reset}
            />
        </div>
    );
}
```

### Option 2: Use Integrated Component

```javascript
import IntentBasedChatInterface from '../components/chat/IntentBasedChatInterface';

function ChatPage() {
    const handleQueryResponse = (response) => {
        // Handle query results
        console.log('Query response:', response);
    };

    const handleError = (error) => {
        // Handle errors
        console.error('Error:', error);
    };

    return (
        <IntentBasedChatInterface
            connectionId={connectionId}
            conversationId={conversationId}
            onQueryResponse={handleQueryResponse}
            onError={handleError}
        />
    );
}
```

---

## 📡 API Endpoints

All endpoints are defined in `constants/api.js`:

```javascript
import { API_ENDPOINTS } from '../constants/api';

// WRITE operations
API_ENDPOINTS.WRITE.PREVIEW  // POST /api/agent/write/preview
API_ENDPOINTS.WRITE.EXECUTE  // POST /api/agent/write/execute

// DDL operations
API_ENDPOINTS.DDL.PREVIEW    // POST /api/agent/ddl/preview
API_ENDPOINTS.DDL.EXECUTE    // POST /api/agent/ddl/execute

// Main agent
API_ENDPOINTS.AGENT.PROCESS  // POST /api/agent/process
```

---

## 🎨 Components

### 1. WriteConfirmationModal

Shows SQL preview and requires user confirmation for INSERT/UPDATE.

**Props:**
```javascript
{
    open: boolean,              // Modal visibility
    preview: {                  // Preview data from API
        operationType: string,  // 'Insert' or 'Update'
        targetTable: string,
        sqlStatement: string,
        estimatedAffectedRows: number,
        hasWhereClause: boolean,
        warnings: string[],
        affectedColumns: string[]
    },
    onConfirm: () => void,     // Execute operation
    onCancel: () => void,      // Cancel operation
    loading: boolean           // Execution in progress
}
```

**Features:**
- ✅ SQL syntax highlighting
- ✅ Estimated affected rows
- ✅ WHERE clause validation
- ✅ Warning messages
- ✅ Disabled confirm if no WHERE clause (UPDATE)

### 2. DDLImpactCard

Shows DDL script with impact analysis.

**Props:**
```javascript
{
    open: boolean,
    preview: {
        operationType: string,  // 'CreateIndex', 'AlterTable', etc.
        targetObject: string,
        ddlScript: string,
        impact: {
            estimatedStorageBytes: number,
            estimatedLockDuration: string,
            estimatedPerformanceGain: number,
            writeOverheadPercent: number,
            benefits: string[],
            warnings: string[]
        },
        relatedObjects: string[]
    },
    onConfirm: () => void,
    onCancel: () => void,
    loading: boolean
}
```

**Features:**
- ✅ Impact metrics (storage, lock time, performance)
- ✅ Benefits list
- ✅ Warning messages
- ✅ Related objects
- ✅ Visual metric cards

### 3. ForbiddenAlert

Shows rejection message with safe alternatives.

**Props:**
```javascript
{
    open: boolean,
    result: {
        rejectionReason: string,
        forbiddenReason: string,
        detectedPatterns: string[],
        safeAlternatives: [{
            title: string,
            description: string,
            exampleSql: string
        }]
    },
    onClose: () => void
}
```

**Features:**
- ✅ Clear rejection message
- ✅ Detected dangerous patterns
- ✅ 4 safe alternatives with examples
- ✅ Educational content

---

## 🔧 Hooks

### useIntentBasedChat

Unified hook for all pipeline types.

**Parameters:**
```javascript
const hook = useIntentBasedChat(connectionId, conversationId);
```

**Returns:**
```javascript
{
    // State
    queryResponse: object | null,
    forbiddenResult: object | null,
    writePreview: object | null,
    ddlPreview: object | null,
    currentPipeline: string | null,  // 'QUERY', 'WRITE', 'DDL', 'FORBIDDEN'
    loading: boolean,
    error: string | null,

    // Actions
    send: (question: string) => Promise<{
        type: 'query' | 'write' | 'ddl' | 'forbidden' | 'error',
        data: object,
        pipeline: string
    }>,
    executeWrite: (question, preview) => Promise<object>,
    executeDDL: (question, preview) => Promise<object>,
    reset: () => void,

    // Sub-operations (advanced)
    writeOperation: { preview, result, loading, error },
    ddlOperation: { preview, result, loading, error }
}
```

### useWriteOperation

Specialized hook for WRITE operations.

```javascript
import { useWriteOperation } from '../api/write';

const {
    preview,
    result,
    loading,
    error,
    generatePreview,
    execute,
    reset
} = useWriteOperation();

// Generate preview
await generatePreview(question, connectionId, conversationId);

// Execute after confirmation
await execute(question, connectionId, conversationId, preview);
```

### useDDLOperation

Specialized hook for DDL operations.

```javascript
import { useDDLOperation } from '../api/ddl';

const {
    preview,
    result,
    loading,
    error,
    generatePreview,
    execute,
    reset
} = useDDLOperation();
```

---

## 🎯 Usage Examples

### Example 1: Simple Query

```javascript
const { send } = useIntentBasedChat(connectionId, conversationId);

const result = await send('Show me all users');
// result.type === 'query'
// result.data contains query results
```

### Example 2: Write Operation

```javascript
const { send, executeWrite, writePreview } = useIntentBasedChat(connectionId, conversationId);

// Step 1: Send question
const result = await send('Update user email to test@example.com where id = 1');
// result.type === 'write'
// writePreview is now populated

// Step 2: Show modal (automatic via useEffect)
// User reviews and clicks confirm

// Step 3: Execute
await executeWrite(question, writePreview);
```

### Example 3: DDL Operation

```javascript
const { send, executeDDL, ddlPreview } = useIntentBasedChat(connectionId, conversationId);

// Step 1: Send question
const result = await send('Create index on users email column');
// result.type === 'ddl'
// ddlPreview is now populated

// Step 2: Show impact analysis modal
// User reviews metrics and clicks confirm

// Step 3: Execute
await executeDDL(question, ddlPreview);
```

### Example 4: Forbidden Operation

```javascript
const { send, forbiddenResult } = useIntentBasedChat(connectionId, conversationId);

const result = await send('Delete all inactive users');
// result.type === 'forbidden'
// forbiddenResult contains rejection + safe alternatives
// Modal shows automatically
```

---

## 🔄 Flow Diagrams

### WRITE Flow

```
User Input
    ↓
send(question)
    ↓
Backend Intent Classification
    ↓
WRITE Pipeline Detected
    ↓
Generate Preview (W1-W7)
    ↓
Return writePreview
    ↓
Show WriteConfirmationModal
    ↓
User Reviews SQL
    ↓
User Clicks Confirm
    ↓
executeWrite(question, preview)
    ↓
Backend Executes (W8)
    ↓
Return Result
    ↓
Show Success Message
```

### DDL Flow

```
User Input
    ↓
send(question)
    ↓
Backend Intent Classification
    ↓
DDL Pipeline Detected
    ↓
Generate Preview + Impact (D1-D6)
    ↓
Return ddlPreview
    ↓
Show DDLImpactCard
    ↓
User Reviews Impact
    ↓
User Clicks Confirm
    ↓
executeDDL(question, preview)
    ↓
Backend Executes (D7-D8)
    ↓
Return Result
    ↓
Show Success Message
```

### FORBIDDEN Flow

```
User Input
    ↓
send(question)
    ↓
Backend Intent Classification
    ↓
FORBIDDEN Pipeline Detected
    ↓
Hard Block (F1-F2)
    ↓
Generate Safe Alternatives (F3)
    ↓
Return forbiddenResult
    ↓
Show ForbiddenAlert
    ↓
User Reads Alternatives
    ↓
User Clicks "I Understand"
    ↓
Modal Closes
```

---

## 🎨 Styling

All components use Material-UI (MUI) with consistent styling:

- **Colors:**
  - QUERY: Primary (blue)
  - WRITE: Warning (orange)
  - DDL: Primary (blue)
  - FORBIDDEN: Error (red)

- **Icons:**
  - WRITE: ⚠️ Warning
  - DDL: ℹ️ Info
  - FORBIDDEN: 🚫 Block

- **Modals:**
  - Max width: `md` (960px)
  - Border radius: 2
  - Dividers between sections

---

## 🐛 Error Handling

All hooks include comprehensive error handling:

```javascript
const { error, send } = useIntentBasedChat(connectionId, conversationId);

try {
    const result = await send(question);
    
    if (result.type === 'error') {
        // Handle error
        console.error(result.error);
        console.error(result.details); // Full error response
    }
} catch (err) {
    // Network or unexpected errors
    console.error(err);
}

// Display error in UI
{error && <Alert severity="error">{error}</Alert>}
```

---

## ✅ Testing Checklist

- [ ] QUERY operations work normally
- [ ] WRITE operations show confirmation modal
- [ ] UPDATE without WHERE is blocked
- [ ] DDL operations show impact analysis
- [ ] FORBIDDEN operations show alert with alternatives
- [ ] Error messages display correctly
- [ ] Loading states work properly
- [ ] Modals can be cancelled
- [ ] Success messages appear after execution
- [ ] Schema reloads after DDL operations

---

## 📚 Additional Resources

- **Backend API:** See `QUICK-START-TESTING-GUIDE.md`
- **Constants:** `frontend/src/constants/api.js`
- **Example:** `frontend/src/examples/IntentBasedChatExample.jsx`
- **Components:** `frontend/src/components/write|ddl|forbidden/`

---

**Last Updated:** March 22, 2026  
**Status:** Production-Ready ✅  
**Version:** 1.0.0
