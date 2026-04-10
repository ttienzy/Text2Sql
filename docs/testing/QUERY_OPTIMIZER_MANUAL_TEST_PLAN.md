# Query Optimizer - Manual UI Test Plan

**Date:** 2026-04-09  
**Sprint:** 1 - MVP Testing  
**Status:** 📋 READY FOR TESTING  
**Tester:** [Your Name]

---

## Test Environment Setup

### Prerequisites
- ✅ Backend API running (TextToSqlAgent.API)
- ✅ Frontend running (npm run dev)
- ✅ Database connection configured
- ✅ User authenticated
- ✅ Active connection selected

### Test Data
Prepare these SQL queries for testing:

```sql
-- Test 1: Simple query with SELECT *
SELECT * FROM Users WHERE Id = 1

-- Test 2: Complex query with multiple anti-patterns
SELECT * 
FROM Users 
WHERE YEAR(CreatedDate) = 2024 
AND Name LIKE '%test%'

-- Test 3: Query with JOINs
SELECT u.Name, o.OrderDate
FROM Users u
INNER JOIN Orders o ON u.Id = o.UserId
WHERE u.Status = 'Active'

-- Test 4: Query with CTE
WITH ActiveUsers AS (
    SELECT * FROM Users WHERE Status = 'Active'
)
SELECT * FROM ActiveUsers

-- Test 5: Invalid SQL
SELECT * FROM

-- Test 6: Already optimal query
SELECT Id, Name, Email 
FROM dbo.Users 
WHERE Id = 1
```

---

## Test Cases

### TC-01: Page Access & Navigation ✅

**Objective:** Verify Query Lab page is accessible

**Steps:**
1. Login to application
2. Select a database connection
3. Click "Query Lab" in sidebar menu

**Expected Results:**
- ✅ Query Lab page loads successfully
- ✅ Split editor layout visible (left: Your SQL, right: Optimized SQL)
- ✅ Header shows "Query Lab — SQL Optimizer"
- ✅ Connection name displayed in header
- ✅ No errors in console

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-02: SQL Editor - Basic Input ✅

**Objective:** Verify SQL editor functionality

**Steps:**
1. Navigate to Query Lab
2. Click in left editor (Your SQL)
3. Type: `SELECT * FROM Users`
4. Observe syntax highlighting

**Expected Results:**
- ✅ Monaco Editor loads
- ✅ SQL syntax highlighting works
- ✅ Keywords (SELECT, FROM) highlighted
- ✅ Cursor visible and responsive
- ✅ "Analyze & Optimize" button enabled

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-03: Keyboard Shortcut (Ctrl+Enter) ✅

**Objective:** Verify keyboard shortcut works

**Steps:**
1. Enter SQL in editor: `SELECT * FROM Users`
2. Press Ctrl+Enter

**Expected Results:**
- ✅ Analysis starts immediately
- ✅ Loading spinner appears
- ✅ "Analyzing query..." message shown
- ✅ Buttons disabled during analysis

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-04: Simple Query Analysis ✅

**Objective:** Verify basic optimization workflow

**Steps:**
1. Enter: `SELECT * FROM Users WHERE Id = 1`
2. Click "Analyze & Optimize"
3. Wait for results

**Expected Results:**
- ✅ Loading state shows
- ✅ Right panel shows optimized SQL
- ✅ Bottom panel shows analysis results
- ✅ AP-01 (SELECT *) detected
- ✅ AP-13 (Missing schema) detected
- ✅ Severity badge shown (Critical/Warning)
- ✅ Complexity score displayed
- ✅ Model used displayed (GPT-4o-mini)

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-05: Multiple Anti-Patterns Detection ✅

**Objective:** Verify multiple issues detected

**Steps:**
1. Enter complex query:
```sql
SELECT * 
FROM Users 
WHERE YEAR(CreatedDate) = 2024 
AND Name LIKE '%test%'
```
2. Click "Analyze & Optimize"

**Expected Results:**
- ✅ At least 4 issues detected:
  - AP-01: SELECT *
  - AP-02: YEAR function (non-SARGable)
  - AP-03: LIKE '%...' (non-SARGable)
  - AP-13: Missing schema prefix
- ✅ Each issue shows: Code, Title, Description, Impact, Location
- ✅ Severity badges color-coded (red/orange/yellow)

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-06: Collapsible Sections ✅

**Objective:** Verify analysis result sections

**Steps:**
1. Analyze any query with issues
2. Observe bottom panel
3. Click each section header to collapse/expand

**Expected Results:**
- ✅ "Detected Issues" section visible
- ✅ "Issues Fixed" section visible (if any)
- ✅ "Explanation (Vietnamese)" section visible
- ✅ "Index Suggestions" section visible (if any)
- ✅ All sections collapsible/expandable
- ✅ Default: all sections expanded

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-07: Copy SQL Button ✅

**Objective:** Verify copy functionality

**Steps:**
1. Analyze a query
2. Click "Copy SQL" button in right panel
3. Paste in a text editor (Ctrl+V)

**Expected Results:**
- ✅ Success message: "SQL copied to clipboard"
- ✅ Optimized SQL copied correctly
- ✅ Formatting preserved

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-08: Apply to Chat Integration ✅

**Objective:** Verify navigation to Chat

**Steps:**
1. Analyze a query
2. Click "Apply to Chat" button
3. Observe navigation

**Expected Results:**
- ✅ Navigates to /chat page
- ✅ Optimized SQL included in context
- ✅ Context message visible in chat
- ✅ No errors during navigation

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-09: Clear Functionality ✅

**Objective:** Verify clear buttons work

**Steps:**
1. Enter SQL and analyze
2. Click "Clear" in left panel
3. Observe editor cleared
4. Enter SQL again and analyze
5. Click "Clear" in right panel
6. Observe result cleared

**Expected Results:**
- ✅ Left "Clear" button clears SQL editor
- ✅ Right "Clear" button clears optimized result
- ✅ Bottom analysis panel hidden after right clear
- ✅ Buttons disabled appropriately

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-10: Invalid SQL Handling ✅

**Objective:** Verify error handling

**Steps:**
1. Enter invalid SQL: `SELECT * FROM`
2. Click "Analyze & Optimize"

**Expected Results:**
- ✅ Error message displayed
- ✅ User-friendly error text
- ✅ No application crash
- ✅ Can retry with valid SQL

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-11: No Connection Selected ✅

**Objective:** Verify connection guard

**Steps:**
1. Logout or clear connection
2. Navigate to /query-lab

**Expected Results:**
- ✅ Warning message: "No Connection Selected"
- ✅ Description: "Please select a database connection"
- ✅ Cannot analyze queries
- ✅ Redirected or blocked appropriately

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-12: Empty SQL Validation ✅

**Objective:** Verify input validation

**Steps:**
1. Leave SQL editor empty
2. Try to click "Analyze & Optimize"

**Expected Results:**
- ✅ Button disabled when empty
- ✅ Or warning message: "Please enter a SQL query"
- ✅ No API call made

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-13: Loading States ✅

**Objective:** Verify loading indicators

**Steps:**
1. Enter a complex query
2. Click "Analyze & Optimize"
3. Observe loading states

**Expected Results:**
- ✅ Loading spinner in right panel
- ✅ "Analyzing query..." message
- ✅ "This may take a few seconds" hint
- ✅ Buttons disabled during loading
- ✅ Cannot submit another query while loading

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-14: Index Suggestions with Copy DDL ✅

**Objective:** Verify index suggestions

**Steps:**
1. Analyze query that needs indexes
2. Expand "Index Suggestions" section
3. Click "Copy DDL" button

**Expected Results:**
- ✅ Index suggestions displayed
- ✅ DDL shown in code block
- ✅ "Copy DDL" button visible
- ✅ Click copies DDL to clipboard
- ✅ Success message: "DDL copied to clipboard"

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-15: Vietnamese Explanation ✅

**Objective:** Verify Vietnamese explanation

**Steps:**
1. Analyze any query
2. Expand "Explanation (Vietnamese)" section

**Expected Results:**
- ✅ Explanation in Vietnamese
- ✅ Readable and understandable
- ✅ Explains what was optimized
- ✅ Info alert styling

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-16: Complexity Score Display ✅

**Objective:** Verify complexity metrics

**Steps:**
1. Analyze simple query: `SELECT * FROM Users`
2. Note complexity score
3. Analyze complex query with JOINs, CTEs
4. Note complexity score

**Expected Results:**
- ✅ Simple query: score ≤ 5
- ✅ Complex query: score > 10
- ✅ Score displayed in analysis panel
- ✅ Model selection appropriate (mini for simple, 4o/o3 for complex)

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-17: Already Optimal Query ✅

**Objective:** Verify optimal query handling

**Steps:**
1. Enter optimal query:
```sql
SELECT Id, Name, Email 
FROM dbo.Users 
WHERE Id = 1
```
2. Click "Analyze & Optimize"

**Expected Results:**
- ✅ Analysis completes
- ✅ Message: "Query is already optimal"
- ✅ isChanged = false
- ✅ Explanation why it's optimal
- ✅ No unnecessary changes

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-18: Responsive Layout ✅

**Objective:** Verify layout responsiveness

**Steps:**
1. Resize browser window
2. Test at different widths: 1920px, 1366px, 1024px

**Expected Results:**
- ✅ Split panels adjust appropriately
- ✅ No horizontal scrolling
- ✅ Buttons remain accessible
- ✅ Text readable at all sizes

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-19: Multiple Queries in Session ✅

**Objective:** Verify multiple analyses

**Steps:**
1. Analyze query 1
2. Clear and analyze query 2
3. Clear and analyze query 3

**Expected Results:**
- ✅ Each analysis works independently
- ✅ Previous results cleared properly
- ✅ No memory leaks
- ✅ Performance consistent

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

### TC-20: Browser Console Errors ✅

**Objective:** Verify no console errors

**Steps:**
1. Open browser DevTools (F12)
2. Navigate to Query Lab
3. Perform various actions
4. Check Console tab

**Expected Results:**
- ✅ No JavaScript errors
- ✅ No React warnings
- ✅ No network errors (except expected 401/404)
- ✅ API calls successful (200 OK)

**Status:** [ ] Pass [ ] Fail  
**Notes:** _______________

---

## Performance Testing

### PT-01: Response Time ✅

**Objective:** Verify performance targets

**Test Queries:**
- Simple: `SELECT * FROM Users`
- Medium: Query with 2-3 JOINs
- Complex: Query with CTEs, subqueries, window functions

**Expected Results:**
- ✅ Simple query: < 2s
- ✅ Medium query: < 4s
- ✅ Complex query: < 6s
- ✅ No timeout errors

**Actual Results:**
- Simple: _____ seconds
- Medium: _____ seconds
- Complex: _____ seconds

**Status:** [ ] Pass [ ] Fail

---

### PT-02: Cache Performance ✅

**Objective:** Verify caching works

**Steps:**
1. Analyze query: `SELECT * FROM Users`
2. Note response time
3. Clear result
4. Analyze SAME query again
5. Note response time

**Expected Results:**
- ✅ First request: ~2-5s (LLM call)
- ✅ Second request: < 500ms (cached)
- ✅ Same result returned

**Actual Results:**
- First: _____ seconds
- Second: _____ seconds

**Status:** [ ] Pass [ ] Fail

---

## Cross-Browser Testing

### Browser Compatibility ✅

Test on multiple browsers:

| Browser | Version | Status | Notes |
|---------|---------|--------|-------|
| Chrome | Latest | [ ] Pass [ ] Fail | _____ |
| Firefox | Latest | [ ] Pass [ ] Fail | _____ |
| Edge | Latest | [ ] Pass [ ] Fail | _____ |
| Safari | Latest | [ ] Pass [ ] Fail | _____ |

---

## Bug Report Template

If you find a bug, document it here:

### Bug #1
**Title:** _____________________  
**Severity:** [ ] Critical [ ] High [ ] Medium [ ] Low  
**Steps to Reproduce:**
1. _____
2. _____
3. _____

**Expected:** _____________________  
**Actual:** _____________________  
**Screenshot:** _____________________  
**Console Errors:** _____________________

---

## Test Summary

**Date Tested:** _____________________  
**Tester:** _____________________  
**Environment:** _____________________

### Results
- Total Test Cases: 20
- Passed: _____
- Failed: _____
- Blocked: _____
- Pass Rate: _____%

### Critical Issues Found
1. _____________________
2. _____________________

### Recommendations
- [ ] Ready for production
- [ ] Needs bug fixes
- [ ] Needs improvements

### Sign-off
**Tester:** _____________________ **Date:** _____  
**Developer:** _____________________ **Date:** _____  
**Product Owner:** _____________________ **Date:** _____

---

## Next Steps

After manual testing:
1. Fix any critical bugs
2. Address medium/low priority issues
3. Update documentation
4. Prepare for Sprint 2
5. Plan production deployment

---

**Document Version:** 1.0  
**Last Updated:** 2026-04-09  
**Status:** 📋 READY FOR TESTING
