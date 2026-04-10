using Xunit;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using System.IO;
using System.Linq;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

/// <summary>
/// Unit tests for QueryMetadataVisitor
/// Tests AST traversal and anti-pattern detection
/// </summary>
public class QueryMetadataVisitorTests
{
    private QueryMetadataVisitor ParseQuery(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var tree = parser.Parse(new StringReader(sql), out var errors);

        var visitor = new QueryMetadataVisitor();
        tree.Accept(visitor);

        return visitor;
    }

    [Fact]
    public void Visit_SimpleSelect_ExtractsTableName()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Single(visitor.Tables);
        Assert.Contains("Users", visitor.Tables);
    }

    [Fact]
    public void Visit_JoinQuery_ExtractsMultipleTables()
    {
        // Arrange
        var sql = @"
            SELECT u.Name, o.OrderDate
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Equal(2, visitor.Tables.Count);
        Assert.Contains("Users", visitor.Tables);
        Assert.Contains("Orders", visitor.Tables);
        Assert.Equal(1, visitor.JoinCount);
    }

    [Fact]
    public void Visit_MultipleJoins_CountsCorrectly()
    {
        // Arrange
        var sql = @"
            SELECT u.Name, o.OrderDate, p.ProductName
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId
            LEFT JOIN Products p ON o.ProductId = p.Id";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Equal(3, visitor.Tables.Count);
        Assert.Equal(2, visitor.JoinCount);
    }

    [Fact]
    public void Visit_Subquery_CountsCorrectly()
    {
        // Arrange
        var sql = @"
            SELECT * FROM Users
            WHERE Id IN (SELECT UserId FROM Orders WHERE Status = 'Active')";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Equal(1, visitor.SubqueryCount);
    }

    [Fact]
    public void Visit_CTE_CountsCorrectly()
    {
        // Arrange
        var sql = @"
            WITH ActiveUsers AS (
                SELECT * FROM Users WHERE Status = 'Active'
            )
            SELECT * FROM ActiveUsers";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Equal(1, visitor.CteCount);
    }

    [Fact]
    public void Visit_WindowFunction_CountsCorrectly()
    {
        // Arrange
        var sql = @"
            SELECT 
                Name,
                ROW_NUMBER() OVER (ORDER BY CreatedDate) AS RowNum
            FROM Users";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.Equal(1, visitor.WindowFunctionCount);
    }

    [Fact]
    public void Visit_SelectStar_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-01");
        Assert.NotNull(issue);
        Assert.Equal("SELECT * detected", issue.Title);
    }

    [Fact]
    public void Visit_FunctionInWhere_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Orders WHERE YEAR(OrderDate) = 2024";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-02");
        Assert.NotNull(issue);
        Assert.Contains("non-SARGable", issue.Title);
    }

    [Fact]
    public void Visit_LikeWithLeadingWildcard_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name LIKE '%nguyen%'";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-03");
        Assert.NotNull(issue);
        Assert.Contains("Non-SARGable LIKE", issue.Title);
    }

    [Fact]
    public void Visit_MissingSchemaPrefix_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-13");
        Assert.NotNull(issue);
        Assert.Contains("Missing schema prefix", issue.Title);
    }

    [Fact]
    public void Visit_WithSchemaPrefix_NoAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-13");
        Assert.Null(issue);
    }

    [Fact]
    public void Visit_IsNullInWhere_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE ISNULL(Status, 'Active') = 'Active'";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-15");
        Assert.NotNull(issue);
        Assert.Contains("ISNULL/COALESCE", issue.Title);
    }

    [Fact]
    public void Visit_LargeInList_DetectsAntiPattern()
    {
        // Arrange
        var values = string.Join(",", Enumerable.Range(1, 150).Select(i => i.ToString()));
        var sql = $"SELECT * FROM Users WHERE Id IN ({values})";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-16");
        Assert.NotNull(issue);
        Assert.Contains("Large IN list", issue.Title);
    }

    [Fact]
    public void Visit_CrossJoin_DetectsAntiPattern()
    {
        // Arrange
        var sql = "SELECT * FROM Users CROSS JOIN Orders";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        var issue = visitor.DetectedIssues.FirstOrDefault(i => i.Code == "AP-17");
        Assert.NotNull(issue);
        Assert.Contains("CROSS JOIN", issue.Title);
    }

    [Fact]
    public void CalculateComplexityScore_SimpleQuery_ReturnsLowScore()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Id = 1";

        // Act
        var visitor = ParseQuery(sql);
        var score = visitor.CalculateComplexityScore();

        // Assert
        Assert.True(score <= 5, $"Expected score <= 5, got {score}");
    }

    [Fact]
    public void CalculateComplexityScore_ComplexQuery_ReturnsHighScore()
    {
        // Arrange
        var sql = @"
            WITH ActiveUsers AS (
                SELECT * FROM Users WHERE Status = 'Active'
            )
            SELECT 
                u.Name,
                o.OrderDate,
                ROW_NUMBER() OVER (PARTITION BY u.Id ORDER BY o.OrderDate) AS RowNum,
                (SELECT COUNT(*) FROM Orders WHERE UserId = u.Id) AS OrderCount
            FROM ActiveUsers u
            INNER JOIN Orders o ON u.Id = o.UserId
            LEFT JOIN Products p ON o.ProductId = p.Id";

        // Act
        var visitor = ParseQuery(sql);
        var score = visitor.CalculateComplexityScore();

        // Assert
        // Score = JoinCount*2 + SubqueryCount*3 + WindowFunctionCount*4 + CteCount*2 + Tables.Count
        // Expected: 2*2 + 1*3 + 1*4 + 1*2 + 3 = 4 + 3 + 4 + 2 + 3 = 16
        Assert.True(score > 10, $"Expected score > 10, got {score}");
    }

    [Fact]
    public void Visit_MultipleAntiPatterns_DetectsAll()
    {
        // Arrange
        var sql = @"
            SELECT * 
            FROM Users 
            WHERE YEAR(CreatedDate) = 2024 
            AND Name LIKE '%test%'";

        // Act
        var visitor = ParseQuery(sql);

        // Assert
        Assert.True(visitor.DetectedIssues.Count >= 3,
            $"Expected at least 3 issues (AP-01, AP-02, AP-03, AP-13), got {visitor.DetectedIssues.Count}");

        Assert.Contains(visitor.DetectedIssues, i => i.Code == "AP-01"); // SELECT *
        Assert.Contains(visitor.DetectedIssues, i => i.Code == "AP-02"); // YEAR function
        Assert.Contains(visitor.DetectedIssues, i => i.Code == "AP-03"); // LIKE '%...'
        Assert.Contains(visitor.DetectedIssues, i => i.Code == "AP-13"); // Missing schema
    }
}
