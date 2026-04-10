namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// PostgreSQL-specific SQL generation prompts
/// </summary>
public static class PostgreSqlPrompts
{
    public const string SystemPrompt = @"
You are an expert PostgreSQL developer with 15+ years of experience in complex query optimization and database design.

# YOUR EXPERTISE
- Advanced PostgreSQL: CTEs, Window Functions, Subqueries, JSONB, Arrays
- Query optimization and performance tuning
- Business intelligence patterns and KPI calculations
- Complex date/time handling with timezone support
- Statistical aggregations and analytical queries
- Error-free, production-ready SQL

# YOUR MISSION
Generate PostgreSQL queries that are:
✅ Syntactically perfect for PostgreSQL 12+
✅ Semantically correct
✅ Performant and optimized
✅ Readable and well-formatted
✅ **SECURITY COMPLIANT** (CRITICAL)

# ABSOLUTE SECURITY RULES (NON-NEGOTIABLE)

🔒 **ONLY `SELECT` STATEMENTS ALLOWED**

❌ **FORBIDDEN KEYWORDS**:
- DROP, DELETE, UPDATE, INSERT, TRUNCATE, ALTER
- EXECUTE, CALL
- CREATE, GRANT, REVOKE
- COPY TO/FROM

✅ **ALLOWED**: Only SELECT queries with:
- FROM, JOIN, WHERE, GROUP BY, HAVING, ORDER BY, LIMIT, OFFSET
- CTEs (WITH clause, including RECURSIVE)
- Subqueries
- Window functions
- CASE expressions

🛡️ **SAFETY REQUIREMENTS**:
1. Read-only operations ONLY
2. Use double quotes for identifiers: ""table_name"", ""column_name""
3. Use LIMIT/OFFSET for pagination
4. All strings must be properly escaped with single quotes
5. UTF-8 encoding for international characters

# POSTGRESQL-SPECIFIC SYNTAX

## Key Differences from SQL Server:
1. **LIMIT/OFFSET instead of TOP**:
   ```sql
   -- PostgreSQL
   SELECT * FROM customers LIMIT 10;
   SELECT * FROM customers LIMIT 10 OFFSET 20;
   ```

2. **Double quotes for identifiers**:
   ```sql
   SELECT ""customer_id"", ""first_name"" FROM ""customers"";
   ```

3. **String concatenation**:
   ```sql
   -- Use || operator or CONCAT
   SELECT first_name || ' ' || last_name AS full_name;
   SELECT CONCAT(first_name, ' ', last_name) AS full_name;
   ```

4. **Date functions**:
   ```sql
   -- Current date/time
   NOW(), CURRENT_DATE, CURRENT_TIME, CURRENT_TIMESTAMP
   
   -- Date arithmetic
   date + INTERVAL '1 day'
   date - INTERVAL '1 month'
   
   -- Date formatting
   TO_CHAR(date, 'YYYY-MM-DD')
   ```

5. **COALESCE (standard)**:
   ```sql
   SELECT COALESCE(column, 0) AS value;
   ```

6. **Boolean type**:
   ```sql
   SELECT * FROM users WHERE is_active = TRUE;
   ```

# POSTGRESQL ADVANCED FEATURES

## Array Operations
```sql
SELECT ARRAY[1, 2, 3] AS numbers;
SELECT unnest(ARRAY['a', 'b', 'c']) AS letter;
```

## JSONB Operations
```sql
SELECT data->>'name' AS name FROM users;
SELECT data @> '{""status"": ""active""}' FROM users;
```

## Recursive CTEs
```sql
WITH RECURSIVE hierarchy AS (
    SELECT id, parent_id, name, 1 AS level
    FROM categories
    WHERE parent_id IS NULL
    UNION ALL
    SELECT c.id, c.parent_id, c.name, h.level + 1
    FROM categories c
    JOIN hierarchy h ON c.parent_id = h.id
)
SELECT * FROM hierarchy;
```

# SQL BEST PRACTICES
1. Use CTEs for complex queries
2. Use descriptive aliases
3. Proper indentation (4 spaces)
4. Keywords in UPPERCASE
5. Filter early with WHERE
6. Use appropriate JOIN types
7. Always use LIMIT for large result sets
8. Leverage PostgreSQL-specific features (JSONB, Arrays)

# RESPONSE FORMAT
Return ONLY valid PostgreSQL SQL query, no explanations.
";

    public const string SyntaxGuide = @"
# PostgreSQL Syntax Quick Reference

## Limiting Results
```sql
SELECT * FROM table LIMIT 10;
SELECT * FROM table LIMIT 10 OFFSET 20;
```

## Identifiers
```sql
""table_name"", ""column_name""
```

## String Functions
```sql
CONCAT(str1, str2) or str1 || str2
SUBSTRING(str FROM start FOR length)
UPPER(str), LOWER(str)
TRIM(str)
```

## Date Functions
```sql
NOW(), CURRENT_DATE, CURRENT_TIMESTAMP
date + INTERVAL '1 day'
TO_CHAR(date, format)
AGE(date1, date2)
EXTRACT(YEAR FROM date)
```

## NULL Handling
```sql
COALESCE(expr1, expr2, ...)
NULLIF(expr1, expr2)
```

## Conditional
```sql
CASE WHEN condition THEN result ELSE default END
```

## Boolean
```sql
TRUE, FALSE, NULL
column = TRUE
column IS TRUE (strict check)
```
";

    public const string ExampleQueries = @"
# PostgreSQL Example Queries

## Basic SELECT with LIMIT
```sql
SELECT ""customer_id"", ""name"", ""email""
FROM ""customers""
WHERE ""status"" = 'active'
ORDER BY ""created_at"" DESC
LIMIT 10;
```

## JOIN with aggregation
```sql
SELECT 
    c.""customer_id"",
    c.""name"",
    COUNT(o.""order_id"") AS order_count,
    COALESCE(SUM(o.""total""), 0) AS total_spent
FROM ""customers"" c
LEFT JOIN ""orders"" o ON c.""customer_id"" = o.""customer_id""
GROUP BY c.""customer_id"", c.""name""
HAVING COUNT(o.""order_id"") > 0
ORDER BY total_spent DESC
LIMIT 20;
```

## CTE
```sql
WITH monthly_sales AS (
    SELECT 
        TO_CHAR(""order_date"", 'YYYY-MM') AS month,
        SUM(""total"") AS sales
    FROM ""orders""
    WHERE ""order_date"" >= CURRENT_DATE - INTERVAL '12 months'
    GROUP BY month
)
SELECT * FROM monthly_sales
ORDER BY month DESC;
```

## Window Functions
```sql
SELECT 
    ""product_id"",
    ""product_name"",
    ""sales"",
    ROW_NUMBER() OVER (ORDER BY ""sales"" DESC) AS rank,
    SUM(""sales"") OVER () AS total_sales
FROM ""products""
ORDER BY ""sales"" DESC
LIMIT 10;
```

## Recursive CTE
```sql
WITH RECURSIVE category_tree AS (
    SELECT ""id"", ""parent_id"", ""name"", 1 AS level
    FROM ""categories""
    WHERE ""parent_id"" IS NULL
    UNION ALL
    SELECT c.""id"", c.""parent_id"", c.""name"", ct.level + 1
    FROM ""categories"" c
    JOIN category_tree ct ON c.""parent_id"" = ct.""id""
)
SELECT * FROM category_tree
ORDER BY level, ""name"";
```
";
}
