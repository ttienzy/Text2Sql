# Write Pipeline Optimization - Test Plan

## 🎯 Objective

Verify that Write Pipeline optimization reduces latency from 12-38s to 7-25s (35-40% improvement) by eliminating redundant LLM calls.

## 📋 Pre-Test Checklist

- [ ] Backend build successful (0 errors)
- [ ] Docker containers running
- [ ] Database connection active
- [ ] Test database has sample tables (Customers, Orders, Products)
- [ ] Bearer token available
- [ ] Connection ID available

## 🧪 Test Cases

### TEST 1: Simple INSERT (English)

**Query:** "Add a new customer named John Smith"

**Expected behavior:**
- ✅ IntentClassifier extracts entity: `["customer"]`
- ✅ WritePipeline uses PreResolvedEntities (no LLM call #2)
- ✅ Direct match: `customer` → `Customers` table
- ✅ GenerateSQL with compact prompt (LLM call #2 only)
- ✅ Total duration: <8s

**Logs to verify:**
```
[IntentClassifier] Rule-based entity extraction: [customer] for intent Insert
[WritePipeline] Using pre-resolved entities from IntentClassifier: [customer]
[WritePipeline] Direct match found: 'customer' → 'Customers'
[PERF-SUMMARY] Duration=7200ms, Success=True
```

**Should NOT see:**
```
[WritePipeline] ⚠️ Using fallback table identification
```

---

### TEST 2: Simple INSERT (Vietnamese)

**Query:** "Thêm khách hàng tên Nguyễn Văn A"

**Expected behavior:**
- ✅ IntentClassifier extracts entity: `["khachhang"]`
- ✅ Semantic resolution: `khachhang` → `Customers`
- ✅ Total duration: <8s

**Logs to verify:**
```
[IntentClassifier] Rule-based entity extraction: [khachhang] for intent Insert
[WritePipeline] Using pre-resolved entities from IntentClassifier: [khachhang]
[WritePipeline] Semantic resolution: 'khachhang' → 'Customers'
```

---

### TEST 3: UPDATE with WHERE

**Query:** "Update customer with ID 123 set email to test@example.com"

**Expected behavior:**
- ✅ IntentClassifier extracts entity: `["customer"]`
- ✅ WritePipeline uses PreResolvedEntities
- ✅ SQL includes WHERE clause
- ✅ Validation passes
- ✅ Total duration: <10s

**Logs to verify:**
```
[WritePipeline] Using pre-resolved entities from IntentClassifier: [customer]
[WritePipeline] Generated SQL: UPDATE Customers SET Email = N'test@example.com' WHERE CustomerID = 123
[PERF-SUMMARY] Duration=9500ms, Success=True
```

---

### TEST 4: DELETE with WHERE

**Query:** "Delete customer with ID 123"

**Expected behavior:**
- ✅ IntentClassifier classifies as DML_DELETE (not FORBIDDEN)
- ✅ WritePipeline uses PreResolvedEntities
- ✅ SQL includes WHERE clause
- ✅ Risk level: CRITICAL
- ✅ Requires confirmation
- ✅ Total duration: <10s

**Logs to verify:**
```
[IntentClassifier] Rule-based entity extraction: [customer] for intent Delete
[WritePipeline] Using pre-resolved entities from IntentClassifier: [customer]
[WritePipeline] Generated SQL: DELETE FROM Customers WHERE CustomerID = 123
```

---

### TEST 5: Complex INSERT with FK

**Query:** "Add a new order for customer John Smith with product Laptop"

**Expected behavior:**
- ✅ IntentClassifier extracts entities: `["order", "customer", "product"]`
- ✅ WritePipeline uses PreResolvedEntities
- ✅ Multi-table context includes FK relationships
- ✅ SQL includes FK lookups
- ✅ Total duration: <15s (slightly longer due to FK complexity)

**Logs to verify:**
```
[WritePipeline] Using pre-resolved entities from IntentClassifier: [order, customer, product]
[WritePipeline] Direct match found: 'order' → 'Orders'
```

---

## 📊 Performance Metrics

### Before Optimization

| Operation | Duration | LLM Calls | Notes |
|-----------|----------|-----------|-------|
| Simple INSERT | 12-20s | 3 | IntentClassifier + IdentifyTable + GenerateSQL |
| UPDATE | 15-25s | 3 | Same as above |
| DELETE | 15-25s | 3 | Same as above |

### After Optimization (Target)

| Operation | Duration | LLM Calls | Notes |
|-----------|----------|-----------|-------|
| Simple INSERT | 7-10s | 2 | IntentClassifier + GenerateSQL (compact) |
| UPDATE | 8-12s | 2 | Same as above |
| DELETE | 8-12s | 2 | Same as above |

### Improvement

- **Time saved:** 5-13s per request (35-40% faster)
- **LLM calls reduced:** 3 → 2 (33% reduction)
- **Token usage reduced:** ~200 tokens per request
- **Cost saved:** ~$0.006 per request (gpt-4o pricing)

---

## 🔍 Monitoring Commands

### Monitor logs in real-time

```powershell
# Option 1: Docker logs with filtering
docker logs -f texttosqlagent-api 2>&1 | Select-String -Pattern "WritePipeline|PERF-SUMMARY|IntentClassifier"

# Option 2: File logs
Get-Content -Path "logs/app.log" -Wait -Tail 100 | Select-String -Pattern "WritePipeline|PERF-SUMMARY"

# Option 3: Specific patterns
docker logs -f texttosqlagent-api 2>&1 | Select-String -Pattern "Using pre-resolved|Direct match|fallback|PERF-SUMMARY"
```

### Run automated tests

```powershell
# Set environment variables
$env:BEARER_TOKEN = "your-token-here"
$env:CONNECTION_ID = "your-connection-id"

# Run test script
.\test-write-optimization.ps1
```

---

## ✅ Success Criteria

### Must Have (P0)

- [ ] All test cases complete in expected time
- [ ] Logs show "Using pre-resolved entities" for all tests
- [ ] NO "Using fallback table identification" warnings
- [ ] PERF-SUMMARY shows <10s for simple operations
- [ ] Frontend receives response correctly
- [ ] Confirmation modal appears for write operations

### Should Have (P1)

- [ ] Token usage reduced by ~200 tokens per request
- [ ] No regression in SQL quality
- [ ] Entity extraction accuracy >95%
- [ ] Semantic resolution works for Vietnamese entities

### Nice to Have (P2)

- [ ] Performance metrics logged to monitoring system
- [ ] Dashboard shows improvement trends
- [ ] A/B test shows user satisfaction improvement

---

## 🐛 Troubleshooting

### Issue: Still seeing "Using fallback table identification"

**Cause:** PreResolvedEntities not being passed correctly

**Fix:**
1. Check IntentClassifier is extracting entities: `result.DetectedEntities`
2. Verify EnhancedAgentOrchestrator passes entities: `PreResolvedEntities = intentResult.DetectedEntities`
3. Check WritePipeline receives entities: `request.PreResolvedEntities?.Any()`

### Issue: Duration still >15s for simple INSERT

**Cause:** LLM latency or network issues

**Fix:**
1. Check LLM provider status
2. Verify compact prompt is being used (80 tokens vs 180)
3. Check network latency to LLM API
4. Consider caching LLM responses

### Issue: Entity extraction fails for custom table names

**Cause:** ExtractEntitiesSimple doesn't recognize custom entities

**Fix:**
1. Add custom entities to fallback list in IntentClassifier
2. Use semantic resolver for fuzzy matching
3. Consider LLM-based entity extraction for complex cases

---

## 📝 Test Execution Log

### Test Run: [Date/Time]

| Test | Duration | Status | Notes |
|------|----------|--------|-------|
| Simple INSERT (EN) | ___ s | ⬜ | |
| Simple INSERT (VI) | ___ s | ⬜ | |
| UPDATE with WHERE | ___ s | ⬜ | |
| DELETE with WHERE | ___ s | ⬜ | |
| Complex INSERT FK | ___ s | ⬜ | |

**Overall Result:** ⬜ PASS / ⬜ FAIL

**Notes:**
- 
- 
- 

---

## 🚀 Next Steps

### If tests pass:
1. ✅ Mark optimization as complete
2. ✅ Update documentation
3. ✅ Deploy to staging
4. ✅ Monitor production metrics
5. ✅ Consider Phase 4 (caching) if needed

### If tests fail:
1. ❌ Review logs for root cause
2. ❌ Check entity extraction accuracy
3. ❌ Verify PreResolvedEntities flow
4. ❌ Debug LLM prompt changes
5. ❌ Rollback if critical issues

---

**Test Plan Version:** 1.0  
**Last Updated:** 2024-01-XX  
**Owner:** Backend Team  
**Status:** 🟡 Ready for Testing
