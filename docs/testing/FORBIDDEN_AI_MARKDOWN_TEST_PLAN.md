# Forbidden AI + Markdown Test Plan

**Date**: 2026-04-08  
**Feature**: AI-Generated Forbidden Messages with ReactMarkdown Rendering  
**Status**: Ready for Testing

---

## 🎯 Test Objectives

1. Verify AI generates contextual forbidden messages with markdown
2. Verify ReactMarkdown renders markdown correctly in UI
3. Verify fallback to static template when AI unavailable
4. Verify both Vietnamese and English messages work
5. Verify HTML entities are decoded properly

---

## 🔧 Pre-Test Setup

### Backend
1. Ensure `ILLMClient` is properly configured and available
2. Check logs to confirm AI generation is enabled
3. Verify `ForbiddenMessageTemplates` exists as fallback

### Frontend
1. Verify `react-markdown` is installed: `npm list react-markdown`
2. Check browser console for any import errors
3. Clear browser cache to ensure latest code

### Database
Use any test database with tables like `customers`, `users`, `orders`

---

## 📋 Test Cases

### Test Case 1: Vietnamese DELETE with Table Name

**Input:**
```
Xóa khách hàng có id = 123
```

**Expected Backend Behavior:**
- IntentClassifier detects FORBIDDEN intent
- ForbiddenPipeline calls `GenerateAIMessageAsync()`
- AI generates Vietnamese message with markdown
- Message includes:
  - ⚠️ emoji
  - **Bold text** for warnings
  - ```sql code blocks for alternatives
  - 💡 emoji for tips
  - Mentions table name "khách hàng" or "customers"

**Expected Frontend Behavior:**
- ForbiddenAlert modal opens
- Markdown renders correctly:
  - ✅ Bold text is red and bold
  - ✅ SQL code blocks have dark theme
  - ✅ Emojis display
  - ✅ No raw `**` or ` ``` ` visible
  - ✅ Proper spacing and formatting

**How to Test:**
1. Open chat interface
2. Type: `Xóa khách hàng có id = 123`
3. Press Enter
4. Verify modal appears with formatted message
5. Check browser console for any errors
6. Click "I Understand" to close

**Success Criteria:**
- ✅ Modal opens immediately
- ✅ Message is in Vietnamese
- ✅ Markdown renders beautifully
- ✅ No syntax errors in console
- ✅ Table name mentioned in context

---

### Test Case 2: English DELETE with Table Name

**Input:**
```
DELETE FROM users WHERE id = 456
```

**Expected Backend Behavior:**
- Quick-block detects `delete from` pattern
- ForbiddenPipeline generates English message
- AI provides contextual alternatives for `users` table

**Expected Frontend Behavior:**
- Modal shows English message
- Code blocks show SQL alternatives
- Professional appearance

**How to Test:**
1. Type: `DELETE FROM users WHERE id = 456`
2. Verify English message
3. Check SQL alternatives are specific to `users` table

**Success Criteria:**
- ✅ English language used
- ✅ Mentions "users" table
- ✅ Provides 2-3 alternatives
- ✅ SQL examples are contextual

---

### Test Case 3: Vietnamese DROP TABLE

**Input:**
```
Xóa bảng customers
```

**Expected Backend Behavior:**
- Detects `xóa bảng` pattern (FORBIDDEN)
- AI generates message about dropping entire table
- Suggests safer alternatives (rename, archive)

**Expected Frontend Behavior:**
- Modal shows severe warning
- Alternatives focus on preserving data

**How to Test:**
1. Type: `Xóa bảng customers`
2. Verify message emphasizes severity
3. Check alternatives are appropriate for DROP TABLE

**Success Criteria:**
- ✅ Message warns about losing ALL data
- ✅ Suggests RENAME or ARCHIVE
- ✅ No DELETE alternatives (not applicable)

---

### Test Case 4: English TRUNCATE

**Input:**
```
TRUNCATE TABLE orders
```

**Expected Backend Behavior:**
- Detects `truncate` pattern
- AI explains TRUNCATE removes all rows
- Suggests soft delete or archive

**Expected Frontend Behavior:**
- Modal shows clear explanation
- Alternatives preserve data

**How to Test:**
1. Type: `TRUNCATE TABLE orders`
2. Verify message explains TRUNCATE
3. Check alternatives

**Success Criteria:**
- ✅ Explains TRUNCATE vs DELETE
- ✅ Suggests safer options
- ✅ Mentions `orders` table

---

### Test Case 5: Ambiguous Vietnamese Query

**Input:**
```
Xóa tất cả đơn hàng cũ
```

**Expected Backend Behavior:**
- Detects `xóa tất cả` (delete all)
- AI generates contextual message
- Suggests filtering by date instead

**Expected Frontend Behavior:**
- Modal shows helpful alternatives
- Suggests UPDATE with status flag

**How to Test:**
1. Type: `Xóa tất cả đơn hàng cũ`
2. Verify AI understands "old orders" context
3. Check alternatives use date filtering

**Success Criteria:**
- ✅ AI understands "cũ" (old) context
- ✅ Suggests date-based filtering
- ✅ Provides soft delete example

---

### Test Case 6: Fallback to Static Template

**Scenario:** AI service unavailable or fails

**How to Test:**
1. Stop AI service or simulate failure
2. Type: `DELETE FROM customers WHERE id = 1`
3. Verify fallback template is used

**Expected Behavior:**
- Backend catches AI exception
- Falls back to `ForbiddenMessageTemplates.GetCustomMessage()`
- Frontend still displays message (plain text format)

**Success Criteria:**
- ✅ No crash or error
- ✅ Static template displays
- ✅ Message is readable (no markdown)
- ✅ Logs show fallback warning

---

### Test Case 7: HTML Entities Decoding

**Scenario:** Backend sends HTML entities (e.g., `&#96;` for backtick)

**How to Test:**
1. Check network response for forbidden operation
2. Look for HTML entities in `userFacingMessage`
3. Verify frontend decodes them

**Expected Behavior:**
- `decodeHtml()` function converts entities
- ReactMarkdown receives clean text
- No `&#96;` or `&#x27;` visible in UI

**Success Criteria:**
- ✅ No HTML entities visible
- ✅ Backticks render as code blocks
- ✅ Quotes render correctly

---

### Test Case 8: Multiple Patterns Detected

**Input:**
```
DELETE FROM users; DROP TABLE customers;
```

**Expected Backend Behavior:**
- Detects multiple forbidden patterns
- `DetectedPatterns` array has multiple entries
- AI generates comprehensive warning

**Expected Frontend Behavior:**
- Modal shows all detected patterns as tags
- Message addresses multiple operations

**How to Test:**
1. Type: `DELETE FROM users; DROP TABLE customers;`
2. Check "Detected dangerous patterns" section
3. Verify multiple tags appear

**Success Criteria:**
- ✅ Multiple pattern tags visible
- ✅ Message addresses both operations
- ✅ Clear warning about severity

---

### Test Case 9: Long AI Response

**Scenario:** AI generates very long message (>500 words)

**Expected Behavior:**
- Backend limits to maxTokens: 500
- Message is concise but complete
- Frontend scrolls if needed

**How to Test:**
1. Trigger complex forbidden operation
2. Check message length
3. Verify modal is scrollable

**Success Criteria:**
- ✅ Message is under 500 tokens
- ✅ Still provides 2-3 alternatives
- ✅ Modal scrolls if content overflows

---

### Test Case 10: Markdown Edge Cases

**Test various markdown elements:**

**Bold:**
- Input: Message with `**warning**`
- Expected: Red bold text

**Code Inline:**
- Input: Message with `` `column_name` ``
- Expected: Gray background inline code

**Code Block:**
- Input: Message with ` ```sql ... ``` `
- Expected: Dark theme code block

**Lists:**
- Input: Message with `1. First\n2. Second`
- Expected: Numbered list with proper spacing

**Emojis:**
- Input: Message with ⚠️ 💡 ✅
- Expected: Emojis display correctly

**Success Criteria:**
- ✅ All markdown elements render
- ✅ No raw markdown syntax visible
- ✅ Styling matches design

---

## 🔍 Backend Verification

### Check Logs

**Look for these log entries:**

```
[IntentClassifier] FORBIDDEN pattern detected: ...
[ForbiddenPipeline] BLOCKED forbidden operation: ...
[ForbiddenPipeline] Generating AI message with markdown
[ForbiddenPipeline] Rejection complete. Suggested X safe alternatives
```

**If AI fails:**
```
[ForbiddenPipeline] AI generation failed, using fallback template
```

### Check Response Structure

**Network tab → Response:**
```json
{
  "success": false,
  "pipeline": "Forbidden",
  "message": "⚠️ **Thao tác bị chặn**\n\n...",
  "data": {
    "result": {
      "isBlocked": true,
      "originalQuestion": "...",
      "rejectionReason": "...",
      "detectedPatterns": ["delete from"],
      "userFacingMessage": "⚠️ **Thao tác bị chặn**\n\n...",
      "safeAlternatives": [...]
    }
  },
  "error": {
    "code": "FORBIDDEN_OPERATION",
    "message": "..."
  }
}
```

---

## 🎨 Frontend Verification

### Visual Checks

**Modal Appearance:**
- ✅ Title: "Operation Blocked" with red stop icon
- ✅ Pattern tags (if any) with red color
- ✅ Error alert banner
- ✅ Message area with light gray background
- ✅ Info alert at bottom

**Markdown Rendering:**
- ✅ Headings: Ant Design Title components
- ✅ Bold: Red color (#ff4d4f)
- ✅ Code blocks: Dark theme (#1e1e1e)
- ✅ Inline code: Light gray background
- ✅ Lists: Proper indentation
- ✅ Spacing: Comfortable reading

### Browser Console

**Should NOT see:**
- ❌ React errors
- ❌ Import errors for react-markdown
- ❌ Syntax errors
- ❌ Warning about missing props

**Should see:**
- ✅ Clean console (or only info logs)

---

## 🐛 Common Issues & Fixes

### Issue 1: Raw Markdown Visible

**Symptom:** See `**bold**` or ` ```sql ``` ` in UI

**Cause:** ReactMarkdown not rendering

**Fix:**
1. Check `react-markdown` is imported
2. Verify `userMessage` is passed to ReactMarkdown
3. Check browser console for errors

### Issue 2: HTML Entities Visible

**Symptom:** See `&#96;` or `&#x27;` in UI

**Cause:** `decodeHtml()` not working

**Fix:**
1. Verify `decodeHtml()` is called before ReactMarkdown
2. Check `result.userFacingMessage` in network response
3. Test `decodeHtml()` function separately

### Issue 3: AI Not Generating

**Symptom:** Always see static template

**Cause:** AI service unavailable or not configured

**Fix:**
1. Check backend logs for AI errors
2. Verify `ILLMClient` is injected
3. Check AI service configuration
4. Test AI service separately

### Issue 4: Wrong Language

**Symptom:** English message for Vietnamese query

**Cause:** Language detection failed

**Fix:**
1. Check `ContainsVietnamese()` logic
2. Verify Vietnamese keywords list
3. Test with clear Vietnamese keywords

### Issue 5: No Table Name in Message

**Symptom:** Generic message without table context

**Cause:** `ExtractTableName()` failed

**Fix:**
1. Check regex patterns in `ExtractTableName()`
2. Test with different query formats
3. Verify AI receives table name in prompt

---

## ✅ Test Checklist

### Backend Tests
- [ ] AI generates Vietnamese message
- [ ] AI generates English message
- [ ] Fallback to static template works
- [ ] Table name extracted correctly
- [ ] Language detected correctly
- [ ] Logs show correct flow
- [ ] Response structure is correct

### Frontend Tests
- [ ] Modal opens on forbidden operation
- [ ] ReactMarkdown renders bold text
- [ ] ReactMarkdown renders code blocks
- [ ] ReactMarkdown renders lists
- [ ] Emojis display correctly
- [ ] No raw markdown visible
- [ ] HTML entities decoded
- [ ] Pattern tags display
- [ ] Modal closes properly
- [ ] No console errors

### Integration Tests
- [ ] Vietnamese DELETE works end-to-end
- [ ] English DELETE works end-to-end
- [ ] DROP TABLE works
- [ ] TRUNCATE works
- [ ] Multiple patterns detected
- [ ] Fallback works when AI fails
- [ ] Message added to chat after close

### Edge Cases
- [ ] Very long message scrolls
- [ ] Empty table name handled
- [ ] Special characters in query
- [ ] Multiple SQL statements
- [ ] Mixed language query

---

## 📊 Test Results Template

```markdown
## Test Execution Results

**Date:** YYYY-MM-DD
**Tester:** [Name]
**Environment:** [Dev/Staging/Prod]

### Test Case 1: Vietnamese DELETE
- Status: ✅ PASS / ❌ FAIL
- Notes: ...

### Test Case 2: English DELETE
- Status: ✅ PASS / ❌ FAIL
- Notes: ...

[... continue for all test cases ...]

### Issues Found
1. [Issue description]
   - Severity: High/Medium/Low
   - Steps to reproduce: ...
   - Expected: ...
   - Actual: ...

### Overall Assessment
- Total Tests: X
- Passed: Y
- Failed: Z
- Pass Rate: Y/X %

### Recommendation
- [ ] Ready for production
- [ ] Needs fixes
- [ ] Needs more testing
```

---

## 🚀 Quick Test Commands

### Backend
```bash
# Check if AI service is running
curl http://localhost:5000/health

# Check logs
tail -f logs/app.log | grep ForbiddenPipeline
```

### Frontend
```bash
# Verify react-markdown installed
npm list react-markdown

# Check for build errors
npm run build

# Run dev server
npm run dev
```

### Database
```sql
-- Create test table
CREATE TABLE test_customers (
    id INT PRIMARY KEY,
    name VARCHAR(100),
    status VARCHAR(20)
);

-- Test queries
DELETE FROM test_customers WHERE id = 1;
DROP TABLE test_customers;
TRUNCATE TABLE test_customers;
```

---

## 📝 Notes

- Test in both Chrome and Firefox
- Test with different screen sizes
- Test with slow network (throttling)
- Test with AI service down (fallback)
- Test with different database schemas
- Take screenshots of successful renders
- Record video of full flow for documentation

---

**Status**: Ready for comprehensive testing
**Next Steps**: Execute all test cases and document results
