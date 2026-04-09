# Forbidden Message Markdown Rendering Fix

**Date**: 2026-04-08  
**Status**: ✅ FIXED  
**Issue**: Markdown không render đúng - hiển thị raw `&#96;`, `**`, etc.  
**Solution**: Sử dụng ReactMarkdown để render AI-generated message

---

## 🔍 Root Cause Analysis

### 1. Backend (Dữ liệu gốc)
Backend AI tạo message với Markdown format đúng chuẩn:
```markdown
⚠️ **Thao tác bị chặn**

Bạn đang cố gắng xóa dữ liệu. Đây là các phương án thay thế:

```sql
UPDATE users SET is_deleted = TRUE WHERE id = 1;
```

**Lưu ý**: Sử dụng soft delete thay vì xóa vĩnh viễn.
```

Backend trả về message này qua JSON response:
- `ForbiddenOperationResult.UserFacingMessage` chứa markdown text
- JSON serialization có thể escape một số ký tự đặc biệt

### 2. Frontend (Hiển thị)
**Vấn đề cũ:**
- Component `ForbiddenAlert` chỉ hiển thị plain text
- Không parse markdown → hiển thị raw `**`, ` ``` `, etc.
- Nếu có HTML entities (`&#96;`) từ JSON → hiển thị luôn entities

**Giải pháp mới:**
- Sử dụng `react-markdown` để render markdown
- Decode HTML entities trước khi render
- Custom styling cho code blocks, headings, lists, etc.

---

## ✅ Solution Implementation

### 1. Frontend Changes

#### A. Install Dependencies
```bash
npm install react-markdown
```

#### B. Update ForbiddenAlert Component
**File**: `frontend/src/components/forbidden/ForbiddenAlert.jsx`

**Key Changes:**
1. Import ReactMarkdown
2. Decode HTML entities function
3. Custom markdown components with styling

```jsx
import ReactMarkdown from 'react-markdown';

const ForbiddenAlert = ({ open, result, onClose }) => {
    // Decode HTML entities if Backend sends them
    const decodeHtml = (str) => {
        if (!str) return '';
        const txt = document.createElement("textarea");
        txt.innerHTML = str;
        return txt.value;
    };

    const userMessage = decodeHtml(
        result.userFacingMessage || result.rejectionReason || ''
    );

    return (
        <Modal>
            <ReactMarkdown
                components={{
                    h1: ({node, ...props}) => <Title level={3} {...props} />,
                    h2: ({node, ...props}) => <Title level={4} {...props} />,
                    code: ({node, inline, ...props}) => (
                        inline 
                            ? <code style={{ /* inline code */ }} {...props} />
                            : <pre style={{ /* code block */ }}>
                                <code {...props} />
                              </pre>
                    ),
                    // ... more custom components
                }}
            >
                {userMessage}
            </ReactMarkdown>
        </Modal>
    );
};
```

### 2. Backend - No Changes Needed
Backend đã trả về markdown đúng format. Không cần thay đổi gì.

**File**: `TextToSqlAgent.Application/Pipelines/Forbidden/ForbiddenPipeline.cs`
- `GenerateAIMessageAsync()` tạo markdown message
- `CompleteWithSystemPromptAsync()` trả về raw markdown
- JSON serialization tự động xử lý escape

---

## 🎨 Markdown Styling

### Custom Components
ReactMarkdown cho phép custom styling cho từng element:

| Element | Custom Style |
|---------|-------------|
| `h1`, `h2`, `h3` | Ant Design Title với level tương ứng |
| `code` (inline) | Background #f5f5f5, padding, border-radius |
| `code` (block) | Dark theme (#1e1e1e), syntax highlighting style |
| `ul`, `ol` | Margin left 20px, spacing |
| `li` | Margin bottom 4px |
| `strong` | Color #ff4d4f (error red) |
| `blockquote` | Border left, padding, gray color |

### Example Output
**Input (Markdown):**
```markdown
⚠️ **Thao tác bị chặn**

**1. Soft Delete**
```sql
UPDATE customers SET status = 'deleted';
```
```

**Output (Rendered):**
- ⚠️ với **Thao tác bị chặn** màu đỏ
- **1. Soft Delete** in đậm
- Code block SQL với dark theme, syntax highlighting

---

## 🧪 Testing

### Test File
**File**: `frontend/test-forbidden-markdown.html`

Test cases:
1. **With HTML Entities**: Backend escapes backticks → `&#96;`
2. **Clean Markdown**: Backend sends raw backticks → ` ``` `
3. **English Version**: Test với tiếng Anh

### How to Test
1. Open `frontend/test-forbidden-markdown.html` in browser
2. Check console logs for decoded messages
3. Verify markdown renders correctly

### Manual Testing
1. Trigger forbidden operation: `DELETE FROM customers WHERE id = 1`
2. Check ForbiddenAlert modal
3. Verify:
   - ✅ Markdown renders (bold, code blocks, lists)
   - ✅ SQL code has dark theme
   - ✅ No raw `&#96;` or `**` visible
   - ✅ Emojis display correctly

---

## 📊 Before vs After

### Before (Plain Text)
```
⚠️ **Thao tác bị chặn**

Bạn đang cố gắng xóa dữ liệu. Đây là các phương án thay thế:

&#96;&#96;&#96;sql
UPDATE users SET is_deleted = TRUE WHERE id = 1;
&#96;&#96;&#96;

**Lưu ý**: Sử dụng soft delete thay vì xóa vĩnh viễn.
```
→ Hiển thị raw markdown, không đẹp, khó đọc

### After (Rendered Markdown)
- ⚠️ **Thao tác bị chặn** (bold, màu đỏ)
- Text paragraph với spacing đúng
- ```sql ... ``` render thành code block với dark theme
- **Lưu ý** in đậm màu đỏ
→ Đẹp, dễ đọc, professional

---

## 🔧 Configuration

### ReactMarkdown Props
```jsx
<ReactMarkdown
    components={{
        // Custom component mapping
    }}
>
    {markdownText}
</ReactMarkdown>
```

### HTML Entity Decoding
```javascript
const decodeHtml = (str) => {
    const txt = document.createElement("textarea");
    txt.innerHTML = str;
    return txt.value;
};
```

**Why?**
- JSON serialization có thể escape backticks → `&#96;`
- Textarea decode tự động HTML entities
- Safe và reliable

---

## 🚀 Benefits

1. **Better UX**: Markdown renders đẹp như ChatGPT/Claude
2. **Maintainable**: Backend chỉ cần trả markdown, Frontend tự render
3. **Flexible**: Dễ dàng thay đổi styling qua custom components
4. **Consistent**: Tất cả forbidden messages đều render giống nhau
5. **Professional**: Code blocks với syntax highlighting

---

## 📝 Notes

### Security Considerations
- ReactMarkdown tự động sanitize HTML
- Không cho phép dangerous tags (`<script>`, `<iframe>`)
- Safe để render user-generated content

### Performance
- ReactMarkdown lightweight (~50KB)
- No syntax highlighting library needed (custom CSS)
- Fast rendering

### Future Enhancements
- Add syntax highlighting library (e.g., `react-syntax-highlighter`)
- Support more markdown features (tables, task lists)
- Add copy button for code blocks

---

## ✅ Checklist

- [x] Install `react-markdown`
- [x] Update `ForbiddenAlert.jsx` với ReactMarkdown
- [x] Add HTML entity decoding
- [x] Custom styling cho markdown elements
- [x] Create test file `test-forbidden-markdown.html`
- [x] Test với Vietnamese và English
- [x] Verify no raw markdown visible
- [x] Document changes

---

## 🎯 Result

Forbidden messages bây giờ hiển thị đẹp với:
- ✅ Bold text render đúng
- ✅ Code blocks với dark theme
- ✅ Lists với proper spacing
- ✅ Emojis hiển thị
- ✅ No raw `&#96;` or `**`
- ✅ Professional appearance

**Status**: COMPLETE ✅
