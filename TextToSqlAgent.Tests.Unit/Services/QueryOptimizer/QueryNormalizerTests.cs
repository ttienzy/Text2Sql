using Xunit;
using TextToSqlAgent.Application.Services.QueryOptimizer;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

/// <summary>
/// Unit tests for QueryNormalizer
/// Tests query normalization for cache efficiency
/// </summary>
public class QueryNormalizerTests
{
    private readonly QueryNormalizer _normalizer;

    public QueryNormalizerTests()
    {
        _normalizer = new QueryNormalizer();
    }

    [Fact]
    public void NormalizeQuery_SameQueryDifferentSpacing_ReturnsSameHash()
    {
        // Arrange
        var query1 = "SELECT * FROM Users WHERE Id = 1";
        var query2 = "SELECT   *   FROM   Users   WHERE   Id   =   1";
        var query3 = "SELECT * \nFROM Users \nWHERE Id = 1";

        // Act
        var hash1 = _normalizer.GetNormalizedHash(query1);
        var hash2 = _normalizer.GetNormalizedHash(query2);
        var hash3 = _normalizer.GetNormalizedHash(query3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash1, hash3);
    }

    [Fact]
    public void NormalizeQuery_SameQueryDifferentCase_ReturnsSameHash()
    {
        // Arrange
        var query1 = "SELECT * FROM Users WHERE Name = 'John'";
        var query2 = "select * from Users where Name = 'John'";
        var query3 = "SeLeCt * FrOm Users WhErE Name = 'John'";

        // Act
        var hash1 = _normalizer.GetNormalizedHash(query1);
        var hash2 = _normalizer.GetNormalizedHash(query2);
        var hash3 = _normalizer.GetNormalizedHash(query3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash1, hash3);
    }

    [Fact]
    public void NormalizeQuery_DifferentQueries_ReturnsDifferentHashes()
    {
        // Arrange
        var query1 = "SELECT * FROM Users WHERE Id = 1";
        var query2 = "SELECT * FROM Orders WHERE Id = 1";

        // Act
        var hash1 = _normalizer.GetNormalizedHash(query1);
        var hash2 = _normalizer.GetNormalizedHash(query2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void NormalizeQuery_InvalidSQL_ReturnsHashOfOriginal()
    {
        // Arrange
        var invalidQuery = "SELECT * FROM";

        // Act
        var hash = _normalizer.GetNormalizedHash(invalidQuery);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void NormalizeQuery_ComplexQuery_NormalizesSuccessfully()
    {
        // Arrange
        var query1 = @"
            SELECT u.Id, u.Name, o.OrderDate
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId
            WHERE u.Status = 'Active'
            ORDER BY o.OrderDate DESC";

        var query2 = @"SELECT u.Id,u.Name,o.OrderDate FROM Users u INNER JOIN Orders o ON u.Id=o.UserId WHERE u.Status='Active' ORDER BY o.OrderDate DESC";

        // Act
        var hash1 = _normalizer.GetNormalizedHash(query1);
        var hash2 = _normalizer.GetNormalizedHash(query2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void NormalizeQuery_WithCTE_NormalizesSuccessfully()
    {
        // Arrange
        var query1 = @"
            WITH ActiveUsers AS (
                SELECT * FROM Users WHERE Status = 'Active'
            )
            SELECT * FROM ActiveUsers";

        var query2 = "WITH ActiveUsers AS (SELECT * FROM Users WHERE Status = 'Active') SELECT * FROM ActiveUsers";

        // Act
        var hash1 = _normalizer.GetNormalizedHash(query1);
        var hash2 = _normalizer.GetNormalizedHash(query2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void NormalizeQuery_EmptyString_ReturnsHash()
    {
        // Arrange
        var emptyQuery = "";

        // Act
        var hash = _normalizer.GetNormalizedHash(emptyQuery);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void NormalizeQuery_NullString_ReturnsHash()
    {
        // Arrange
        string? nullQuery = null;

        // Act
        var hash = _normalizer.GetNormalizedHash(nullQuery!);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }
}
