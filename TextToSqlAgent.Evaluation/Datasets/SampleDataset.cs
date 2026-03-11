using TextToSqlAgent.Evaluation.Models;

namespace TextToSqlAgent.Evaluation.Datasets;

/// <summary>
/// Sample dataset for initial testing
/// </summary>
public static class SampleDataset
{
    public static List<EvaluationExample> GetExamples()
    {
        return new List<EvaluationExample>
        {
            // Easy: Simple COUNT
            new EvaluationExample
            {
                Id = "easy_001",
                Question = "How many customers are there?",
                DatabaseId = "test_db",
                GroundTruthSql = "SELECT COUNT(*) FROM Customers",
                Difficulty = "Easy",
                RequiredTables = new List<string> { "Customers" },
                RequiredColumns = new List<string> { }
            },
            
            // Easy: Simple SELECT with WHERE
            new EvaluationExample
            {
                Id = "easy_002",
                Question = "List all customers from New York",
                DatabaseId = "test_db",
                GroundTruthSql = "SELECT * FROM Customers WHERE City = 'New York'",
                Difficulty = "Easy",
                RequiredTables = new List<string> { "Customers" },
                RequiredColumns = new List<string> { "City" }
            },
            
            // Medium: JOIN with aggregation
            new EvaluationExample
            {
                Id = "medium_001",
                Question = "What is the total revenue for each customer?",
                DatabaseId = "test_db",
                GroundTruthSql = @"SELECT c.Name, SUM(o.TotalAmount) as TotalRevenue 
                                   FROM Customers c 
                                   JOIN Orders o ON c.Id = o.CustomerId 
                                   GROUP BY c.Id, c.Name",
                Difficulty = "Medium",
                RequiredTables = new List<string> { "Customers", "Orders" },
                RequiredColumns = new List<string> { "Name", "TotalAmount", "CustomerId" }
            },
            
            // Medium: TOP N with date filter
            new EvaluationExample
            {
                Id = "medium_002",
                Question = "Top 10 customers by revenue this month",
                DatabaseId = "test_db",
                GroundTruthSql = @"SELECT TOP 10 c.Name, SUM(o.TotalAmount) as Revenue
                                   FROM Customers c
                                   JOIN Orders o ON c.Id = o.CustomerId
                                   WHERE o.OrderDate >= DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)
                                   GROUP BY c.Id, c.Name
                                   ORDER BY Revenue DESC",
                Difficulty = "Medium",
                RequiredTables = new List<string> { "Customers", "Orders" },
                RequiredColumns = new List<string> { "Name", "TotalAmount", "OrderDate" }
            },
            
            // Hard: Multiple JOINs with complex filter
            new EvaluationExample
            {
                Id = "hard_001",
                Question = "List customers who have orders with products from category 'Electronics' in the last 30 days",
                DatabaseId = "test_db",
                GroundTruthSql = @"SELECT DISTINCT c.Name, c.Email
                                   FROM Customers c
                                   JOIN Orders o ON c.Id = o.CustomerId
                                   JOIN OrderItems oi ON o.Id = oi.OrderId
                                   JOIN Products p ON oi.ProductId = p.Id
                                   JOIN Categories cat ON p.CategoryId = cat.Id
                                   WHERE cat.Name = 'Electronics'
                                   AND o.OrderDate >= DATEADD(DAY, -30, GETDATE())",
                Difficulty = "Hard",
                RequiredTables = new List<string> { "Customers", "Orders", "OrderItems", "Products", "Categories" },
                RequiredColumns = new List<string> { "Name", "Email", "OrderDate", "CategoryId" }
            },
            
            // Hard: Subquery with aggregation
            new EvaluationExample
            {
                Id = "hard_002",
                Question = "Find customers whose total spending is above the average",
                DatabaseId = "test_db",
                GroundTruthSql = @"SELECT c.Name, SUM(o.TotalAmount) as TotalSpending
                                   FROM Customers c
                                   JOIN Orders o ON c.Id = o.CustomerId
                                   GROUP BY c.Id, c.Name
                                   HAVING SUM(o.TotalAmount) > (SELECT AVG(TotalSpending) 
                                                                 FROM (SELECT SUM(TotalAmount) as TotalSpending 
                                                                       FROM Orders 
                                                                       GROUP BY CustomerId) as CustomerTotals)",
                Difficulty = "Hard",
                RequiredTables = new List<string> { "Customers", "Orders" },
                RequiredColumns = new List<string> { "Name", "TotalAmount", "CustomerId" }
            }
        };
    }
}
