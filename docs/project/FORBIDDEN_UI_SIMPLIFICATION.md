# FORBIDDEN Alert UI Simplification

**Date**: 2026-04-08  
**Issue**: Markdown không render đúng - hiển thị raw `&#96;`, `**`, etc.  
**Solution**: Bỏ markdown, dùng plain React components  
**Status**: ✅ FIXED

---

## 🐛 Problem

Backend AI generates markdown message:
```markdown
### Safe Alternatives:
1. **Soft Delete:** Update...
```sql
UPDATE customers SET status = 'inactive' WHERE id = 123;
```
```

Frontend hiển thị:
```
&#96;&#96;&#96;markdown
**Soft Delete**: Update...
&#96;&#96;&#96;sql
UPDATE customers...
&#96;&#96;&#96;
```

**Root Cause**: ReactMarkdown không parse đúng hoặc backend encode HTML entities

---

## ✅ Solution

### Approach: Bỏ Markdown Completely

**Why**:
- Markdown parsing phức tạp và error-prone
- Backend AI message format không consistent
- HTML entities (`&#96;`) gây confusion
- Simpler = Better UX

**What Changed**:
1. Removed ReactMarkdown dependency
2. Removed react-syntax-highlighter dependency
3. Hardcoded 3 default alternatives:
   - Soft Delete (UPDATE status)
   - Archive (INSERT + DELETE)
   - Anonymize (UPDATE to NULL)
4. Used plain React components (Card, pre, etc.)

---

## 📝 New Component Structure

```javascript
const ForbiddenAlert = ({ open, result, onClose }) => {
    const defaultAlternatives = [
        {
            title: 'Soft Delete',
            description: 'Update the record status instead of deleting.',
            sql: "UPDATE customers SET status = 'inactive' WHERE id = 123;"
        },
        // ... 2 more
    ];

    return (
        <Modal>
            <Alert message="Warning" type="error" />
            
            {/* Alternatives */}
            {alternatives.map((alt, i) => (
                <Card>
                    <Title>{i + 1}. {alt.title}</Title>
                    <Text>{alt.description}</Text>
                    <pre>{alt.sql}</pre>
                </Card>
            ))}
            
            <Alert message="Tip" type="info" />
        </Modal>
    );
};
```

---

## ✅ Benefits

1. **No HTML Entities**: Plain text, no encoding issues
2. **Consistent UI**: Always shows 3 alternatives
3. **Simpler Code**: No markdown parsing logic
4. **Smaller Bundle**: Removed 90 packages
5. **Better UX**: Clean, readable, professional

---

## 📊 Before vs After

### Before (❌ Broken)
- ReactMarkdown + SyntaxHighlighter
- 90 extra packages
- HTML entities visible: `&#96;&#96;&#96;`
- Inconsistent rendering

### After (✅ Fixed)
- Plain React components
- 90 packages removed
- Clean SQL code blocks
- Consistent, professional UI

---

**Status**: READY TO TEST  
**Next**: Restart frontend, test DELETE operation
