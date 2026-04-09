# Forbidden Message - AI Generation + Markdown Rendering Solution

**Date**: 2026-04-08  
**Status**: ✅ IMPLEMENTED  
**Approach**: Kết hợp AI Generation (Backend) + ReactMarkdown Rendering (Frontend)

---

## 🎯 Vấn đề

### Template cứng (Hard-coded) - Không linh hoạt
```csharp
// ❌ Cách cũ: Template cứng
return ForbiddenMessageTemplates.GetCustomMessage(operation, isVietnamese, tableName);
```

**Nhược điểm:**
- Không thông minh, không thể điều chỉnh theo context
- Phải maintain nhiều template cho từng trường hợp
- Mất đi sức mạnh của AI
- Cảm giác máy móc, không tự nhiên

### Regex xóa Markdown - Phức tạp và dễ lỗi
```javascript
// ❌ Cách cũ: Dùng regex để xóa markdown
const cleanMessage = message
    .replace(/\*\*/g, '')
    .replace(/```/g, '')
    .replace(/#{1,6}\s/g, '');
```

**Nhược điểm:**
- Mất đi format đẹp của markdown
- Regex phức tạp, dễ miss case
- HTML entities (&#96;, &#x27;) gây lỗi UI
- Không tận dụng được markdown rendering

---

## ✅ Giải pháp: AI + ReactMarkdown

### Nguyên tắc
1. **Backend**: AI tự generate message với markdown format
2. **Frontend**: ReactMarkdown render markdown thành UI đẹp
3. **Fallback**: Vẫn giữ template cứng nếu AI fail

### Ưu điểm
- ✅ **Linh hoạt**: AI tự điều chỉnh message theo context
- ✅ **Đẹp**: Markdown render như ChatGPT/Claude
- ✅ **Thông minh**: AI hiểu ngữ cảnh và đưa ra gợi ý phù hợp
- ✅ **Maintainable**: Không cần maintain nhiều template
- ✅ **Professional**: Code blocks, bold, lists render chuẩn

---

## 🔧 Implementation

### 1. Backend - AI Generation với Markdown

**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`

#### A. BuildUserFacingMessageAsync - Ưu tiên AI
```csharp
private async Task<string> BuildUserFacingMessageAsync(
    ForbiddenOperationResult result,
    bool isVietnamese,
    CancellationToken cancellationToken)
{
    // Ưu tiên dùng AI nếu có
    if (_llmClient != null)
    {
        try
        {
            _logger.LogDebug("[ForbiddenPipeline] Generating AI message with markdown");
            return await GenerateAIMessageAsync(result, isVietnamese, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForbiddenPipeline] AI generation failed, using fallback template");
        }
    }

    // Fallback: Dùng template cứng nếu AI không available
    _logger.LogDebug("[ForbiddenPipeline] Using static template message");
    var tableName = ExtractTableName(result.OriginalQuestion);
    
    if (!string.IsNullOrEmpty(tableName))
    {
        return ForbiddenMessageTemplates.GetCustomMessage(
            string.Join(", ", result.DetectedPatterns ?? new List<string>()),
            isVietnamese,
            tableName
        );
    }

    return isVietnamese
        ? ForbiddenMessageTemplates.GetVietnameseMessage(...)
        : ForbiddenMessageTemplates.GetEnglishMessage(...);
}
```

#### B. GenerateAIMessageAsync - System Prompt cho Markdown
```csharp
private async Task<string> GenerateAIMessageAsync(
    ForbiddenOperationResult result,
    bool isVietnamese,
    CancellationToken cancellationToken)
{
    var language = isVietnamese ? "Vietnamese" : "English";
    var tableName = ExtractTableName(result.OriginalQuestion);

    var systemPrompt = $@"You are a database security assistant. Generate a clear, helpful warning message.

**CRITICAL RULES:**
1. Use {language} language ONLY
2. Keep it SHORT and actionable (max 250 words)
3. Use markdown for better readability:
   - Use **bold** for important warnings
   - Use ```sql code blocks for SQL examples
   - Use bullet points for lists
   - Use emojis: ⚠️ for warning, 💡 for tips, ✅ for recommendations
4. Focus on WHY it's blocked and provide 2-3 concrete alternatives
5. Make SQL examples specific to the user's context when possible

**Structure:**
1. Clear warning with emoji
2. Brief explanation of the risk
3. 2-3 safe alternatives with SQL examples
4. Helpful tip at the end";

    var userPrompt = $@"Generate a {language} security warning for this blocked operation:

**Original Question:** {result.OriginalQuestion}
**Detected Patterns:** {string.Join(", ", result.DetectedPatterns ?? new List<string>())}
**Table Name:** {(string.IsNullOrEmpty(tableName) ? "unknown" : tableName)}
**Reason:** {result.RejectionReason}

Provide specific, actionable alternatives with real SQL examples.";

    var messages = new List<LLMMessage>
    {
        new() { Role = "system", Content = systemPrompt },
        new() { Role = "user", Content = userPrompt }
    };

    var response = await _llmClient!.CompleteWithSystemPromptAsync(
        messages,
        temperature: 0.7,
        maxTokens: 500,
        cancellationToken: cancellationToken
    );

    return response?.Trim() ?? result.RejectionReason;
}
```

**Key Points:**
- System prompt yêu cầu AI dùng markdown (bold, code blocks, lists)
- User prompt cung cấp context đầy đủ (question, patterns, table name)
- Temperature 0.7 để có sự sáng tạo vừa phải
- MaxTokens 500 để message không quá dài
- Fallback về RejectionReason nếu AI fail

### 2. Frontend - ReactMarkdown Rendering

**File**: `frontend/src/components/forbidden/ForbiddenAlert.jsx`

#### A. Import ReactMarkdown
```javascript
import ReactMarkdown from 'react-markdown';
```

#### B. Decode HTML Entities
```javascript
const decodeHtml = (str) => {
    if (!str) return '';
    const txt = document.createElement("textarea");
    txt.innerHTML = str;
    return txt.value;
};

const userMessage = decodeHtml(result.userFacingMessage || result.rejectionReason || '');
```

**Why?** JSON serialization có thể escape backticks thành `&#96;`, textarea tự động decode.

#### C. ReactMarkdown với Custom Components
```javascript
<ReactMarkdown
    components={{
        // Headings
        h1: ({node, ...props}) => <Title level={3} style={{ marginTop: 16, marginBottom: 8 }} {...props} />,
        h2: ({node, ...props}) => <Title level={4} style={{ marginTop: 12, marginBottom: 6 }} {...props} />,
        h3: ({node, ...props}) => <Title level={5} style={{ marginTop: 8, marginBottom: 4 }} {...props} />,
        
        // Paragraphs
        p: ({node, ...props}) => <p style={{ marginBottom: 12, lineHeight: 1.6 }} {...props} />,
        
        // Code (inline và block)
        code: ({node, inline, ...props}) => 
            inline 
                ? <code style={{ 
                    backgroundColor: '#f5f5f5', 
                    padding: '2px 6px', 
                    borderRadius: '3px', 
                    fontFamily: 'Consolas, Monaco, monospace', 
                    fontSize: '13px' 
                  }} {...props} />
                : <pre style={{ 
                    backgroundColor: '#1e1e1e', 
                    color: '#d4d4d4', 
                    padding: '12px', 
                    borderRadius: '4px', 
                    fontSize: '13px', 
                    fontFamily: 'Consolas, Monaco, monospace', 
                    overflow: 'auto', 
                    marginTop: 8, 
                    marginBottom: 8 
                  }}>
                    <code {...props} />
                  </pre>,
        
        // Lists
        ul: ({node, ...props}) => <ul style={{ marginLeft: 20, marginBottom: 12 }} {...props} />,
        ol: ({node, ...props}) => <ol style={{ marginLeft: 20, marginBottom: 12 }} {...props} />,
        li: ({node, ...props}) => <li style={{ marginBottom: 4 }} {...props} />,
        
        // Bold (màu đỏ để nhấn mạnh warning)
        strong: ({node, ...props}) => <strong style={{ color: '#ff4d4f', fontWeight: 600 }} {...props} />,
        
        // Blockquote
        blockquote: ({node, ...props}) => <blockquote style={{ 
            borderLeft: '4px solid #d9d9d9', 
            paddingLeft: 16, 
            margin: '12px 0', 
            color: '#595959' 
          }} {...props} />
    }}
>
    {userMessage}
</ReactMarkdown>
```

**Custom Styling:**
- **Code blocks**: Dark theme (#1e1e1e) giống VS Code
- **Bold text**: Màu đỏ (#ff4d4f) để nhấn mạnh warning
- **Lists**: Proper spacing và indentation
- **Inline code**: Light gray background
- **Headings**: Ant Design Title với levels phù hợp

---

## 📊 So sánh Before vs After

### Before: Template cứng + Regex strip markdown

**Backend sends:**
```
⚠️ THAO TÁC BỊ CHẶN

Xóa dữ liệu trực tiếp có thể dẫn đến mất mát không thể phục hồi.

• Soft Delete - Đánh dấu bản ghi:
UPDATE customers SET status = 'inactive' WHERE id = 123;
```

**Problems:**
- ❌ Không linh hoạt, không thể điều chỉnh theo context
- ❌ Phải maintain nhiều template
- ❌ Không có markdown formatting
- ❌ Cảm giác máy móc

### After: AI Generation + ReactMarkdown

**Backend sends (AI-generated):**
```markdown
⚠️ **Thao tác bị chặn**

Bạn đang cố gắng xóa dữ liệu từ bảng `customers`. Điều này có thể gây mất mát dữ liệu vĩnh viễn.

**Các phương án thay thế an toàn:**

1. **Soft Delete** - Đánh dấu là đã xóa thay vì xóa thật:
```sql
UPDATE customers SET is_deleted = 1, deleted_at = NOW() WHERE id = 123;
```

2. **Archive** - Chuyển sang bảng lưu trữ:
```sql
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;
DELETE FROM customers WHERE id = 123;
```

💡 **Lưu ý**: Luôn sao lưu dữ liệu trước khi thực hiện bất kỳ thay đổi nào.
```

**Frontend renders:**
- ✅ ⚠️ **Thao tác bị chặn** (bold, màu đỏ)
- ✅ Text với line spacing đẹp
- ✅ **Soft Delete**, **Archive** in đậm màu đỏ
- ✅ SQL code blocks với dark theme
- ✅ 💡 **Lưu ý** in đậm
- ✅ Professional, dễ đọc như ChatGPT

**Benefits:**
- ✅ AI tự điều chỉnh message theo context (table name, operation type)
- ✅ Markdown render đẹp, professional
- ✅ Không cần maintain template
- ✅ Linh hoạt, thông minh

---

## 🧪 Testing

### Test Cases

#### 1. Vietnamese - DELETE operation
**Input:**
```
Xóa khách hàng có id = 123
```

**Expected AI Output:**
```markdown
⚠️ **Thao tác bị chặn**

Bạn đang cố gắng xóa dữ liệu từ bảng `customers`. Điều này có thể gây mất mát dữ liệu vĩnh viễn.

**Các phương án thay thế:**

1. **Soft Delete**:
```sql
UPDATE customers SET is_deleted = 1 WHERE id = 123;
```

2. **Archive**:
```sql
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;
```

💡 Luôn backup trước khi thay đổi.
```

#### 2. English - DROP TABLE operation
**Input:**
```
DROP TABLE users
```

**Expected AI Output:**
```markdown
⚠️ **Operation Blocked**

You are attempting to drop the entire `users` table. This will permanently delete all user data.

**Safe alternatives:**

1. **Rename table** (preserve data):
```sql
ALTER TABLE users RENAME TO archived_users;
```

2. **Truncate** (keep structure):
```sql
TRUNCATE TABLE users;
```

💡 Always backup before destructive operations.
```

#### 3. Fallback - AI unavailable
**Expected:**
- Sử dụng `ForbiddenMessageTemplates.GetCustomMessage()`
- Plain text format (không có markdown)
- Vẫn hiển thị được, nhưng không đẹp bằng

### Manual Testing Steps
1. Start backend và frontend
2. Trigger DELETE operation: `DELETE FROM customers WHERE id = 1`
3. Check ForbiddenAlert modal
4. Verify:
   - ✅ Markdown renders correctly (bold, code blocks, lists)
   - ✅ SQL code has dark theme
   - ✅ No raw `**` or ` ``` ` visible
   - ✅ Emojis display correctly
   - ✅ Message is contextual (mentions table name)

---

## 🔒 Security & Performance

### Security
- ReactMarkdown tự động sanitize HTML
- Không cho phép dangerous tags (`<script>`, `<iframe>`)
- Safe để render AI-generated content

### Performance
- ReactMarkdown lightweight (~50KB)
- AI generation: ~500-1000ms (acceptable cho blocking operation)
- Fallback template: instant (nếu AI fail)

### Error Handling
```csharp
try
{
    return await GenerateAIMessageAsync(...);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "AI generation failed, using fallback template");
    // Fallback to static template
}
```

---

## 📝 Files Changed

### Backend
- ✅ `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`
  - Added `GenerateAIMessageAsync()` method
  - Updated `BuildUserFacingMessageAsync()` to prioritize AI
  - Fallback to template if AI unavailable

### Frontend
- ✅ `frontend/src/components/forbidden/ForbiddenAlert.jsx`
  - Import ReactMarkdown
  - Added `decodeHtml()` function
  - Custom markdown components with styling
  - Removed line-by-line rendering logic

### Dependencies
- ✅ `react-markdown` already installed in `frontend/package.json`

---

## 🎯 Result

**Approach**: AI Generation (Backend) + ReactMarkdown Rendering (Frontend)

**Benefits:**
1. ✅ **Linh hoạt**: AI tự điều chỉnh message theo context
2. ✅ **Đẹp**: Markdown render professional như ChatGPT
3. ✅ **Thông minh**: AI hiểu ngữ cảnh và đưa ra gợi ý phù hợp
4. ✅ **Maintainable**: Không cần maintain nhiều template
5. ✅ **Reliable**: Có fallback nếu AI fail

**Status**: ✅ IMPLEMENTED

---

## 💡 Best Practices

### Backend - AI Prompt Design
1. **Clear instructions**: Yêu cầu AI dùng markdown format cụ thể
2. **Context-rich**: Cung cấp đầy đủ context (question, patterns, table name)
3. **Structured output**: Hướng dẫn AI về structure (warning → explanation → alternatives → tip)
4. **Language-specific**: Chỉ định ngôn ngữ rõ ràng
5. **Token limit**: Giới hạn maxTokens để message không quá dài

### Frontend - Markdown Rendering
1. **Decode HTML entities**: Luôn decode trước khi render
2. **Custom styling**: Tùy chỉnh style cho từng markdown element
3. **Dark theme code**: Code blocks dùng dark theme để dễ đọc
4. **Emphasis colors**: Bold text dùng màu đỏ để nhấn mạnh warning
5. **Proper spacing**: Margin và padding hợp lý cho readability

---

## 🚀 Future Enhancements

1. **Syntax highlighting**: Add `react-syntax-highlighter` cho SQL code
2. **Copy button**: Thêm button copy cho code blocks
3. **Markdown tables**: Support tables nếu AI cần so sánh alternatives
4. **Collapsible sections**: Collapse/expand cho long messages
5. **Localization**: Support thêm ngôn ngữ khác (English, Vietnamese đã có)

---

**Conclusion**: Giải pháp này kết hợp tốt nhất giữa sức mạnh của AI (linh hoạt, thông minh) và khả năng render đẹp của ReactMarkdown (professional, dễ đọc). Không còn template cứng, không còn regex phức tạp!
