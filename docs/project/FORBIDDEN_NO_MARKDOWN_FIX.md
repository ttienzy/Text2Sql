# Forbidden Message - No Markdown Fix

**Date**: 2026-04-08  
**Status**: ✅ COMPLETE  
**Root Cause**: Backend AI prompt yêu cầu format markdown  
**Solution**: Thay đổi prompt để AI trả plain text

---

## 🔍 Root Cause

### Backend Prompt Issue
**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`

**Dòng 5 trong systemPrompt:**
```csharp
5. Format with markdown  // ❌ Đây là nguyên nhân
```

AI nhận instruction này nên trả về:
- `**bold text**`
- ` ```sql ... ``` `
- `# headers`
- `* lists`

→ Frontend nhận raw markdown và hiển thị như vậy

---

## ✅ Solution

### 1. Backend Changes

**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`

#### A. Update System Prompt
```csharp
var systemPrompt = $@"You are a database security assistant.

**CRITICAL RULES:**
1. Be direct and clear
2. Use {language} language ONLY
3. Keep it SHORT - max 200 words
4. Use emojis: ⚠️ for warning, 💡 for tips
5. Use PLAIN TEXT format - NO markdown syntax  // ✅ FIXED
6. Focus on WHY it's blocked and 2-3 safe alternatives

**Formatting:**
- Use plain text only
- For SQL examples, just write them on new lines
- Use bullet points (•) for lists, not markdown
- Use UPPERCASE for emphasis instead of bold
";
```

#### B. Update User Prompt with Example
```csharp
var userPrompt = $@"Generate a SHORT {language} warning in PLAIN TEXT (no markdown):

Example format:
⚠️ OPERATION BLOCKED

Deleting data directly can lead to irreversible loss. Consider safer alternatives:

1. Soft Delete - Mark the record as inactive:
UPDATE customers SET status = 'inactive' WHERE id = 123;

2. Archive - Move to archive table:
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;

💡 Tip: Always backup data before making changes.

Use plain text only, no markdown syntax.";
```

### 2. Frontend Changes

**File**: `frontend/src/components/forbidden/ForbiddenAlert.jsx`

#### Simplified Logic
```javascript
// Before: Strip markdown với regex phức tạp
// After: Chỉ decode HTML entities

const decodeHtml = (str) => {
    const txt = document.createElement("textarea");
    txt.innerHTML = str;
    return txt.value;
};

const cleanMessage = decodeHtml(result.userFacingMessage);
const lines = cleanMessage.split('\n').filter(line => line.trim());
```

#### Simple Rendering
```javascript
{lines.map((line, index) => {
    const isSql = line.match(/^(SELECT|UPDATE|INSERT|DELETE)/i);
    if (isSql) {
        return <pre style={{ /* dark theme */ }}>{line}</pre>;
    }
    return <Paragraph>{line}</Paragraph>;
})}
```

---

## 📊 Before vs After

### Before
**Backend sends:**
```
**⚠️ Thao tác bị chặn**

Bạn đang cố gắng xóa dữ liệu:

```sql
UPDATE customers SET status = 'deleted';
```

**Tip**: Luôn backup.
```

**Frontend displays:**
```
**⚠️ Thao tác bị chặn**    ← Raw markdown visible
```sql                      ← Raw markdown visible
UPDATE customers...
```                         ← Raw markdown visible
**Tip**: Luôn backup.      ← Raw markdown visible
```

### After
**Backend sends:**
```
⚠️ OPERATION BLOCKED

Deleting data directly can lead to irreversible loss.

1. Soft Delete - Mark the record as inactive:
UPDATE customers SET status = 'inactive' WHERE id = 123;

💡 Tip: Always backup data.
```

**Frontend displays:**
```
⚠️ OPERATION BLOCKED       ← Clean text

Deleting data directly...  ← Clean text

1. Soft Delete...          ← Clean text
UPDATE customers...        ← Dark theme code block

💡 Tip: Always backup.     ← Clean text
```

---

## ✅ Benefits

1. **No Markdown Syntax Visible**: Không còn `**`, ` ``` `, `#`
2. **Simpler Frontend**: Không cần strip markdown
3. **Faster**: Không có regex processing
4. **Cleaner**: Plain text dễ đọc hơn
5. **Consistent**: Backend control format hoàn toàn

---

## 🧪 Testing

### Test Steps
1. Rebuild Backend (prompt đã thay đổi)
2. Trigger DELETE operation
3. Check ForbiddenAlert modal

### Expected Results
- ✅ No `**bold**` visible
- ✅ No ` ```sql ``` ` visible
- ✅ SQL code has dark theme
- ✅ Plain text readable
- ✅ Emojis display correctly

---

## 📝 Files Changed

### Backend
- ✅ `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`
  - Line 5: `Format with markdown` → `Use PLAIN TEXT format - NO markdown`
  - Added example format in user prompt

### Frontend
- ✅ `frontend/src/components/forbidden/ForbiddenAlert.jsx`
  - Removed markdown stripping logic
  - Simplified to just decode HTML entities
  - Simple line-by-line rendering

---

## 🎯 Result

**Root cause**: Backend AI prompt yêu cầu markdown  
**Fix**: Thay đổi prompt → AI trả plain text  
**Impact**: Frontend đơn giản hơn, không cần strip markdown

**Status**: COMPLETE ✅
