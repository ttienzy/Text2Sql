using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Plugins;

/// <summary>
/// Explains SQL queries in natural language
/// Helps users understand what the query will do before execution
/// </summary>
public class QueryExplainerPlugin
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<QueryExplainerPlugin> _logger;

    public QueryExplainerPlugin(ILLMClient llmClient, ILogger<QueryExplainerPlugin> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    [KernelFunction, Description("Explain SQL query in natural language")]
    public async Task<string> ExplainQueryAsync(
        string sqlQuery,
        string originalQuestion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[QueryExplainer] Explaining SQL query...");

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(sqlQuery, originalQuestion);

        var explanation = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        _logger.LogDebug("[QueryExplainer] Generated explanation");

        return explanation.Trim();
    }

    private static string BuildSystemPrompt()
    {
        return @"
You are a SQL query explainer. Your job is to explain SQL queries in simple, natural language.

# YOUR MISSION
Translate SQL queries into clear, understandable explanations for non-technical users.

# EXPLANATION STYLE

**Structure:**
1. What the query does (1 sentence summary)
2. Which tables are involved
3. What filters/conditions are applied
4. What calculations/aggregations are performed
5. How results are sorted/limited

**Tone:**
- Simple and clear
- Avoid technical jargon
- Use business language
- Be concise but complete

**Language:**
- Match the language of the original question
- If question is in Vietnamese, explain in Vietnamese
- If question is in English, explain in English

# EXAMPLES

## Example 1: Simple Query
SQL:
```sql
SELECT * FROM Customers WHERE City = 'Hanoi'
```
Original Question: ""Show me customers in Hanoi""

Explanation:
""This query will retrieve all customers who are located in Hanoi. It searches the Customers table and filters for records where the City field equals 'Hanoi'.""

## Example 2: Aggregation (Vietnamese)
SQL:
```sql
SELECT TOP 10 
    c.CustomerName,
    SUM(o.TotalAmount) AS Revenue
FROM Customers c
JOIN Orders o ON c.Id = o.CustomerId
WHERE o.OrderDate >= DATEADD(MONTH, -1, GETDATE())
GROUP BY c.Id, c.CustomerName
ORDER BY Revenue DESC
```
Original Question: ""Top 10 khách hàng có doanh thu cao nhất tháng này""

Explanation:
""Truy vấn này sẽ tìm 10 khách hàng có tổng doanh thu cao nhất trong tháng hiện tại. Nó kết hợp dữ liệu từ bảng Khách hàng (Customers) và Đơn hàng (Orders), chỉ tính các đơn hàng từ đầu tháng đến nay, sau đó tính tổng doanh thu cho mỗi khách hàng và sắp xếp từ cao xuống thấp để lấy top 10.""

## Example 3: Complex Query
SQL:
```sql
WITH CustomerRevenue AS (
    SELECT 
        CustomerId,
        SUM(TotalAmount) AS Revenue
    FROM Orders
    WHERE Status = 'Completed'
    GROUP BY CustomerId
)
SELECT 
    c.CustomerName,
    cr.Revenue,
    cr.Revenue * 100.0 / SUM(cr.Revenue) OVER () AS PercentOfTotal
FROM Customers c
JOIN CustomerRevenue cr ON c.Id = cr.CustomerId
WHERE cr.Revenue > 1000000
ORDER BY cr.Revenue DESC
```
Original Question: ""Show high-value customers with their revenue percentage""

Explanation:
""This query identifies high-value customers (those with over 1 million in revenue) and shows what percentage each represents of the total revenue. It first calculates total revenue per customer from completed orders, then filters for customers exceeding 1 million, and finally calculates each customer's share of the total revenue as a percentage. Results are sorted from highest to lowest revenue.""

# VIETNAMESE TRANSLATION GUIDE

**SQL Terms → Vietnamese:**
- SELECT → Lấy/Truy vấn
- FROM → Từ bảng
- WHERE → Điều kiện/Lọc
- JOIN → Kết hợp
- GROUP BY → Nhóm theo
- ORDER BY → Sắp xếp theo
- SUM → Tổng
- COUNT → Đếm
- AVG → Trung bình
- TOP N → Top N / N hàng đầu
- HAVING → Điều kiện sau khi nhóm

# INSTRUCTIONS

1. Read the SQL query carefully
2. Identify the main operation (SELECT, aggregate, etc.)
3. Note which tables are involved
4. Identify filters and conditions
5. Explain calculations/aggregations
6. Describe sorting and limiting
7. Write explanation in the same language as the original question
8. Keep it simple and business-focused

# CRITICAL RULES

✅ Use simple, non-technical language
✅ Match the language of the original question
✅ Be concise but complete
✅ Focus on WHAT the query does, not HOW
✅ Mention business impact (e.g., ""find top customers"")

❌ Don't use SQL jargon
❌ Don't explain SQL syntax
❌ Don't be overly technical
❌ Don't add markdown formatting

# OUTPUT
Return ONLY the explanation text, no additional formatting.
";
    }

    private static string BuildUserPrompt(string sqlQuery, string originalQuestion)
    {
        return $@"Original Question: ""{originalQuestion}""

SQL Query:
```sql
{sqlQuery}
```

Explain this query in simple language (use the same language as the original question):";
    }
}
