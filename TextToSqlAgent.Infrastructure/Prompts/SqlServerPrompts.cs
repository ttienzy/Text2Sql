namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// SQL Server-specific SQL generation prompts
/// </summary>
public static class SqlServerPrompts
{
    public const string SystemPrompt = SqlGenerationPrompt.SystemPrompt;

    public const string SyntaxGuide = @"
# SQL Server Syntax Quick Reference

## Limiting Results
```sql
SELECT TOP 10 * FROM table;
SELECT TOP 10 * FROM table ORDER BY column;
```

## Identifiers
```sql
[table_name], [column_name]
```

## String Functions
```sql
CONCAT(str1, str2) or str1 + str2
SUBSTRING(str, start, length)
UPPER(str), LOWER(str)
LTRIM(RTRIM(str))
```

## Date Functions
```sql
GETDATE(), GETUTCDATE()
DATEADD(DAY, n, date)
DATEDIFF(DAY, date1, date2)
FORMAT(date, 'yyyy-MM-dd')
```

## NULL Handling
```sql
ISNULL(expr, default)
COALESCE(expr1, expr2, ...)
NULLIF(expr1, expr2)
```

## Conditional
```sql
CASE WHEN condition THEN result ELSE default END
IIF(condition, true_value, false_value)
```
";

    public const string ExampleQueries = @"
# SQL Server Example Queries

## Basic SELECT with TOP
```sql
SELECT TOP 10 
    [customer_id], 
    [name], 
    [email]
FROM [customers]
WHERE [status] = 'active'
ORDER BY [created_at] DESC;
```

## JOIN with aggregation
```sql
SELECT TOP 20
    c.[customer_id],
    c.[name],
    COUNT(o.[order_id]) AS order_count,
    ISNULL(SUM(o.[total]), 0) AS total_spent
FROM [customers] c
LEFT JOIN [orders] o ON c.[customer_id] = o.[customer_id]
GROUP BY c.[customer_id], c.[name]
HAVING COUNT(o.[order_id]) > 0
ORDER BY total_spent DESC;
```

## CTE
```sql
WITH monthly_sales AS (
    SELECT 
        FORMAT([order_date], 'yyyy-MM') AS month,
        SUM([total]) AS sales
    FROM [orders]
    WHERE [order_date] >= DATEADD(MONTH, -12, GETDATE())
    GROUP BY FORMAT([order_date], 'yyyy-MM')
)
SELECT * FROM monthly_sales
ORDER BY month DESC;
```

## Window Functions
```sql
SELECT TOP 10
    [product_id],
    [product_name],
    [sales],
    ROW_NUMBER() OVER (ORDER BY [sales] DESC) AS rank,
    SUM([sales]) OVER () AS total_sales
FROM [products]
ORDER BY [sales] DESC;
```
";
}
