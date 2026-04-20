using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Models.DbExplorer;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Detects implicit foreign key relationships using fast metadata heuristics
/// and validates likely matches with the V2 FK detection prompt.
/// </summary>
public class ImplicitRelationshipDetector
{
    private readonly ILogger<ImplicitRelationshipDetector> _logger;
    private readonly ILLMClient _llmClient;
    private readonly PromptRegistry _promptRegistry;

    private static readonly List<string> FkPatterns = new()
    {
        @"^{table}Id$",
        @"^{table}_ID$",
        @"^{table}ID$",
        @"^Ma{table}$",
        @"^{table}Code$",
        @"^{table}Key$",
        @"^ID{table}$",
        @"^{table}No$",
        @"^{table}Ref$",
        @"^{table}FK$",
    };

    private static readonly Dictionary<string, string> VietnameseAbbreviations = new()
    {
        { "KH", "KhachHang" },
        { "NV", "NhanVien" },
        { "SP", "SanPham" },
        { "DH", "DonHang" },
        { "HD", "HoaDon" },
        { "DM", "DanhMuc" },
        { "NCC", "NhaCungCap" },
        { "KHO", "Kho" },
        { "PX", "PhieuXuat" },
        { "PN", "PhieuNhap" },
    };

    public ImplicitRelationshipDetector(
        ILogger<ImplicitRelationshipDetector> logger,
        ILLMClient llmClient,
        PromptRegistry promptRegistry)
    {
        _logger = logger;
        _llmClient = llmClient;
        _promptRegistry = promptRegistry;
    }

    public async Task<List<ImplicitRelationship>> DetectImplicitForeignKeysAsync(
        EnhancedTableInfo table,
        EnhancedDatabaseSchema schema,
        string? systemContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ImplicitRelationship>();

        _logger.LogInformation("[ImplicitFK] Analyzing table {TableName} for implicit relationships", table.TableName);

        var potentialParents = schema.EnhancedTables
            .Where(t => t.TableName != table.TableName)
            .Where(t => !IsSystemTable(t.TableName))
            .ToList();

        foreach (var column in table.Columns)
        {
            if (column.IsForeignKey || column.IsPrimaryKey)
            {
                continue;
            }

            var matches = FindPotentialParents(column, table, potentialParents)
                .OrderByDescending(CalculateConfidence)
                .Take(3)
                .ToList();

            foreach (var match in matches)
            {
                var heuristicConfidence = CalculateConfidence(match);
                if (heuristicConfidence < 0.6)
                {
                    continue;
                }

                var relationship = await ValidateMatchAsync(
                    table,
                    column,
                    match,
                    heuristicConfidence,
                    systemContext,
                    cancellationToken);

                if (relationship == null)
                {
                    continue;
                }

                results.Add(relationship);

                _logger.LogInformation(
                    "[ImplicitFK] Found: {FromTable}.{FromColumn} -> {ToTable}.{ToColumn} (confidence: {Confidence:P0}, method: {Method})",
                    relationship.FromTable,
                    relationship.FromColumn,
                    relationship.ToTable,
                    relationship.ToColumn,
                    relationship.Confidence,
                    relationship.DetectionMethod);
            }
        }

        _logger.LogInformation("[ImplicitFK] Found {Count} implicit relationships for {TableName}", results.Count, table.TableName);
        return results;
    }

    private async Task<ImplicitRelationship?> ValidateMatchAsync(
        EnhancedTableInfo childTable,
        ColumnInfo childColumn,
        FkMatch match,
        double heuristicConfidence,
        string? systemContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var variables = new Dictionary<string, object>
            {
                ["system_context"] = systemContext ?? "No specific context provided.",
                ["domain"] = ExtractDomain(systemContext),
                ["child_table"] = childTable.TableName,
                ["child_column"] = childColumn.ColumnName,
                ["child_data_type"] = childColumn.DataType,
                ["parent_table"] = match.ParentTable.TableName,
                ["parent_column"] = match.ParentColumn.ColumnName,
                ["parent_data_type"] = match.ParentColumn.DataType,
                ["child_rows"] = childTable.RowCount,
                ["parent_rows"] = match.ParentTable.RowCount,
                ["data_type_match"] = true,
                ["naming_match"] = match.NamingScore >= 0.7
            };

            var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
                "db-explorer/fk-detection",
                new List<string>(),
                variables,
                includeExamples: false);

            var response = await _llmClient.CompleteWithSystemPromptAsync(
                systemPrompt,
                userPrompt,
                cancellationToken);

            var validation = ParseFkValidation(response);
            if (!validation.IsImplicitFk)
            {
                return null;
            }

            var finalConfidence = Math.Max(heuristicConfidence, validation.Confidence);
            return new ImplicitRelationship
            {
                FromTable = childTable.TableName,
                FromColumn = childColumn.ColumnName,
                ToTable = match.ParentTable.TableName,
                ToColumn = match.ParentColumn.ColumnName,
                Confidence = finalConfidence,
                DetectionMethod = $"{match.Method}+llm",
                Reason = string.IsNullOrWhiteSpace(validation.Reason) ? match.Reason : validation.Reason,
                RequiresDataValidation = finalConfidence < 0.85
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ImplicitFK] LLM validation failed for {ChildTable}.{ChildColumn} -> {ParentTable}.{ParentColumn}; falling back to heuristic confidence",
                childTable.TableName,
                childColumn.ColumnName,
                match.ParentTable.TableName,
                match.ParentColumn.ColumnName);

            return new ImplicitRelationship
            {
                FromTable = childTable.TableName,
                FromColumn = childColumn.ColumnName,
                ToTable = match.ParentTable.TableName,
                ToColumn = match.ParentColumn.ColumnName,
                Confidence = heuristicConfidence,
                DetectionMethod = $"{match.Method}+heuristic-fallback",
                Reason = match.Reason,
                RequiresDataValidation = heuristicConfidence < 0.85
            };
        }
    }

    private List<FkMatch> FindPotentialParents(
        ColumnInfo column,
        EnhancedTableInfo childTable,
        List<EnhancedTableInfo> potentialParents)
    {
        var matches = new List<FkMatch>();

        foreach (var parentTable in potentialParents)
        {
            var namingMatch = CheckNamingPattern(column, parentTable);
            if (namingMatch != null)
            {
                matches.Add(namingMatch);
                continue;
            }

            var containsMatch = CheckColumnContainsTableName(column, parentTable);
            if (containsMatch != null)
            {
                matches.Add(containsMatch);
            }

            var abbreviationMatch = CheckVietnameseAbbreviation(column, parentTable);
            if (abbreviationMatch != null)
            {
                matches.Add(abbreviationMatch);
            }
        }

        matches = matches.Where(m => IsDataTypeCompatible(column, m.ParentColumn)).ToList();
        matches = matches.Where(m => IsRowCountLogical(childTable, m.ParentTable)).ToList();
        return matches;
    }

    private FkMatch? CheckNamingPattern(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        foreach (var pattern in FkPatterns)
        {
            var regex = new Regex(
                pattern.Replace("{table}", Regex.Escape(parentTable.TableName)),
                RegexOptions.IgnoreCase);

            if (!regex.IsMatch(column.ColumnName))
            {
                continue;
            }

            var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pkColumn == null)
            {
                return null;
            }

            return new FkMatch
            {
                ParentTable = parentTable,
                ParentColumn = pkColumn,
                Method = "naming_pattern",
                Reason = $"Column name '{column.ColumnName}' matches FK pattern for table '{parentTable.TableName}'",
                NamingScore = 1.0
            };
        }

        return null;
    }

    private FkMatch? CheckColumnContainsTableName(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        var columnName = column.ColumnName.ToLowerInvariant();
        var tableName = parentTable.TableName.ToLowerInvariant();

        if (!columnName.Contains(tableName) || columnName == tableName)
        {
            return null;
        }

        var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkColumn == null)
        {
            return null;
        }

        return new FkMatch
        {
            ParentTable = parentTable,
            ParentColumn = pkColumn,
            Method = "name_contains",
            Reason = $"Column name '{column.ColumnName}' contains table name '{parentTable.TableName}'",
            NamingScore = 0.7
        };
    }

    private FkMatch? CheckVietnameseAbbreviation(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        if (!column.ColumnName.StartsWith("Ma", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var abbreviation = column.ColumnName.Substring(2).ToUpperInvariant();
        if (!VietnameseAbbreviations.TryGetValue(abbreviation, out var fullName))
        {
            return null;
        }

        if (!parentTable.TableName.Equals(fullName, StringComparison.OrdinalIgnoreCase) &&
            !parentTable.TableName.Contains(fullName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkColumn == null)
        {
            return null;
        }

        return new FkMatch
        {
            ParentTable = parentTable,
            ParentColumn = pkColumn,
            Method = "vietnamese_abbreviation",
            Reason = $"Column '{column.ColumnName}' is Vietnamese abbreviation for '{fullName}' (table: '{parentTable.TableName}')",
            NamingScore = 0.9
        };
    }

    private bool IsDataTypeCompatible(ColumnInfo childColumn, ColumnInfo parentColumn)
    {
        var childType = NormalizeDataType(childColumn.DataType);
        var parentType = NormalizeDataType(parentColumn.DataType);

        if (childType == parentType)
        {
            return true;
        }

        var numericTypes = new[] { "int", "bigint", "smallint", "tinyint" };
        if (numericTypes.Contains(childType) && numericTypes.Contains(parentType))
        {
            return true;
        }

        var stringTypes = new[] { "varchar", "nvarchar", "char", "nchar" };
        return stringTypes.Contains(childType) && stringTypes.Contains(parentType);
    }

    private bool IsRowCountLogical(EnhancedTableInfo childTable, EnhancedTableInfo parentTable)
    {
        if (childTable.RowCount == 0 || parentTable.RowCount == 0)
        {
            return true;
        }

        return childTable.RowCount <= parentTable.RowCount * 10;
    }

    private double CalculateConfidence(FkMatch match)
    {
        match.TypeScore = 1.0;
        match.RowCountScore = 1.0;

        var weights = new Dictionary<string, double>
        {
            { "naming_pattern", 0.5 },
            { "name_contains", 0.4 },
            { "vietnamese_abbreviation", 0.45 },
        };

        var namingWeight = weights.GetValueOrDefault(match.Method, 0.4);
        var typeWeight = 0.3;
        var rowCountWeight = 0.2;

        return Math.Min(
            (match.NamingScore * namingWeight) +
            (match.TypeScore * typeWeight) +
            (match.RowCountScore * rowCountWeight),
            1.0);
    }

    private FkValidationResult ParseFkValidation(string response)
    {
        var cleaned = response.Trim()
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        var jsonStart = cleaned.IndexOf('{');
        var jsonEnd = cleaned.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        var result = JsonSerializer.Deserialize<FkValidationResult>(
            cleaned,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result ?? new FkValidationResult();
    }

    private string NormalizeDataType(string dataType)
    {
        return dataType.ToLowerInvariant()
            .Replace("identity", string.Empty)
            .Replace("not null", string.Empty)
            .Trim();
    }

    private bool IsSystemTable(string tableName)
    {
        var systemPrefixes = new[] { "sys", "dbo", "__EFMigrationsHistory", "sysdiagrams" };
        return systemPrefixes.Any(prefix => tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractDomain(string? systemContext)
    {
        if (string.IsNullOrWhiteSpace(systemContext))
        {
            return "general business";
        }

        var lower = systemContext.ToLowerInvariant();
        return lower switch
        {
            var value when value.Contains("ecommerce") || value.Contains("commerce") => "e-commerce",
            var value when value.Contains("crm") || value.Contains("customer") => "crm",
            var value when value.Contains("erp") => "erp",
            var value when value.Contains("health") => "healthcare",
            var value when value.Contains("finance") => "finance",
            _ => systemContext
        };
    }

    private sealed class FkMatch
    {
        public EnhancedTableInfo ParentTable { get; set; } = null!;
        public ColumnInfo ParentColumn { get; set; } = null!;
        public string Method { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double NamingScore { get; set; }
        public double TypeScore { get; set; }
        public double RowCountScore { get; set; }
    }

    private sealed class FkValidationResult
    {
        public bool IsImplicitFk { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }
}
