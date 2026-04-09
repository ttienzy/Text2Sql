# Forbidden Message - Static Template Fix (Final Solution)

**Date**: 2026-04-08  
**Status**: ✅ COMPLETE  
**Root Cause**: AI vẫn trả markdown dù đã có prompt  
**Solution**: Bỏ AI, dùng static templates (pure plain text)

---

## 🔍 Problem

Dù đã update prompt yêu cầu plain text, AI vẫn trả về:
- `&#x27;` (HTML entity cho `'`)
- Markdown syntax (`**`, ` ``` `, etc.)
- Không consistent

→ Cần giải pháp triệt để: KHÔNG DÙNG AI

---

## ✅ Solution: Static Templates

### 1. Tạo Template Class

**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenMessageTemplates.cs`

```csharp
public static class ForbiddenMessageTemplates
{
    public static string GetEnglishMessage(string operation) { }
    public static string GetVietnameseMessage(string operation) { }
    public static string GetCustomMessage(string operation, bool isVietnamese, string tableName) { }
}
```

**Features:**
- Pure plain text - NO markdown
- NO HTML entities
- Hardcoded messages
- Support Vietnamese & English
- Custom message với table name

### 2. Update ForbiddenPipeline

**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`

#### A. Bỏ AI Generation
```csharp
// BEFORE: Call AI with prompt
var aiMessage = await GenerateAIMessageAsync(...);

// AFTER: Use static template
return ForbiddenMessageTemplates.GetCustomMessage(...);
```

#### B. Extract Table Name
```csharp
private string ExtractTableName(string question)
{
    // Pattern: "delete from <table>"
    // Pattern: "xóa <table>"
    // Pattern: "drop table <table>"
    return tableName;
}
```

#### C. Build Message
```csharp
private async Task<string> BuildUserFacingMessageAsync(...)
{
    var tableName = ExtractTableName(result.OriginalQuestion);
    
    if (!string.IsNullOrEmpty(tableName))
    {
        return ForbiddenMessageTemplates.GetCustomMessage(
            operation, isVietnamese, tableName
        );
    }
    
    return isVietnamese
        ? ForbiddenMessageTemplates.GetVietnameseMessage(operation)
        : ForbiddenMessageTemplates.GetEnglishMessage(operation);
}
```

---

## 📊 Template Examples

### English Template
```
⚠️ OPERATION BLOCKED

You are attempting to delete data from table customers. This can lead to permanent data loss.

SAFE ALTERNATIVES:

1. Soft Delete - Mark as deleted instead of actual deletion:
UPDATE customers SET is_deleted = 1, deleted_at = NOW() WHERE id = 123;

2. Archive - Move to archive table:
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;
UPDATE customers SET archived = 1 WHERE id = 123;

3. Deactivate - Disable instead of delete:
UPDATE customers SET status = 'inactive', active = 0 WHERE id = 123;

💡 NOTE: Always backup data before making any changes.
```

### Vietnamese Template
```
⚠️ THAO TÁC BỊ CHẶN

Bạn đang cố gắng xóa dữ liệu từ bảng customers. Điều này có thể gây mất mát dữ liệu vĩnh viễn.

CÁC PHƯƠNG ÁN THAY THẾ AN TOÀN:

1. Soft Delete - Đánh dấu là đã xóa thay vì xóa thật:
UPDATE customers SET is_deleted = 1, deleted_at = NOW() WHERE id = 123;

2. Archive - Chuyển sang bảng lưu trữ:
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;
UPDATE customers SET archived = 1 WHERE id = 123;

3. Deactivate - Vô hiệu hóa thay vì xóa:
UPDATE customers SET status = 'inactive', active = 0 WHERE id = 123;

💡 LƯU Ý: Luôn sao lưu dữ liệu trước khi thực hiện bất kỳ thay đổi nào.
```

---

## ✅ Benefits

1. **100% Plain Text**: Không có markdown, không có HTML entities
2. **Consistent**: Luôn giống nhau, không phụ thuộc AI
3. **Fast**: Không cần call LLM
4. **Reliable**: Không có AI hallucination
5. **Maintainable**: Dễ update templates
6. **Customizable**: Có thể thêm table name động

---

## 🎯 Architecture

```
User Question
    ↓
ForbiddenPipeline.RejectAsync()
    ↓
BuildUserFacingMessageAsync()
    ↓
ExtractTableName() → Get table name from question
    ↓
ForbiddenMessageTemplates.GetCustomMessage()
    ↓
Return plain text message (NO AI, NO markdown)
```

---

## 📝 Files Changed

### New Files
- ✅ `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenMessageTemplates.cs`
  - Static templates for English & Vietnamese
  - Custom message with table name
  - Pure plain text, no markdown

### Modified Files
- ✅ `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`
  - Removed `GenerateAIMessageAsync()` method
  - Added `ExtractTableName()` method
  - Updated `BuildUserFacingMessageAsync()` to use templates
  - No more LLM calls for forbidden messages

---

## 🧪 Testing

### Test Cases

1. **English DELETE**:
   - Input: `DELETE FROM customers WHERE id = 123`
   - Expected: English template with "customers" table

2. **Vietnamese DELETE**:
   - Input: `Xóa khách hàng có id = 123`
   - Expected: Vietnamese template with "khách hàng" → "customers"

3. **DROP TABLE**:
   - Input: `DROP TABLE users`
   - Expected: Template with "users" table

4. **Generic (no table)**:
   - Input: `Delete all data`
   - Expected: Generic template without table name

### Verification
- ✅ No `**bold**` visible
- ✅ No ` ```sql ``` ` visible
- ✅ No `&#x27;` or HTML entities
- ✅ Pure plain text
- ✅ SQL code on separate lines
- ✅ Emojis display correctly

---

## 🎯 Result

**Root Cause**: AI không reliable cho plain text  
**Solution**: Static templates - 100% controlled  
**Impact**: 
- No more markdown issues
- Consistent messages
- Faster (no LLM call)
- Easier to maintain

**Status**: COMPLETE ✅

---

## 📌 Future Enhancements

1. Add more templates for different operations (TRUNCATE, DROP, etc.)
2. Support more languages
3. Add context-aware messages based on table type
4. Template variables for dynamic content
