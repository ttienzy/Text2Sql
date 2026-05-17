using FluentAssertions;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Tests.Unit.Core;

public class ColumnSemanticHintsTests
{
    [Fact]
    public void Infer_Should_Mark_Id_Columns_As_Low_Priority_Technical_Keys()
    {
        var profile = ColumnSemanticHints.Infer(
            new ColumnInfo { ColumnName = "ProductId", DataType = "int", IsForeignKey = true },
            "Reviews");

        profile.Role.Should().Be("technical_key");
        profile.DisplayPriority.Should().Be("low");
        profile.PreferredForReports.Should().BeFalse();
        profile.IsTechnical.Should().BeTrue();
    }

    [Fact]
    public void Infer_Should_Mark_Name_Columns_As_High_Priority_Report_Labels()
    {
        var profile = ColumnSemanticHints.Infer(
            new ColumnInfo { ColumnName = "ProductName", DataType = "nvarchar" },
            "Products");

        profile.Role.Should().Be("display_label");
        profile.DisplayPriority.Should().Be("high");
        profile.PreferredForReports.Should().BeTrue();
        profile.IsTechnical.Should().BeFalse();
    }

    [Fact]
    public void Infer_Should_Mark_Audit_Columns_As_Low_Priority()
    {
        var profile = ColumnSemanticHints.Infer(
            new ColumnInfo { ColumnName = "UpdatedAt", DataType = "datetime" },
            "Products");

        profile.Role.Should().Be("audit_field");
        profile.DisplayPriority.Should().Be("low");
        profile.PreferredForReports.Should().BeFalse();
        profile.IsTechnical.Should().BeTrue();
    }
}
