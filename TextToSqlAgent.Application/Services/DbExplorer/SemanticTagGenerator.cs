using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models.DbExplorer;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Generates semantic tags for tables to improve search accuracy
/// Uses AI to create synonyms, related concepts, and search terms
/// </summary>
public class SemanticTagGenerator
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<SemanticTagGenerator> _logger;
    private readonly PromptRegistry _promptRegistry;

    public SemanticTagGenerator(
        ILLMClient llmClient,
        ILogger<SemanticTagGenerator> logger,
        PromptRegistry promptRegistry)
    {
        _llmClient = llmClient;
        _logger = logger;
        _promptRegistry = promptRegistry;
    }

    /// <summary>
    /// Generate semantic tags for a table
    /// </summary>
    public async Task<SemanticTags> GenerateTagsAsync(
        EnhancedTableInfo table,
        string? systemContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SemanticTags] Generating tags for table {TableName}", table.TableName);

        try
        {
            // Build variables for prompt
            var variables = new Dictionary<string, object>
            {
                ["system_context"] = systemContext ?? "No specific context provided.",
                ["domain"] = ExtractDomain(systemContext),
                ["table_name"] = table.TableName,
                ["description"] = GetTableDescription(table),
                ["role"] = table.Role?.ToString() ?? string.Empty,
                ["module"] = table.Module ?? string.Empty
            };

            var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
                "db-explorer/semantic-tagging",
                new List<string>(),
                variables,
                includeExamples: false);

            var response = await _llmClient.CompleteWithSystemPromptAsync(
                systemPrompt,
                userPrompt,
                cancellationToken);

            // Parse response
            var tags = ParseSemanticTags(response, table.TableName);

            _logger.LogInformation(
                "[SemanticTags] Generated {Count} tags for {TableName}",
                tags.AllTags.Count,
                table.TableName);

            return tags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SemanticTags] Failed to generate tags for {TableName}", table.TableName);

            // Return fallback tags
            return CreateFallbackTags(table);
        }
    }

    /// <summary>
    /// Generate tags for multiple tables (batch)
    /// </summary>
    public async Task<Dictionary<string, SemanticTags>> GenerateTagsBatchAsync(
        List<EnhancedTableInfo> tables,
        string? systemContext = null,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, SemanticTags>();

        _logger.LogInformation("[SemanticTags] Generating tags for {Count} tables in batches of {BatchSize}",
            tables.Count, batchSize);

        // Process in batches to avoid overwhelming LLM
        for (int i = 0; i < tables.Count; i += batchSize)
        {
            var batch = tables.Skip(i).Take(batchSize).ToList();

            var tasks = batch.Select(table =>
                GenerateTagsAsync(table, systemContext, cancellationToken));

            var batchResults = await Task.WhenAll(tasks);

            for (int j = 0; j < batch.Count; j++)
            {
                results[batch[j].TableName] = batchResults[j];
            }

            _logger.LogInformation("[SemanticTags] Completed batch {Current}/{Total}",
                Math.Min(i + batchSize, tables.Count), tables.Count);
        }

        return results;
    }

    /// <summary>
    /// Parse semantic tags from LLM response
    /// </summary>
    private SemanticTags ParseSemanticTags(string response, string tableName)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var dto = JsonSerializer.Deserialize<SemanticTagsDto>(cleaned, options);

            if (dto == null)
            {
                throw new JsonException("Deserialization returned null");
            }

            var tags = new SemanticTags
            {
                TableName = tableName,
                Vietnamese = dto.Vietnamese ?? new List<string>(),
                English = dto.English ?? new List<string>(),
                Abbreviations = dto.Abbreviations ?? new List<string>(),
                RelatedConcepts = dto.RelatedConcepts ?? new List<string>(),
                SearchTerms = dto.SearchTerms ?? new List<string>()
            };

            // Add table name itself
            tags.AllTags.Add(tableName.ToLower());

            // Add all tags to combined list
            tags.AllTags.AddRange(tags.Vietnamese.Select(t => t.ToLower()));
            tags.AllTags.AddRange(tags.English.Select(t => t.ToLower()));
            tags.AllTags.AddRange(tags.Abbreviations.Select(t => t.ToLower()));
            tags.AllTags.AddRange(tags.RelatedConcepts.Select(t => t.ToLower()));
            tags.AllTags.AddRange(tags.SearchTerms.Select(t => t.ToLower()));

            // Remove duplicates
            tags.AllTags = tags.AllTags.Distinct().ToList();

            return tags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SemanticTags] Failed to parse response for {TableName}", tableName);
            return CreateFallbackTags(new EnhancedTableInfo { TableName = tableName });
        }
    }

    /// <summary>
    /// Create fallback tags using heuristics
    /// </summary>
    private SemanticTags CreateFallbackTags(EnhancedTableInfo table)
    {
        var tags = new SemanticTags
        {
            TableName = table.TableName
        };

        // Add table name
        tags.AllTags.Add(table.TableName.ToLower());

        // Add role-based tags
        if (table.Role.HasValue)
        {
            tags.RelatedConcepts.Add(table.Role.Value.ToString().ToLower());
            tags.AllTags.Add(table.Role.Value.ToString().ToLower());
        }

        // Add module-based tags
        if (!string.IsNullOrEmpty(table.Module))
        {
            tags.RelatedConcepts.Add(table.Module.ToLower());
            tags.AllTags.Add(table.Module.ToLower());
        }

        // Add common Vietnamese abbreviations and translations
        var commonMappings = new Dictionary<string, (string[] vietnamese, string[] abbreviations)>
        {
            { "khachhang", (new[] { "khách hàng", "người mua" }, new[] { "kh", "customer", "client" }) },
            { "customer", (new[] { "khách hàng", "người mua" }, new[] { "kh" }) },
            { "nhanvien", (new[] { "nhân viên", "người lao động" }, new[] { "nv", "employee", "staff" }) },
            { "employee", (new[] { "nhân viên", "người lao động" }, new[] { "nv" }) },
            { "sanpham", (new[] { "sản phẩm", "hàng hóa" }, new[] { "sp", "product", "item" }) },
            { "product", (new[] { "sản phẩm", "hàng hóa" }, new[] { "sp" }) },
            { "donhang", (new[] { "đơn hàng", "phiếu đặt" }, new[] { "dh", "order", "purchase" }) },
            { "order", (new[] { "đơn hàng", "phiếu đặt" }, new[] { "dh" }) },
            { "hoadon", (new[] { "hóa đơn", "phiếu thu" }, new[] { "hd", "invoice", "bill" }) },
            { "invoice", (new[] { "hóa đơn", "phiếu thu" }, new[] { "hd" }) },
            { "danhmuc", (new[] { "danh mục", "nhóm" }, new[] { "dm", "category", "catalog" }) },
            { "category", (new[] { "danh mục", "nhóm" }, new[] { "dm" }) },
            { "kho", (new[] { "kho", "tồn kho" }, new[] { "inventory", "stock", "warehouse" }) },
            { "khuvuc", (new[] { "khu vực", "vị trí" }, new[] { "region", "area", "location" }) },
            { "nhacungcap", (new[] { "nhà cung cấp", "đối tác" }, new[] { "ncc", "supplier", "vendor" }) },
            { "supplier", (new[] { "nhà cung cấp", "đối tác" }, new[] { "ncc" }) }
        };

        var lowerTableName = table.TableName.ToLower();
        foreach (var (key, data) in commonMappings)
        {
            if (lowerTableName.Contains(key))
            {
                tags.Vietnamese.AddRange(data.vietnamese);
                tags.Abbreviations.AddRange(data.abbreviations);
                tags.AllTags.AddRange(data.vietnamese);
                tags.AllTags.AddRange(data.abbreviations);
            }
        }

        // De-duplicate tags
        tags.Vietnamese = tags.Vietnamese.Distinct().ToList();
        tags.AllTags = tags.AllTags.Distinct().ToList();

        return tags;
    }

    /// <summary>
    /// Get table description
    /// </summary>
    private string GetTableDescription(EnhancedTableInfo table)
    {
        var parts = new List<string>();

        if (table.Role.HasValue)
        {
            parts.Add($"Role: {table.Role.Value}");
        }

        if (!string.IsNullOrEmpty(table.Module))
        {
            parts.Add($"Module: {table.Module}");
        }

        parts.Add($"Columns: {table.ColumnCount}");
        parts.Add($"Rows: {table.RowCount:N0}");

        return parts.Count > 0 ? string.Join(", ", parts) : "No description available";
    }

    /// <summary>
    /// Extract domain from system context
    /// </summary>
    private string ExtractDomain(string? systemContext)
    {
        if (string.IsNullOrEmpty(systemContext))
            return "Unknown";

        var lower = systemContext.ToLower();
        if (lower.Contains("e-commerce") || lower.Contains("bán lẻ"))
            return "E-commerce";
        if (lower.Contains("erp") || lower.Contains("doanh nghiệp"))
            return "ERP";
        if (lower.Contains("crm") || lower.Contains("khách hàng"))
            return "CRM";
        if (lower.Contains("healthcare") || lower.Contains("y tế"))
            return "Healthcare";

        return "Unknown";
    }

    /// <summary>
    /// Clean JSON response
    /// </summary>
    private string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove markdown code blocks
        if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
        }

        // Find JSON block
        var jsonStart = cleaned.IndexOf('{');
        var jsonEnd = cleaned.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        // ✅ FIX: Remove trailing commas before closing braces/brackets
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @",\s*([}\]])", "$1");

        // ✅ FIX: If multiple JSON objects detected, take only the first one
        var objectMatches = System.Text.RegularExpressions.Regex.Matches(cleaned, @"}\s*{");
        if (objectMatches.Count > 0)
        {
            _logger.LogWarning("[SemanticTags] Multiple JSON objects detected, taking first one only");
            var firstObjectEnd = cleaned.IndexOf('}');
            if (firstObjectEnd > 0)
            {
                cleaned = cleaned.Substring(0, firstObjectEnd + 1);
            }
        }

        return cleaned;
    }

    /// <summary>
    /// DTO for JSON deserialization
    /// </summary>
    private class SemanticTagsDto
    {
        public List<string>? Vietnamese { get; set; }
        public List<string>? English { get; set; }
        public List<string>? Abbreviations { get; set; }
        public List<string>? RelatedConcepts { get; set; }
        public List<string>? SearchTerms { get; set; }
    }
}

/// <summary>
/// Semantic tags for a table
/// </summary>
public class SemanticTags
{
    public string TableName { get; set; } = string.Empty;
    public List<string> Vietnamese { get; set; } = new();
    public List<string> English { get; set; } = new();
    public List<string> Abbreviations { get; set; } = new();
    public List<string> RelatedConcepts { get; set; } = new();
    public List<string> SearchTerms { get; set; } = new();
    public List<string> AllTags { get; set; } = new();
}
