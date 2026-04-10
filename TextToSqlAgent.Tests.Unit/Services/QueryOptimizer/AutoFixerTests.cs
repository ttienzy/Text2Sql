using System.Linq;
using Xunit;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

public class AutoFixerTests
{
    private readonly AutoFixer _fixer;

    public AutoFixerTests()
    {
        _fixer = new AutoFixer();
    }

    // ========== FixMissingSchemaPrefix Tests ==========

    [Fact]
    public void FixMissingSchemaPrefix_ShouldBeHighConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.Equal(ConfidenceLevel.High, result.Confidence);
        Assert.False(result.RequiresSemanticValidation);
        Assert.True(result.CanAutoApply);
    }

    [Fact]
    public void FixMissingSchemaPrefix_ShouldAddDboPrefix()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Id = 1";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.Contains("dbo.Users", result.FixedSql);
        Assert.Contains("AP-13", result.FixesApplied.FirstOrDefault() ?? "");
    }

    [Fact]
    public void FixMissingSchemaPrefix_AlreadyHasSchema_ShouldNotChange()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users";

        // Act
        var result = _fixer.FixMissingSchemaPrefix(sql);

        // Assert
        Assert.Equal(sql, result.FixedSql);
        Assert.Empty(result.FixesApplied);
    }

    // ========== FixSelectStar Tests ==========

    [Fact]
    public void FixSelectStar_ShouldBeMediumConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM Users";
        var schema = CreateMockSchema();

        // Act
        var result = _fixer.FixSelectStar(sql, schema);

        // Assert
        Assert.Equal(ConfidenceLevel.Medium, result.Confidence);
        Assert.True(result.RequiresSemanticValidation);
        Assert.False(result.CanAutoApply);
    }

    [Fact]
    public void FixSelectStar_ShouldHaveSemanticRisks()
    {
        // Arrange
        var sql = "SELECT * FROM Users";
        var schema = CreateMockSchema();

        // Act
        var result = _fixer.FixSelectStar(sql, schema);

        // Assert
        Assert.NotEmpty(result.SemanticRisks);
        Assert.Contains(result.SemanticRisks, r => r.Contains("Column order"));
        Assert.Contains(result.SemanticRisks, r => r.Contains("computed columns"));
    }

    [Fact]
    public void FixSelectStar_ShouldGenerateValidationQuery()
    {
        // Arrange
        var sql = "SELECT * FROM Users";
        var schema = CreateMockSchema();

        // Act
        var result = _fixer.FixSelectStar(sql, schema);

        // Assert
        Assert.NotNull(result.ValidationQuery);
        Assert.Contains("WITH Original AS", result.ValidationQuery);
        Assert.Contains("EXCEPT", result.ValidationQuery);
    }

    // ========== FixNvarcharLiterals Tests ==========

    [Fact]
    public void FixNvarcharLiterals_ShouldBeMediumConfidence()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = 'John'";
        var schema = CreateMockSchema();

        // Act
        var result = _fixer.FixNvarcharLiterals(sql, schema);

        // Assert
        Assert.Equal(ConfidenceLevel.Medium, result.Confidence);
        Assert.True(result.RequiresSemanticValidation);
    }

    [Fact]
    public void FixNvarcharLiterals_ShouldAddNPrefix()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Name = 'John'";
        var schema = CreateMockSchema();

        // Act
        var result = _fixer.FixNvarcharLiterals(sql, schema);

        // Assert
        Assert.Contains("N'John'", result.FixedSql);
        Assert.Contains("AP-21", result.FixesApplied.FirstOrDefault() ?? "");
    }

    // ========== CanAutoFix Tests ==========

    [Fact]
    public void CanAutoFix_AllAutoFixable_ShouldReturnTrue()
    {
        // Arrange
        var issues = new System.Collections.Generic.List<AntiPattern>
        {
            new() { Code = "AP-01" },
            new() { Code = "AP-13" }
        };

        // Act
        var canFix = _fixer.CanAutoFix(issues);

        // Assert
        Assert.True(canFix);
    }

    [Fact]
    public void CanAutoFix_HasNonFixable_ShouldReturnFalse()
    {
        // Arrange
        var issues = new System.Collections.Generic.List<AntiPattern>
        {
            new() { Code = "AP-01" },
            new() { Code = "AP-02" } // Not auto-fixable
        };

        // Act
        var canFix = _fixer.CanAutoFix(issues);

        // Assert
        Assert.False(canFix);
    }

    // ========== CanAutoApply Tests ==========

    [Fact]
    public void CanAutoApply_HighConfidenceNoValidation_ShouldReturnTrue()
    {
        // Arrange
        var result = new AutoFixResult
        {
            Confidence = ConfidenceLevel.High,
            RequiresSemanticValidation = false
        };

        // Act & Assert
        Assert.True(result.CanAutoApply);
    }

    [Fact]
    public void CanAutoApply_MediumConfidence_ShouldReturnFalse()
    {
        // Arrange
        var result = new AutoFixResult
        {
            Confidence = ConfidenceLevel.Medium,
            RequiresSemanticValidation = true
        };

        // Act & Assert
        Assert.False(result.CanAutoApply);
    }

    [Fact]
    public void CanAutoApply_HighConfidenceButNeedsValidation_ShouldReturnFalse()
    {
        // Arrange
        var result = new AutoFixResult
        {
            Confidence = ConfidenceLevel.High,
            RequiresSemanticValidation = true
        };

        // Act & Assert
        Assert.False(result.CanAutoApply);
    }

    // ========== Helper Methods ==========

    private SchemaContext CreateMockSchema()
    {
        return new SchemaContext
        {
            Tables = new System.Collections.Generic.List<TableSchema>
            {
                new()
                {
                    TableName = "Users",
                    Columns = new System.Collections.Generic.List<ColumnInfo>
                    {
                        new() { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                        new() { ColumnName = "Name", DataType = "nvarchar", IsNullable = false },
                        new() { ColumnName = "Email", DataType = "nvarchar", IsNullable = false }
                    }
                }
            }
        };
    }
}
