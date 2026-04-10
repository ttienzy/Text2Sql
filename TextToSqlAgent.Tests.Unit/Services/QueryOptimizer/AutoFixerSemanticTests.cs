using System.Collections.Generic;
using Xunit;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

/// <summary>
/// Semantic validation tests for AutoFixer
/// Tests confidence levels, validation requirements, and semantic risks
/// </summary>
public class AutoFixerSemanticTests
{
    private readonly AutoFixer _fixer;

    public AutoFixerSemanticTests()
    {
        _fixer = new AutoFixer();
    }

    [Fact]
    public void FixOrToIn_NullableColumn_RequiresValidation()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status='A' OR Status='B' OR Status='C'";

        // Act
        var result = _fixer.FixOrToIn(sql);

        // Assert
        Assert.True(result.RequiresSemanticValidation);
        Assert.Equal(ConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.NotEmpty(result.SemanticRisks);
        Assert.Contains("OR", result.SemanticRisks[0], System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixMissingSchemaPrefix_HighConfidence_NoValidationNeeded()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.Equal(ConfidenceLevel.High, result.ConfidenceLevel);
        Assert.False(result.RequiresSemanticValidation);
        Assert.Contains("dbo.Users", result.FixedSql);
    }

    [Fact]
    public void FixSelectStar_GeneratesValidationQuery()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users";
        var schema = new SchemaContext
        {
            Tables = new List<TableSchema>
            {
                new()
                {
                    TableName = "dbo.Users",
                    Columns = new List<ColumnInfo>
                    {
                        new() { ColumnName = "Id", DataType = "int" },
                        new() { ColumnName = "Name", DataType = "nvarchar(100)" },
                        new() { ColumnName = "Email", DataType = "nvarchar(255)" }
                    }
                }
            }
        };

        // Act
        var result = _fixer.FixSelectStar(sql, schema);

        // Assert
        Assert.Equal(ConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.True(result.RequiresSemanticValidation);
        Assert.NotNull(result.ValidationQuery);

        // ValidationQuery should contain EXCEPT pattern for result comparison
        Assert.Contains("EXCEPT", result.ValidationQuery!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanAutoApply_HighConfidenceNoValidation_ReturnsTrue()
    {
        // Arrange
        var result = new AutoFixResult
        {
            ConfidenceLevel = ConfidenceLevel.High,
            RequiresSemanticValidation = false,
            OriginalSql = "SELECT * FROM Users",
            FixedSql = "SELECT * FROM dbo.Users"
        };

        // Act
        var canApply = result.CanAutoApply;

        // Assert
        Assert.True(canApply);
    }

    [Fact]
    public void CanAutoApply_MediumConfidence_ReturnsFalse()
    {
        // Arrange
        var result = new AutoFixResult
        {
            ConfidenceLevel = ConfidenceLevel.Medium,
            RequiresSemanticValidation = true,
            OriginalSql = "SELECT * FROM Users WHERE Status='A' OR Status='B'",
            FixedSql = "SELECT * FROM Users WHERE Status IN ('A', 'B')"
        };

        // Act
        var canApply = result.CanAutoApply;

        // Assert
        Assert.False(canApply);
    }

    [Fact]
    public void CanAutoApply_LowConfidence_ReturnsFalse()
    {
        // Arrange
        var result = new AutoFixResult
        {
            ConfidenceLevel = ConfidenceLevel.Low,
            RequiresSemanticValidation = true,
            OriginalSql = "SELECT * FROM Users",
            FixedSql = "SELECT Id, Name FROM Users"
        };

        // Act
        var canApply = result.CanAutoApply;

        // Assert
        Assert.False(canApply);
    }

    [Fact]
    public void FixNvarcharLiterals_HighConfidence_NoValidation()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = 'John'";

        // Act
        var result = _fixer.FixNvarcharLiterals(sql);

        // Assert
        Assert.Equal(ConfidenceLevel.High, result.ConfidenceLevel);
        Assert.False(result.RequiresSemanticValidation);
        Assert.Contains("N'John'", result.FixedSql);
    }

    [Fact]
    public void FixOrToIn_MultipleColumns_MediumConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status='A' OR Status='B' OR Status='C' OR Status='D'";

        // Act
        var result = _fixer.FixOrToIn(sql);

        // Assert
        Assert.Equal(ConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.True(result.RequiresSemanticValidation);
        Assert.Contains("IN", result.FixedSql);
        Assert.Contains("Status IN", result.FixedSql);
    }

    [Fact]
    public void FixSelectStar_WithWhereClause_PreservesWhereClause()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users WHERE Id = 1";
        var schema = new SchemaContext
        {
            Tables = new List<TableSchema>
            {
                new()
                {
                    TableName = "dbo.Users",
                    Columns = new List<ColumnInfo>
                    {
                        new() { ColumnName = "Id", DataType = "int" },
                        new() { ColumnName = "Name", DataType = "nvarchar(100)" }
                    }
                }
            }
        };

        // Act
        var result = _fixer.FixSelectStar(sql, schema);

        // Assert
        Assert.Contains("WHERE Id = 1", result.FixedSql);
        Assert.DoesNotContain("SELECT *", result.FixedSql);
    }

    [Fact]
    public void AutoFixResult_SemanticRisks_NotEmptyForMediumConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status='A' OR Status='B'";

        // Act
        var result = _fixer.FixOrToIn(sql);

        // Assert
        Assert.NotEmpty(result.SemanticRisks);
        Assert.True(result.SemanticRisks.Count > 0);
    }

    [Fact]
    public void AutoFixResult_Explanation_NotEmpty()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.NotNull(result.Explanation);
        Assert.NotEmpty(result.Explanation);
    }

    [Fact]
    public void FixMissingSchemaPrefix_AlreadyHasSchema_NoChange()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.False(result.IsChanged);
        Assert.Equal(sql, result.FixedSql);
    }

    [Fact]
    public void FixSelectStar_NoSchemaProvided_ReturnsLowConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users";
        var emptySchema = new SchemaContext();

        // Act
        var result = _fixer.FixSelectStar(sql, emptySchema);

        // Assert
        Assert.Equal(ConfidenceLevel.Low, result.ConfidenceLevel);
        Assert.False(result.IsChanged);
    }
}
