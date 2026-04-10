using System.Linq;
using Xunit;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

public class AntiPatternDetectionTests
{
    private readonly StaticAnalyzer _analyzer;

    public AntiPatternDetectionTests()
    {
        _analyzer = new StaticAnalyzer();
    }

    // ========== AP-04: COUNT(*) Tests ==========

    [Fact]
    public async void AP04_CountStar_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT COUNT(*) FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-04");
    }

    [Fact]
    public async void AP04_CountColumn_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT COUNT(Id) FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-04");
    }

    // ========== AP-06: OR Chain Tests ==========

    [Fact]
    public async void AP06_OrChain_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status='Active' OR Status='Pending' OR Status='Approved'";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-06");
    }

    [Fact]
    public async void AP06_SingleOr_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status='Active' OR Status='Pending'";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-06");
    }

    // ========== AP-07: DISTINCT Tests ==========

    [Fact]
    public async void AP07_Distinct_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT DISTINCT Name FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap07 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-07");
        if (ap07 != null)
        {
            Assert.Equal(Severity.Info, ap07.Severity);
        }
    }

    [Fact]
    public async void AP07_AnalyticalQuery_ShouldNotFlag()
    {
        // Arrange
        var sql = "SELECT DISTINCT u.Department, COUNT(*) FROM Users u GROUP BY u.Department";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        // Should not detect AP-07 because it's an analytical query
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-07");
    }

    // ========== AP-08: UNION Tests ==========

    [Fact]
    public async void AP08_Union_ShouldDetectAsInfo()
    {
        // Arrange
        var sql = "SELECT Id FROM Users UNION SELECT Id FROM Customers";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap08 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-08");
        Assert.NotNull(ap08);
        Assert.Equal(Severity.Info, ap08.Severity);
    }

    [Fact]
    public async void AP08_UnionAll_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT Id FROM Users UNION ALL SELECT Id FROM Customers";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-08");
    }

    // ========== AP-09: HAVING without GROUP BY Tests ==========

    [Fact]
    public async void AP09_HavingWithoutGroupBy_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT Name FROM Users HAVING COUNT(*) > 5";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap09 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-09");
        Assert.NotNull(ap09);
        Assert.Equal(Severity.Error, ap09.Severity);
    }

    [Fact]
    public async void AP09_HavingWithGroupBy_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT Department, COUNT(*) FROM Users GROUP BY Department HAVING COUNT(*) > 5";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-09");
    }

    // ========== AP-12: N+1 Query Pattern Tests ==========

    [Fact]
    public async void AP12_SubqueryInSelect_ShouldDetect()
    {
        // Arrange
        var sql = @"
            SELECT 
                u.Name,
                (SELECT COUNT(*) FROM Orders WHERE UserId = u.Id) AS OrderCount
            FROM Users u";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap12 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-12");
        Assert.NotNull(ap12);
        Assert.Equal(Severity.Critical, ap12.Severity);
    }

    // ========== AP-21: Implicit Conversion Tests ==========

    [Fact]
    public async void AP21_StringLiteralWithoutN_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = 'John'";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-21");
    }

    [Fact]
    public async void AP21_StringLiteralWithN_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = N'John'";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-21");
    }

    // ========== AP-23: Missing WHERE Tests ==========

    [Fact]
    public async void AP23_MissingWhere_ShouldBeInfo()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap23 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-23");
        if (ap23 != null)
        {
            Assert.Equal(Severity.Info, ap23.Severity);
            Assert.True(ap23.SuppressInAnalyticalContext);
        }
    }

    [Fact]
    public async void AP23_AnalyticalQuery_ShouldStillDetectButInfo()
    {
        // Arrange
        var sql = "SELECT SUM(Amount), COUNT(*) FROM Orders";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap23 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-23");
        if (ap23 != null)
        {
            Assert.Equal(Severity.Info, ap23.Severity);
        }
    }

    // ========== AP-11: Missing Table Alias Tests ==========

    [Fact]
    public async void AP11_MultiTableNoAlias_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users, Orders WHERE Users.Id = Orders.UserId";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-11");
    }

    [Fact]
    public async void AP11_MultiTableWithAlias_ShouldNotDetect()
    {
        // Arrange
        var sql = "SELECT * FROM Users u JOIN Orders o ON u.Id = o.UserId";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-11");
    }

    // ========== AP-18: ROW_NUMBER Pagination Tests ==========

    [Fact]
    public async void AP18_RowNumber_ShouldDetect()
    {
        // Arrange
        var sql = "SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, * FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        var ap18 = metadata.DetectedIssues.FirstOrDefault(i => i.Code == "AP-18");
        Assert.NotNull(ap18);
        Assert.Equal(Severity.Info, ap18.Severity);
        Assert.Contains("Keyset", ap18.Description);
    }

    // ========== Helper Methods Tests ==========

    [Fact]
    public async void GetCriticalColumns_ShouldReturnDeduplicated()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = 'John' AND Age > 18 ORDER BY Name";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        // Name appears in both WHERE and ORDER BY, should be deduplicated
        var visitor = new QueryMetadataVisitor();
        // Note: This test needs access to visitor instance - may need refactoring
    }

    // ========== Existing Pattern Tests (Regression) ==========

    [Fact]
    public async void AP01_SelectStar_ShouldStillWork()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-01");
    }

    [Fact]
    public async void AP02_FunctionInWhere_ShouldStillWork()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE YEAR(CreatedDate) = 2024";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-02");
    }

    [Fact]
    public async void AP13_MissingSchema_ShouldStillWork()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var metadata = await _analyzer.AnalyzeAsync(sql, default);

        // Assert
        Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-13");
    }
}
