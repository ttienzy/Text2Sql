namespace TextToSqlAgent.Infrastructure.Prompts;

public static class SqlCorrectionPrompt
{
    public const string SystemPrompt = @"You are an expert SQL debugging assistant.

Your task: Analyze SQL errors and generate corrected SQL queries.

CRITICAL RULES:
1. Carefully analyze the error message
2. Use the provided schema to find the CORRECT column/table names
3. Generate ONLY the corrected SQL, no explanation
4. Maintain the original query logic
5. Only fix the specific error, don't change other parts
6. Use exact column names from schema (case-sensitive)
7. **NEVER use parameterized queries (@variable)**
8. **Always use literal values in WHERE clauses**

Common Error Types:
- Invalid column name → Find correct column in schema
- Invalid table name → Find correct table in schema
- Ambiguous column → Add table alias
- Syntax error → Fix SQL syntax
- Must declare variable → Replace @param with literal value

IMPORTANT - NO PARAMETERS:
❌ BAD:  WHERE [Name] = @Name
✅ GOOD: WHERE [Name] = 'Nguyễn Văn A'

Response Format:
Return ONLY the corrected SQL query, nothing else.";

    public static string BuildUserPrompt(
        string originalSql,
        string errorMessage,
        string errorType,
        string? invalidElement,
        string schemaContext,
        string? filterValue = null)
    {
        var prompt = $@"ORIGINAL SQL (with error):
{originalSql}

ERROR MESSAGE:
{errorMessage}

ERROR TYPE: {errorType}";

        if (!string.IsNullOrEmpty(invalidElement))
        {
            prompt += $@"

INVALID ELEMENT: {invalidElement}";
        }

        prompt += $@"

AVAILABLE SCHEMA:
{schemaContext}";

        // Special handling for parameterized queries
        if (errorMessage.Contains("Must declare the scalar variable", StringComparison.OrdinalIgnoreCase))
        {
            prompt += @"

SPECIFIC FIX NEEDED:
The SQL is using a parameter (@variable). Replace it with the actual literal value.
Example: If WHERE [Name] = @Name, and the filter value is 'Nguyễn Văn A', 
then correct it to: WHERE [Name] = 'Nguyễn Văn A'";

            if (!string.IsNullOrEmpty(filterValue))
            {
                prompt += $@"

FILTER VALUE TO USE: {filterValue}";
            }
        }

        prompt += @"

INSTRUCTIONS:
1. Identify what caused the error
2. Find the CORRECT element in the schema above
3. Replace the invalid element with the correct one
4. DO NOT use parameters (@variable) - use literal values
5. Return ONLY the corrected SQL query

CORRECTED SQL:";

        return prompt;
    }
}