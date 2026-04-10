# JSON Deserialization Error Analysis

## Executive Summary

**Status**: Analysis Complete - Root Causes Identified  
**Date**: 2026-04-09  
**Severity**: CRITICAL (blocks user queries)

Two critical JSON deserialization errors are preventing the system from processing complex user queries like "Calculate customer lifetime value with predictions".

---

## Error 1: IntentAnalysisPlugin - QueryIntent Enum Mismatch (CRITICAL)

### Error Details
```
System.Text.Json.JsonException: The JSON value could not be converted to TextToSqlAgent.Core.Models.QueryIntent.
Path: $.intent | LineNumber: 1 | BytePositionInLine: 21.
```

**Location**: `TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs:47`  
**Caller**: `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs:470`

### Root Cause

The LLM returns a string value in the `$.intent` field that doesn't match any value in the `QueryIntent` enum.

**Example Failures**:
- LLM returns: `"AGGREGATION"` → Enum doesn't have this value
- LLM returns: `"CustomerLifetimeValue"` → Enum doesn't have this value
- LLM returns: `"PREDICTION"` → Enum doesn't have this value

**Valid Enum Values** (from `TextToSqlAgent.Core/Models/IntentAnalysis.cs`):
```csharp
public enum QueryIntent
{
    // SIMPLE INTENTS
    LIST, COUNT, DETAIL, SCHEMA,
    
    // AGGREGATE INTENTS
    AGGREGATE, SUM, AVG, MIN_MAX, TOP_N, GROUP_BY,
    
    // ANALYTICAL INTENTS
    TREND, COMPARISON, RANKING, RUNNING_TOTAL, PERCENTAGE, MOVING_AVERAGE, TOP_PER_GROUP,
    
    // COMPLEX INTENTS
    MULTI_AGGREGATE, NESTED_ANALYSIS, PIVOT, COHORT
}
```

### Why This Happens

1. **LLM Prompt Issue**: The system prompt in `IntentAnalysisPlugin.BuildSystemPrompt()` lists all valid intent types, but the LLM sometimes:
   - Invents new intent types not in the enum
   - Uses generic terms like "AGGREGATION" instead of specific ones
   - Tries to be "helpful" by creating descriptive intent names

2. **No Validation**: The code directly deserializes JSON without checking if the intent value is valid:
```csharp
// Line 47 - NO TRY-CATCH
var intentAnalysis = JsonSerializer.Deserialize<IntentAnalysis>(cleanedResponse, options);
```

3. **Cascading Failure**: When deserialization fails, the entire query processing stops with an exception.

### Affected Files

1. **TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs** (line 47)
   - Direct deserialization without error handling
   - No fallback mechanism

2. **TextToSqlAgent.Core/Models/IntentAnalysis.cs**
   - Defines QueryIntent enum
   - Missing "Unknown" value for fallback

3. **TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs** (line 470)
   - Calls AnalyzeIntentAsync
   - No error handling for deserialization failures

4. **TextToSqlAgent.Application/Pipeline/Stages/SchemaRetrievalStage.cs** (lines 74, 80)
   - Also calls AnalyzeIntentAsync
   - Same vulnerability

5. **TextToSqlAgent.Application/Services/TextToSqlAgentOrchestrator.cs** (lines 183, 199, 419, 435)
   - Multiple call sites
   - All vulnerable to same error

### Impact

- **User Experience**: Query fails with cryptic error message
- **Frequency**: Happens with complex/ambiguous queries
- **Workaround**: None - user must rephrase query
- **Data Loss**: No data loss, but conversation context may be lost

---

## Error 2: SemanticTagGenerator - Invalid JSON Format (WARNING)

### Error Details
```
[ERR] SemanticTagGenerator: Failed to parse response for OrderPromotions
',' is invalid after a single JSON value.

[ERR] SemanticTagGenerator: Failed to parse response for ProductReviews
'{' is invalid after a single JSON value.
```

**Location**: `TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs`  
**Method**: `ParseSemanticTags()`

### Root Cause

The LLM returns malformed JSON:
- Extra commas after JSON objects
- Multiple JSON objects concatenated without array brackets
- Markdown formatting not fully cleaned

**Example Bad Responses**:
```json
{"vietnamese": ["khách hàng"]},  // ← Extra comma
{"vietnamese": ["sản phẩm"]}     // ← Two objects, not an array
```

### Why This Happens

1. **LLM Inconsistency**: The LLM sometimes returns:
   - Multiple JSON objects instead of one
   - Trailing commas
   - Mixed markdown and JSON

2. **Insufficient Cleaning**: The `CleanJsonResponse()` method removes markdown but doesn't handle:
   - Multiple JSON objects
   - Trailing commas
   - Malformed arrays

3. **Graceful Degradation**: The code DOES have error handling:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "[SemanticTags] Failed to parse response for {TableName}", tableName);
    return CreateFallbackTags(table); // ← Falls back to heuristics
}
```

### Affected Files

1. **TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs**
   - `ParseSemanticTags()` method
   - `CleanJsonResponse()` method needs improvement

2. **Prompts/DbExplorer/semantic-tags.skprompt.txt** (if exists)
   - LLM prompt may need clearer JSON format instructions

### Impact

- **User Experience**: Semantic search may be less accurate (falls back to heuristics)
- **Frequency**: Occasional, not blocking
- **Workaround**: System automatically uses fallback tags
- **Severity**: WARNING (not CRITICAL) - system continues to function

---

## User Query Example: "Calculate customer lifetime value with predictions"

### Why This Query Fails

1. **Intent Classification Fails**:
   ```
   [19:11:05] IntentClassifier: LLM classification: Unknown (confidence: 0)
   ```
   - IntentClassifier returns "Unknown" because query is complex
   - System proceeds to IntentAnalysisPlugin for detailed analysis

2. **IntentAnalysisPlugin Fails**:
   ```
   [19:11:10 ERR] IntentAnalysisPlugin: Error analyzing intent for query: 'Calculate customer lifetime value with predictions'
   System.Text.Json.JsonException: The JSON value could not be converted to TextToSqlAgent.Core.Models.QueryIntent.
   ```
   - LLM returns intent like "PREDICTION" or "CUSTOMER_LIFETIME_VALUE"
   - These don't exist in QueryIntent enum
   - Deserialization throws exception

3. **Query Processing Stops**:
   - No fallback mechanism
   - User sees error message
   - Conversation context may be lost

---

## Recommended Fixes

### Fix 1: Add Try-Catch Wrapper in IntentAnalysisPlugin (CRITICAL)

**File**: `TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs`  
**Line**: 47

**Current Code**:
```csharp
var intentAnalysis = JsonSerializer.Deserialize<IntentAnalysis>(cleanedResponse, options);

if (intentAnalysis == null)
{
    throw new InvalidOperationException("Failed to deserialize intent analysis response");
}
```

**Proposed Fix**:
```csharp
IntentAnalysis intentAnalysis;
try
{
    intentAnalysis = JsonSerializer.Deserialize<IntentAnalysis>(cleanedResponse, options);
    
    if (intentAnalysis == null)
    {
        throw new InvalidOperationException("Failed to deserialize intent analysis response");
    }
}
catch (JsonException ex)
{
    _logger.LogWarning(ex, 
        "[IntentAnalysis] Failed to deserialize LLM response, using fallback. Response: {Response}", 
        cleanedResponse.Substring(0, Math.Min(200, cleanedResponse.Length)));
    
    // Fallback to safe defaults
    intentAnalysis = new IntentAnalysis
    {
        Intent = QueryIntent.AGGREGATE, // Generic fallback for complex queries
        Complexity = "Complex",
        Target = string.Empty,
        NeedsClarification = true,
        ClarificationQuestion = "I understand you want to analyze data, but could you rephrase your question to be more specific about which tables and calculations you need?"
    };
}
```

### Fix 2: Add "Unknown" Value to QueryIntent Enum

**File**: `TextToSqlAgent.Core/Models/IntentAnalysis.cs`

**Current Code**:
```csharp
public enum QueryIntent
{
    // SIMPLE INTENTS
    LIST, COUNT, DETAIL, SCHEMA,
    // ... other values
}
```

**Proposed Fix**:
```csharp
public enum QueryIntent
{
    // FALLBACK
    Unknown,  // ← ADD THIS as first value
    
    // SIMPLE INTENTS
    LIST, COUNT, DETAIL, SCHEMA,
    // ... other values
}
```

### Fix 3: Improve LLM Prompt Validation

**File**: `TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs`  
**Method**: `BuildSystemPrompt()`

**Add to prompt**:
```
# CRITICAL RULES FOR INTENT FIELD

The "intent" field MUST be EXACTLY one of these values (case-sensitive):
- LIST, COUNT, DETAIL, SCHEMA
- AGGREGATE, SUM, AVG, MIN_MAX, TOP_N, GROUP_BY
- TREND, COMPARISON, RANKING, RUNNING_TOTAL, PERCENTAGE, MOVING_AVERAGE, TOP_PER_GROUP
- MULTI_AGGREGATE, NESTED_ANALYSIS, PIVOT, COHORT

DO NOT invent new intent types. DO NOT use descriptive names.
If unsure, use AGGREGATE for calculations or MULTI_AGGREGATE for complex analysis.
```

### Fix 4: Improve JSON Cleaning in SemanticTagGenerator

**File**: `TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs`  
**Method**: `CleanJsonResponse()`

**Current Code**:
```csharp
private string CleanJsonResponse(string response)
{
    var cleaned = response.Trim();

    // Remove markdown code blocks
    if (cleaned.StartsWith("```"))
    {
        cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
    }

    // Find JSON block
    var jsonStart = cleaned.IndexOf('{');
    var jsonEnd = cleaned.LastIndexOf('}');

    if (jsonStart >= 0 && jsonEnd > jsonStart)
    {
        cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
    }

    return cleaned;
}
```

**Proposed Fix**:
```csharp
private string CleanJsonResponse(string response)
{
    var cleaned = response.Trim();

    // Remove markdown code blocks
    if (cleaned.StartsWith("```"))
    {
        cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
    }

    // Find JSON block
    var jsonStart = cleaned.IndexOf('{');
    var jsonEnd = cleaned.LastIndexOf('}');

    if (jsonStart >= 0 && jsonEnd > jsonStart)
    {
        cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
    }

    // ✅ NEW: Remove trailing commas before closing braces/brackets
    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @",\s*([}\]])", "$1");
    
    // ✅ NEW: If multiple JSON objects detected, wrap in array
    var objectCount = System.Text.RegularExpressions.Regex.Matches(cleaned, @"}\s*{").Count;
    if (objectCount > 0)
    {
        _logger.LogWarning("[SemanticTags] Multiple JSON objects detected, taking first one only");
        var firstObjectEnd = cleaned.IndexOf('}');
        if (firstObjectEnd > 0)
        {
            cleaned = cleaned.Substring(0, firstObjectEnd + 1);
        }
    }

    return cleaned;
}
```

### Fix 5: Add Validation Before Deserialization

**File**: `TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs`

**Add new method**:
```csharp
private bool IsValidQueryIntent(string intentValue)
{
    return Enum.TryParse<QueryIntent>(intentValue, ignoreCase: true, out _);
}

private string NormalizeIntentValue(string rawIntent)
{
    // Map common LLM mistakes to valid enum values
    var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "AGGREGATION", "AGGREGATE" },
        { "PREDICTION", "MULTI_AGGREGATE" },
        { "CALCULATION", "AGGREGATE" },
        { "ANALYSIS", "MULTI_AGGREGATE" },
        { "STATISTICS", "AGGREGATE" },
        { "REPORT", "MULTI_AGGREGATE" },
        { "CUSTOMER_LIFETIME_VALUE", "MULTI_AGGREGATE" },
        { "FORECAST", "TREND" }
    };
    
    if (mappings.TryGetValue(rawIntent, out var normalized))
    {
        _logger.LogInformation("[IntentAnalysis] Normalized intent '{Raw}' → '{Normalized}'", 
            rawIntent, normalized);
        return normalized;
    }
    
    return rawIntent;
}
```

---

## Implementation Priority

### Phase 1: Critical Fixes (Immediate)
1. ✅ Add try-catch wrapper in IntentAnalysisPlugin (Fix 1)
2. ✅ Add "Unknown" to QueryIntent enum (Fix 2)
3. ✅ Add intent value normalization (Fix 5)

### Phase 2: Improvements (Next Sprint)
4. ⏳ Improve LLM prompt validation (Fix 3)
5. ⏳ Improve JSON cleaning in SemanticTagGenerator (Fix 4)

### Phase 3: Testing
6. ⏳ Add unit tests for error scenarios
7. ⏳ Test with complex queries like "Calculate customer lifetime value"
8. ⏳ Verify fallback mechanisms work correctly

---

## Testing Scenarios

### Test Case 1: Invalid Intent Value
**Input**: Query that causes LLM to return "PREDICTION"  
**Expected**: System falls back to AGGREGATE intent with clarification  
**Verify**: No exception thrown, user gets helpful message

### Test Case 2: Complex Query
**Input**: "Calculate customer lifetime value with predictions"  
**Expected**: System processes query or asks for clarification  
**Verify**: No JSON deserialization error

### Test Case 3: Malformed JSON in SemanticTags
**Input**: Table with complex name causing LLM to return bad JSON  
**Expected**: System falls back to heuristic tags  
**Verify**: Semantic search still works with reduced accuracy

---

## Related Files

### Files to Modify (Critical)
1. `TextToSqlAgent.Plugins/IntentAnalysisPlugin.cs` - Add error handling
2. `TextToSqlAgent.Core/Models/IntentAnalysis.cs` - Add Unknown enum value

### Files to Modify (Improvements)
3. `TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs` - Improve JSON cleaning
4. `Prompts/IntentAnalysis/*.skprompt.txt` - Improve LLM instructions (if exists)

### Files to Review (No Changes Needed)
5. `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs` - Caller, no changes needed
6. `TextToSqlAgent.Application/Pipeline/Stages/SchemaRetrievalStage.cs` - Caller, no changes needed
7. `TextToSqlAgent.Application/Routing/IntentClassifier.cs` - Different classification layer, working correctly

---

## Conclusion

The root cause is clear: **LLM returns intent values that don't match the QueryIntent enum**, causing JSON deserialization to fail. The fix is straightforward: add error handling with fallback to safe defaults.

The secondary issue with SemanticTagGenerator is less critical as it already has fallback mechanisms, but can be improved with better JSON cleaning.

**Next Step**: Implement Phase 1 critical fixes (try-catch wrapper + Unknown enum value).
