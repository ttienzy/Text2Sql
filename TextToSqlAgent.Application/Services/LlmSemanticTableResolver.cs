using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// LLM-based semantic table resolver that maps entity mentions to actual database tables.
/// 
/// Features:
/// - Handles synonyms: "user" → "Customers", "product" → "Products"
/// - Context-aware: considers table columns and relationships
/// - Confidence scoring: returns alternatives if ambiguous
/// - Caching: avoids repeated LLM calls for same entities
/// - Fallback: fuzzy matching if LLM fails
/// </summary>
public class LlmSemanticTableResolver : ISemanticTableResolver
{
    private readonly ILLMClient _llmClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LlmSemanticTableResolver> _logger;

    // Cache resolution results for 30 minutes
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    // Confidence threshold for accepting a resolution
    private const double ConfidenceThreshold = 0.7;

    // FIX 2: Vietnamese synonym mapping for common entities
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["user"] = new[] { "NguoiDung", "Users", "KhachHang", "TaiKhoan", "Accounts", "Customers" },
        ["customer"] = new[] { "KhachHang", "Customers", "NguoiDung", "Users", "Clients" },
        ["order"] = new[] { "DonHang", "Orders", "HoaDon", "Invoices" },
        ["product"] = new[] { "SanPham", "Products", "HangHoa", "Items" },
        ["employee"] = new[] { "NhanVien", "Employees", "Staff", "Personnel" },
        ["supplier"] = new[] { "NhaCungCap", "Suppliers", "Vendors" },
        ["category"] = new[] { "DanhMuc", "Categories", "Loai", "Types" },
        ["promotion"] = new[] { "KhuyenMai", "Promotions", "Discounts", "Offers" },
        ["inventory"] = new[] { "TonKho", "Inventory", "Stock", "Warehouse" },
        ["review"] = new[] { "DanhGia", "Reviews", "Ratings", "Feedback" }
    };

    public LlmSemanticTableResolver(
        ILLMClient llmClient,
        IMemoryCache cache,
        ILogger<LlmSemanticTableResolver> logger)
    {
        _llmClient = llmClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TableResolutionResult> ResolveAsync(
        string question,
        DatabaseSchema schema,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[SemanticResolver] Resolving table from question: {Question}", question);

        // Step 1: Extract entity mentions from question
        var entityMentions = await ExtractEntityMentionsAsync(question, ct);

        if (!entityMentions.Any())
        {
            _logger.LogWarning("[SemanticResolver] No entity mentions found in question");
            return new TableResolutionResult
            {
                Success = false,
                ErrorMessage = "Cannot identify target entity in question"
            };
        }

        _logger.LogInformation(
            "[SemanticResolver] Extracted entity mentions: [{Entities}]",
            string.Join(", ", entityMentions));

        // Step 2: Resolve primary entity (usually the first one)
        var primaryEntity = entityMentions.First();
        return await ResolveEntityAsync(primaryEntity, schema, ct);
    }

    public async Task<TableResolutionResult> ResolveEntityAsync(
        string entityMention,
        DatabaseSchema schema,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[SemanticResolver] Resolving entity: '{Entity}'", entityMention);

        // Check cache first - use hash of table names as part of key
        var schemaHash = string.Join(",", schema.Tables.Select(t => t.TableName).OrderBy(n => n).Take(5));
        var cacheKey = $"table_resolution:{entityMention.ToLowerInvariant()}:{schemaHash.GetHashCode()}";
        if (_cache.TryGetValue<TableResolutionResult>(cacheKey, out var cachedResult))
        {
            _logger.LogDebug("[SemanticResolver] Cache hit for entity: {Entity}", entityMention);
            return cachedResult!;
        }

        // Step 1: Try exact match first (fast path)
        var exactMatch = schema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(entityMention, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            _logger.LogInformation("[SemanticResolver] Exact match found: {Table}", exactMatch.TableName);
            var result = new TableResolutionResult
            {
                Success = true,
                ResolvedTableName = exactMatch.TableName,
                OriginalMention = entityMention,
                Confidence = 1.0,
                Reasoning = "Exact table name match"
            };

            _cache.Set(cacheKey, result, CacheDuration);
            return result;
        }

        // Step 2: LLM-based semantic resolution
        try
        {
            var llmResult = await ResolveSemanticallyAsync(entityMention, schema, ct);

            // Cache successful results
            if (llmResult.Success && llmResult.Confidence >= ConfidenceThreshold)
            {
                _cache.Set(cacheKey, llmResult, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration,
                    Size = 1 // Required when SizeLimit is set
                });
            }

            return llmResult;
        }
        catch (Exception ex)
        {
            // FIX 3: Enhanced logging for debugging
            _logger.LogWarning(ex,
                "[SemanticResolver] LLM call failed for entity '{Entity}'. " +
                "Available tables: [{Tables}]. Error: {Error}",
                entityMention,
                string.Join(", ", schema.Tables.Select(t => t.TableName)),
                ex.Message);

            // Step 3: Fallback to fuzzy matching
            return FuzzyMatchTable(entityMention, schema);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // STEP 1: EXTRACT ENTITY MENTIONS
    // ═══════════════════════════════════════════════════════════════

    private async Task<List<string>> ExtractEntityMentionsAsync(string question, CancellationToken ct)
    {
        var prompt = $@"Extract entity mentions from this database query question.
Return ONLY the entity names (e.g., ""user"", ""product"", ""order"", ""customer"").

Question: {question}

Return JSON array of entity mentions:
[""entity1"", ""entity2""]

Entity mentions:";

        var response = await _llmClient.CompleteAsync(prompt, ct);

        try
        {
            var cleaned = response.Trim();
            if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
            else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();

            var entities = JsonSerializer.Deserialize<List<string>>(cleaned) ?? new List<string>();
            return entities.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        }
        catch
        {
            // Fallback: extract common entity keywords
            var keywords = new[] { "user", "customer", "product", "order", "employee", "supplier", "category" };
            var lower = question.ToLowerInvariant();
            return keywords.Where(k => lower.Contains(k)).ToList();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // STEP 2: LLM-BASED SEMANTIC RESOLUTION
    // ═══════════════════════════════════════════════════════════════

    private async Task<TableResolutionResult> ResolveSemanticallyAsync(
        string entityMention,
        DatabaseSchema schema,
        CancellationToken ct)
    {
        var systemPrompt = @"You are a database schema expert that maps entity mentions to actual database table names.

Your task:
1. Analyze the entity mention and available tables
2. Find the best matching table based on semantic meaning
3. Consider table columns to understand table purpose
4. Return confidence score and reasoning

Rules:
- ""user"" typically maps to ""Users"" or ""Customers"" table
- ""product"" maps to ""Products"" table
- ""order"" maps to ""Orders"" table
- Consider singular/plural variations
- Consider domain-specific naming (e.g., ""client"" = ""Customers"")
- If ambiguous, return multiple candidates with confidence scores";

        var tableContext = BuildTableContext(schema);
        var userPrompt = $@"Entity mention: ""{entityMention}""

Available tables:
{tableContext}

Find the best matching table for this entity mention.

Return JSON format:
{{
  ""resolved_table"": ""TableName"",
  ""confidence"": 0.95,
  ""reasoning"": ""explanation"",
  ""alternatives"": [
    {{
      ""table_name"": ""AlternativeTable"",
      ""confidence"": 0.6,
      ""reason"": ""why this could also match""
    }}
  ]
}}

Resolution:";

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Parse LLM response
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
        if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
        cleaned = cleaned.Trim();

        var parsed = JsonSerializer.Deserialize<LlmResolutionResponse>(cleaned);

        if (parsed == null || string.IsNullOrEmpty(parsed.ResolvedTable))
        {
            return new TableResolutionResult
            {
                Success = false,
                ErrorMessage = "LLM failed to resolve table"
            };
        }

        // Validate that resolved table exists in schema
        var resolvedTable = schema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(parsed.ResolvedTable, StringComparison.OrdinalIgnoreCase));

        if (resolvedTable == null)
        {
            _logger.LogWarning(
                "[SemanticResolver] LLM returned non-existent table: {Table}",
                parsed.ResolvedTable);

            return new TableResolutionResult
            {
                Success = false,
                ErrorMessage = $"Resolved table '{parsed.ResolvedTable}' not found in schema"
            };
        }

        _logger.LogInformation(
            "[SemanticResolver] LLM resolved '{Entity}' → '{Table}' (confidence: {Confidence:P0})",
            entityMention,
            resolvedTable.TableName,
            parsed.Confidence);

        return new TableResolutionResult
        {
            Success = true,
            ResolvedTableName = resolvedTable.TableName,
            OriginalMention = entityMention,
            Confidence = parsed.Confidence,
            Reasoning = parsed.Reasoning,
            Alternatives = parsed.Alternatives
                .Select(a => new TableCandidate
                {
                    TableName = a.TableName,
                    Confidence = a.Confidence,
                    Reason = a.Reason,
                    SemanticSimilarity = a.Confidence
                })
                .Where(a => schema.Tables.Any(t =>
                    t.TableName.Equals(a.TableName, StringComparison.OrdinalIgnoreCase)))
                .ToList()
        };
    }

    private class LlmResolutionResponse
    {
        [JsonPropertyName("resolved_table")]
        public string ResolvedTable { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = string.Empty;

        [JsonPropertyName("alternatives")]
        public List<AlternativeTable> Alternatives { get; set; } = new();
    }

    private class AlternativeTable
    {
        [JsonPropertyName("table_name")]
        public string TableName { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    private string BuildTableContext(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables.Take(20)) // Limit to avoid token overflow
        {
            sb.AppendLine($"- {table.TableName}");

            // Add key columns to help LLM understand table purpose
            var keyColumns = table.Columns
                .Where(c => c.IsPrimaryKey || c.ColumnName.Contains("Name") || c.ColumnName.Contains("Email"))
                .Take(3)
                .Select(c => c.ColumnName);

            if (keyColumns.Any())
            {
                sb.AppendLine($"  Columns: {string.Join(", ", keyColumns)}");
            }
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // STEP 3: FUZZY MATCHING FALLBACK
    // ═══════════════════════════════════════════════════════════════

    private TableResolutionResult FuzzyMatchTable(string entityMention, DatabaseSchema schema)
    {
        _logger.LogInformation("[SemanticResolver] Attempting fuzzy match for: {Entity}", entityMention);

        var candidates = new List<TableCandidate>();
        var lower = entityMention.ToLowerInvariant();

        // FIX 2: Check synonym mapping first (before Levenshtein)
        if (Synonyms.TryGetValue(lower, out var synonyms))
        {
            _logger.LogInformation("[SemanticResolver] Found synonyms for '{Entity}': [{Synonyms}]",
                entityMention, string.Join(", ", synonyms));

            foreach (var synonym in synonyms)
            {
                var synonymMatch = schema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(synonym, StringComparison.OrdinalIgnoreCase));

                if (synonymMatch != null)
                {
                    _logger.LogInformation("[SemanticResolver] Synonym match: '{Entity}' → '{Table}' via '{Synonym}'",
                        entityMention, synonymMatch.TableName, synonym);

                    return new TableResolutionResult
                    {
                        Success = true,
                        ResolvedTableName = synonymMatch.TableName,
                        OriginalMention = entityMention,
                        Confidence = 0.85, // High confidence for synonym matches
                        Reasoning = $"Synonym match via '{synonym}'"
                    };
                }
            }
        }

        // Fallback: Levenshtein distance matching
        foreach (var table in schema.Tables)
        {
            var tableLower = table.TableName.ToLowerInvariant();
            var similarity = CalculateSimilarity(lower, tableLower);

            if (similarity > 0.5) // Threshold for fuzzy match
            {
                candidates.Add(new TableCandidate
                {
                    TableName = table.TableName,
                    Confidence = similarity,
                    Reason = $"Fuzzy match (similarity: {similarity:P0})",
                    SemanticSimilarity = similarity
                });
            }
        }

        candidates = candidates.OrderByDescending(c => c.Confidence).ToList();

        if (!candidates.Any())
        {
            return new TableResolutionResult
            {
                Success = false,
                OriginalMention = entityMention,
                ErrorMessage = $"No matching table found for entity '{entityMention}'"
            };
        }

        var best = candidates.First();

        return new TableResolutionResult
        {
            Success = best.Confidence >= 0.7,
            ResolvedTableName = best.TableName,
            OriginalMention = entityMention,
            Confidence = best.Confidence,
            Reasoning = best.Reason,
            Alternatives = candidates.Skip(1).ToList()
        };
    }

    private double CalculateSimilarity(string s1, string s2)
    {
        // Simple Levenshtein-based similarity
        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        var d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            d[0, j] = j;

        for (int j = 1; j <= s2.Length; j++)
        {
            for (int i = 1; i <= s1.Length; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s1.Length, s2.Length];
    }
}
