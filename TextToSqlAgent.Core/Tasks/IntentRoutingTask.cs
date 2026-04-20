using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Tasks;

/// <summary>
/// Enhanced prompt normalization task with intent detection and routing.
/// 
/// v2.0 Features:
/// - Intent classification (Query, Insert, Update, Delete, DDL, etc.)
/// - Ambiguity detection
/// - Risk level assessment
/// - Complexity scoring for pipeline routing
/// - Vietnamese/English support with term mapping
/// </summary>
public class IntentRoutingTask : IAgentTask<string, IntentRoutingResult>
{
    private readonly ILogger<IntentRoutingTask> _logger;
    private readonly IIntentRoutingPromptService? _promptService;

    // Intent classification patterns
    private static readonly Dictionary<IntentCategory, List<string>> IntentPatterns = new()
    {
        { IntentCategory.Query, new List<string>
            {
                @"^(cho\s+tôi\s+)?xem|tìm|liệt\s+kê|danh\s+sách|thống\s+kê|tổng\s+hợp",
                @"bao\s+nhiêu|count|sum|average|total|summary",
                @"top\s+\d+|lớn\s+nhất|nhỏ\s+nhất|cao\s+nhất|thấp\s+nhất",
                @"theo\s+\w+|group\s+by|phân\s+theo|từng",
                @"trong\s+khoảng|from\s+\d+.*to|between"
            }
        },
        { IntentCategory.Insert, new List<string>
            {
                @"thêm|mới\s+tạo|chèn|insert\s+into|tạo\s+mới",
                @"đăng\s+ký|register|create\s+new"
            }
        },
        { IntentCategory.Update, new List<string>
            {
                @"cập\s+nhật|sửa|thay\s+đổi|modify|update\s+\w+",
                @"set\s+\w+\s*=|gán\s+giá\s+trị"
            }
        },
        { IntentCategory.Delete, new List<string>
            {
                @"xóa|delete|remove|bỏ\s+",
                @"loại\s+bỏ"
            }
        },
        { IntentCategory.Upsert, new List<string>
            {
                @"upsert|merge|insert\s+or\s+update|thêm\s+hoặc\s+sửa"
            }
        },
        { IntentCategory.DdlIndex, new List<string>
            {
                @"tạo\s+index|tạo\s+chỉ\s+mục|create\s+index|drop\s+index"
            }
        },
        { IntentCategory.DdlProcedure, new List<string>
            {
                @"tạo\s+proc|tạo\s+procedure|tạo\s+function|create\s+procedure|create\s+function",
                @"sửa\s+proc|modify\s+procedure"
            }
        },
        { IntentCategory.DdlAlter, new List<string>
            {
                @"thêm\s+cột|add\s+column|sửa\s+cột|alter\s+table|modify\s+column",
                @"đổi\s+tên\s+bảng|rename\s+table"
            }
        },
        { IntentCategory.DdlView, new List<string>
            {
                @"tạo\s+view|create\s+view|sửa\s+view|modify\s+view"
            }
        },
        { IntentCategory.Forbidden, new List<string>
            {
                @"drop\s+table|truncate\s+table|xóa\s+tất\s+cả|delete\s+all",
                @"drop\s+database|backup|restore|shutdown"
            }
        },
        { IntentCategory.OffTopic, new List<string>
            {
                @"trợ\s+lý|help|giúp|tôi|là\s+gì|what\s+is|who\s+is|where\s+is",
                @"time|ngày\s+giờ|weather|thời\s+tiết",
                @"tin\s+tức|news"
            }
        }
    };

    // Vietnamese business term mappings
    private static readonly Dictionary<string, string> VietnameseMappings = new()
    {
        // Customers
        { "khách hàng", "Customer" },
        { "khách", "Customer" },
        { "kh", "Customer" },
        
        // Orders
        { "đơn hàng", "Order" },
        { "đơn", "Order" },
        { "dh", "Order" },
        { "hóa đơn", "Invoice" },
        { "hd", "Invoice" },
        
        // Products
        { "sản phẩm", "Product" },
        { "sp", "Product" },
        { "hàng hóa", "Product" },
        
        // Business metrics
        { "doanh thu", "Revenue" },
        { "dt", "Revenue" },
        { "lợi nhuận", "Profit" },
        { "ln", "Profit" },
        { "chi phí", "Cost" },
        { "cp", "Cost" },
        
        // Common terms
        { "tổng", "SUM" },
        { "trung bình", "AVG" },
        { "tb", "AVG" },
        { "số lượng", "COUNT" },
        { "sl", "COUNT" },
        { "tối đa", "MAX" },
        { "tối thiểu", "MIN" },
        
        // Tables
        { "bảng", "table" },
        { "tb", "table" },
        { "ds", "danh sách" }
    };

    // Ambiguity detection patterns
    private static readonly List<string> AmbiguityPatterns = new()
    {
        @"tim\s*.*\s*gi$",           // "tìm gì" without specification
        @"bao\s*nhieu\s*$",          // "bao nhiêu" without context
        @"top\s*$",                  // "top" without number
        @"theo\s*$",                 // "theo" without specification
        @"lớn\s*$",                  // "lớn" without comparison
        @"nhỏ\s*$",                  // "nhỏ" without comparison
        @"gần\s*$",                  // "gần" without specification
        @"mới\s*$",                  // "mới" without date range
        @"nhiều\s*$",                // "nhiều" without metric
    };

    // Risk keywords
    private static readonly List<string> RiskKeywords = new()
    {
        "drop", "delete", "truncate", "shutdown",
        "backup", "restore", "alter", "create table",
        "exec", "execute", "grant", "revoke"
    };

    public IntentRoutingTask(ILogger<IntentRoutingTask> logger)
        : this(logger, null)
    {
    }

    public IntentRoutingTask(
        ILogger<IntentRoutingTask> logger,
        IIntentRoutingPromptService? promptService)
    {
        _logger = logger;
        _promptService = promptService;
    }

    /// <summary>
    /// Execute intent routing and validation
    /// </summary>
    public async Task<IntentRoutingResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[IntentRouting] Processing input: {InputLength} chars", input?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input cannot be empty", nameof(input));
        }

        // Step 1: Normalize input
        var normalized = NormalizeInput(input);
        _logger.LogDebug("[IntentRouting] Normalized: {Normalized}", normalized);

        // Step 2: Detect language
        var language = DetectLanguage(normalized);

        // Step 3: Expand abbreviations and mappings
        var expanded = ExpandAbbreviations(normalized);

        // Step 4: Classify intent
        var intentResult = ClassifyIntent(expanded);

        // Step 5: Detect ambiguity
        var ambiguityResult = DetectAmbiguity(expanded);

        // Step 6: Assess risk
        var riskLevel = AssessRisk(expanded);

        // Step 7: Score complexity
        var complexityScore = ScoreComplexity(expanded);

        // Step 8: Determine pipeline route
        var route = DetermineRoute(intentResult.Intent, riskLevel, complexityScore);

        // Step 9: Generate safe alternatives for DML/DDL
        var safeAlternatives = GenerateSafeAlternatives(intentResult.Intent, expanded);

        // Step 10: Optional prompt-backed clarification/rejection copy
        ambiguityResult = await EnrichAmbiguityAsync(input, language, ambiguityResult, cancellationToken);
        var rejectionMessage = await GenerateRejectionMessageAsync(
            input,
            language,
            intentResult,
            riskLevel,
            cancellationToken);

        _logger.LogInformation(
            "[IntentRouting] Result - Intent: {Intent}, Route: {Route}, Risk: {Risk}, Confidence: {Confidence:P1}",
            intentResult.Intent, route, riskLevel, intentResult.Confidence);

        return await Task.FromResult(new IntentRoutingResult
        {
            OriginalInput = input,
            NormalizedInput = expanded,
            Language = language,
            Intent = intentResult,
            Ambiguity = ambiguityResult,
            RiskLevel = riskLevel,
            ComplexityScore = complexityScore,
            Route = route,
            Timestamp = DateTime.UtcNow,
            Warnings = intentResult.Warnings,
            SafeAlternatives = safeAlternatives,
            RejectionMessage = rejectionMessage
        });
    }

    private string NormalizeInput(string input)
    {
        // Trim and normalize whitespace
        var result = input.Trim();
        result = Regex.Replace(result, @"\s+", " ");
        
        // Fix common Vietnamese typos
        var typos = new Dictionary<string, string>
        {
            { "cho toi", "cho tôi" },
            { "bat dau", "bắt đầu" },
            { "ket thuc", "kết thúc" },
            { "xac nhan", "xác nhận" }
        };

        foreach (var (typo, correct) in typos)
        {
            result = Regex.Replace(result, typo, correct, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private string DetectLanguage(string text)
    {
        var vietnameseChars = new[] { 'ă', 'â', 'đ', 'ê', 'ô', 'ơ', 'ư',
                                       'á', 'à', 'ả', 'ã', 'ạ',
                                       'ắ', 'ằ', 'ẳ', 'ẵ', 'ặ',
                                       'ấ', 'ầ', 'ẩ', 'ẫ', 'ậ' };

        var hasVietnamese = text.Any(c => vietnameseChars.Contains(char.ToLower(c)));
        return hasVietnamese ? "vi" : "en";
    }

    private string ExpandAbbreviations(string text)
    {
        var result = text;
        
        foreach (var (abbrev, full) in VietnameseMappings)
        {
            // Case-insensitive word boundary match
            var pattern = $@"\b{Regex.Escape(abbrev)}\b";
            result = Regex.Replace(result, pattern, full, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private IntentClassificationResult ClassifyIntent(string text)
    {
        var matchedKeywords = new List<string>();
        var warnings = new List<string>();
        IntentCategory? bestMatch = null;
        var bestScore = 0;
        var reasoning = new List<string>();

        text = text.ToLowerInvariant();

        foreach (var (category, patterns) in IntentPatterns)
        {
            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
                if (matches.Count > 0)
                {
                    matchedKeywords.Add(pattern);
                    var score = matches.Count * (category == IntentCategory.OffTopic ? -1 : 1);
                    
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = category;
                    }
                }
            }
        }

        // Handle special cases
        if (text.Contains("select") || text.Contains("from"))
        {
            bestMatch = IntentCategory.Query;
            matchedKeywords.Add("sql-query-detected");
        }

        // Check for forbidden operations with dangerous WHERE
        if (bestMatch == IntentCategory.Delete && !text.Contains("where"))
        {
            bestMatch = IntentCategory.Forbidden;
            matchedKeywords.Add("delete-without-where");
            warnings.Add("DELETE without WHERE clause detected - mass deletion risk");
        }

        var intent = bestMatch ?? IntentCategory.Unknown;
        var confidence = CalculateConfidence(matchedKeywords, text);

        return new IntentClassificationResult
        {
            Intent = intent,
            Route = GetRouteFromIntent(intent),
            Confidence = confidence,
            Reasoning = string.Join("; ", reasoning),
            NormalizedQuery = text,
            Method = ClassificationMethod.RuleBased,
            DetectedEntities = ExtractEntities(text),
            Warnings = warnings,
            MatchedKeywords = matchedKeywords
        };
    }

    private AmbiguityResult DetectAmbiguity(string text)
    {
        var ambiguous = false;
        var reasons = new List<string>();
        var suggestions = new List<string>();

        foreach (var pattern in AmbiguityPatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                ambiguous = true;
                reasons.Add($"Ambiguous pattern detected: '{match.Value}'");
                suggestions.Add(GenerateSuggestion(match.Value));
            }
        }

        // Check for missing time range
        if (Regex.IsMatch(text, @"trong\s+(tháng|năm|ngày)", RegexOptions.IgnoreCase) && 
            !Regex.IsMatch(text, @"\d{4}"))
        {
            ambiguous = true;
            reasons.Add("Date range specified but no year/month provided");
            suggestions.Add("Please specify the year (e.g., '2024')");
        }

        return new AmbiguityResult
        {
            IsAmbiguous = ambiguous,
            Reasons = reasons,
            Suggestions = suggestions,
            Confidence = ambiguous ? 0.7 : 1.0
        };
    }

    private string GenerateSuggestion(string ambiguousPattern)
    {
        return ambiguousPattern switch
        {
            var p when p.EndsWith("gi") => "Specify what to find (e.g., 'tìm khách hàng')",
            var p when p.EndsWith("nhieu") => "Specify the metric (e.g., 'bao nhiêu đơn hàng')",
            var p when p.Contains("top") => "Specify the number (e.g., 'top 10')",
            var p when p.EndsWith("theo") => "Specify grouping criteria (e.g., 'theo tháng')",
            _ => "Please provide more specific details"
        };
    }

    private async Task<AmbiguityResult> EnrichAmbiguityAsync(
        string originalInput,
        string language,
        AmbiguityResult ambiguityResult,
        CancellationToken cancellationToken)
    {
        if (!ambiguityResult.IsAmbiguous || _promptService == null)
        {
            return ambiguityResult;
        }

        try
        {
            var clarification = await _promptService.GenerateClarificationAsync(
                originalInput,
                language,
                ambiguityResult.Reasons,
                ambiguityResult.Suggestions,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(clarification))
            {
                var mergedSuggestions = new List<string> { clarification };
                mergedSuggestions.AddRange(ambiguityResult.Suggestions);
                ambiguityResult.Suggestions = mergedSuggestions.Distinct().ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentRouting] Failed to generate prompt-backed clarification");
        }

        return ambiguityResult;
    }

    private async Task<string> GenerateRejectionMessageAsync(
        string originalInput,
        string language,
        IntentClassificationResult intentResult,
        RiskLevel riskLevel,
        CancellationToken cancellationToken)
    {
        var fallback = GenerateFallbackRejectionMessage(intentResult.Intent, riskLevel, language);

        if (_promptService == null || ShouldSkipPromptedRejection(intentResult.Intent))
        {
            return fallback;
        }

        try
        {
            var prompted = await _promptService.GenerateRejectionMessageAsync(
                originalInput,
                language,
                intentResult.Intent,
                riskLevel,
                intentResult.Confidence,
                intentResult.DetectedEntities,
                intentResult.Warnings,
                cancellationToken);

            return string.IsNullOrWhiteSpace(prompted) ? fallback : prompted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentRouting] Failed to generate prompt-backed rejection message");
            return fallback;
        }
    }

    private static bool ShouldSkipPromptedRejection(IntentCategory intent)
    {
        return intent is IntentCategory.Query or IntentCategory.Unknown;
    }

    private RiskLevel AssessRisk(string text)
    {
        var textLower = text.ToLowerInvariant();
        var riskKeywords = RiskKeywords.Count(k => textLower.Contains(k));

        // High-risk patterns
        if (textLower.Contains("drop table") || textLower.Contains("truncate"))
            return RiskLevel.Critical;
        
        if (textLower.Contains("delete") && textLower.Contains("where"))
            return RiskLevel.Critical;
        
        if (textLower.Contains("update") && !textLower.Contains("where"))
            return RiskLevel.High;

        // Medium-risk patterns
        if (textLower.Contains("update") && textLower.Contains("where"))
            return RiskLevel.High;

        if (textLower.Contains("insert"))
            return RiskLevel.Medium;

        // Low risk
        return RiskLevel.Low;
    }

    private double ScoreComplexity(string text)
    {
        var textLower = text.ToLowerInvariant();
        var score = 0.0;

        // Join complexity
        if (textLower.Contains("join")) score += 0.2;
        if (Regex.Matches(textLower, @"join.*join", RegexOptions.IgnoreCase).Count > 0) score += 0.15;

        // Aggregation complexity
        if (textLower.Contains("group by")) score += 0.15;
        if (textLower.Contains("having")) score += 0.1;

        // Subquery complexity
        var subqueryMatches = Regex.Matches(textLower, @"\(select.*from", RegexOptions.IgnoreCase);
        score += subqueryMatches.Count * 0.1;

        // Window functions
        if (Regex.IsMatch(textLower, @"\bover\s*\(", RegexOptions.IgnoreCase)) score += 0.15;
        if (Regex.IsMatch(textLower, @"\b(row_number|rank|dense_rank)\s*\(", RegexOptions.IgnoreCase)) score += 0.1;

        // Multiple conditions
        if (Regex.Matches(textLower, @"\bwhere\b.*\band\b", RegexOptions.IgnoreCase).Count >= 2) score += 0.1;

        return Math.Min(score, 1.0);
    }

    private PipelineRoute DetermineRoute(IntentCategory intent, RiskLevel risk, double complexity)
    {
        // Check for forbidden operations
        if (intent == IntentCategory.Forbidden)
            return PipelineRoute.Forbidden;

        // Check for off-topic or unknown
        if (intent == IntentCategory.OffTopic || intent == IntentCategory.Unknown)
            return PipelineRoute.Reject;

        // Route based on intent category
        return intent switch
        {
            IntentCategory.Query => PipelineRoute.Query,
            IntentCategory.Insert or IntentCategory.Update => risk >= RiskLevel.High 
                ? PipelineRoute.Dml 
                : PipelineRoute.Query,
            IntentCategory.Delete => PipelineRoute.Dml,
            IntentCategory.Upsert => PipelineRoute.Dml,
            IntentCategory.DdlIndex or IntentCategory.DdlProcedure or 
                IntentCategory.DdlAlter or IntentCategory.DdlView => PipelineRoute.Ddl,
            _ => PipelineRoute.Query
        };
    }

    private double CalculateConfidence(List<string> matchedKeywords, string text)
    {
        if (matchedKeywords.Count == 0)
            return 0.3; // Low confidence for unknown

        if (matchedKeywords.Count >= 3)
            return 0.95; // High confidence for multiple matches

        return 0.6 + (matchedKeywords.Count * 0.1);
    }

    private PipelineRoute GetRouteFromIntent(IntentCategory intent)
    {
        return intent switch
        {
            IntentCategory.Query => PipelineRoute.Query,
            IntentCategory.Insert or IntentCategory.Update or IntentCategory.Delete or IntentCategory.Upsert => PipelineRoute.Dml,
            IntentCategory.DdlIndex or IntentCategory.DdlProcedure or IntentCategory.DdlAlter or IntentCategory.DdlView => PipelineRoute.Ddl,
            IntentCategory.Forbidden => PipelineRoute.Forbidden,
            _ => PipelineRoute.Reject
        };
    }

    private List<string> ExtractEntities(string text)
    {
        var entities = new List<string>();

        // Extract potential table names (PascalCase or snake_case)
        var tableMatches = Regex.Matches(text, @"\b[A-Z][a-z]+(?:[A-Z][a-z]+)*\b");
        entities.AddRange(tableMatches.Select(m => m.Value));

        // Extract potential column names
        var columnMatches = Regex.Matches(text, @"\b[a-z_]+\b");
        entities.AddRange(columnMatches.Select(m => m.Value).Take(10));

        return entities.Distinct().ToList();
    }

    /// <summary>
    /// Generate safe alternative suggestions for DML/DDL operations
    /// </summary>
    private List<SafeAlternative> GenerateSafeAlternatives(IntentCategory intent, string text)
    {
        var alternatives = new List<SafeAlternative>();

        switch (intent)
        {
            case IntentCategory.Insert:
                alternatives.Add(new SafeAlternative
                {
                    Type = SafeAlternativeType.AuditLog,
                    Title = "Xem trước dữ liệu hiện tại",
                    Description = "Sử dụng SELECT để xem dữ liệu trước khi thêm mới",
                    ExampleSql = "SELECT * FROM TableName WHERE condition ORDER BY CreatedDate DESC"
                });
                break;

            case IntentCategory.Update:
                alternatives.Add(new SafeAlternative
                {
                    Type = SafeAlternativeType.AuditLog,
                    Title = "Xem trước bản ghi sẽ thay đổi",
                    Description = "SELECT với WHERE tương tự để xem trước ảnh hưởng",
                    ExampleSql = "SELECT * FROM TableName WHERE condition -- Preview before UPDATE"
                });
                break;

            case IntentCategory.Delete:
                alternatives.Add(new SafeAlternative
                {
                    Type = SafeAlternativeType.SoftDelete,
                    Title = "Sử dụng Soft Delete",
                    Description = "Thay vì xóa, cập nhật cột is_deleted = 1",
                    ExampleSql = "UPDATE TableName SET is_deleted = 1, DeletedAt = GETDATE() WHERE condition"
                });
                alternatives.Add(new SafeAlternative
                {
                    Type = SafeAlternativeType.Archive,
                    Title = "Archive trước khi xóa",
                    Description = "Di chuyển dữ liệu sang bảng lưu trữ trước",
                    ExampleSql = "INSERT INTO ArchiveTable SELECT * FROM TableName WHERE condition"
                });
                break;

            case IntentCategory.DdlAlter:
                alternatives.Add(new SafeAlternative
                {
                    Type = SafeAlternativeType.AuditLog,
                    Title = "Review schema hiện tại",
                    Description = "Xem cấu trúc bảng trước khi thay đổi",
                    ExampleSql = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TableName'"
                });
                break;
        }

        return alternatives;
    }

    /// <summary>
    /// Generate user-friendly rejection message
    /// </summary>
    private string GenerateFallbackRejectionMessage(IntentCategory intent, RiskLevel risk, string language)
    {
        if (language == "vi")
        {
            return intent switch
            {
                IntentCategory.Insert => "⚠️ Hệ thống chỉ hỗ trợ truy vấn đọc (SELECT). Để thêm dữ liệu, vui lòng sử dụng giao diện ứng dụng hoặc liên hệ admin.",
                IntentCategory.Update => "⚠️ Cập nhật dữ liệu không được phép trong chế độ truy vấn. Vui lòng liên hệ admin để được hỗ trợ.",
                IntentCategory.Delete => "⚠️ Xóa dữ liệu là thao tác nguy hiểm. Hệ thống khuyến nghị sử dụng soft delete (đánh dấu xóa) thay vì xóa vĩnh viễn.",
                IntentCategory.DdlIndex or IntentCategory.DdlProcedure or IntentCategory.DdlAlter or IntentCategory.DdlView =>
                    "⚠️ Thay đổi cấu trúc database cần được DBA review và approve. Vui lòng tạo ticket yêu cầu.",
                IntentCategory.Forbidden => "🚫 Thao tác này bị cấm vì lý do bảo mật. Vui lòng liên hệ admin.",
                _ => "❓ Tôi không hiểu yêu cầu của bạn. Vui lòng diễn đạt lại câu hỏi."
            };
        }
        else
        {
            return intent switch
            {
                IntentCategory.Insert => "⚠️ This system only supports read queries (SELECT). Please use the application interface or contact admin to add data.",
                IntentCategory.Update => "⚠️ Data updates are not allowed in query mode. Please contact admin for assistance.",
                IntentCategory.Delete => "⚠️ Deleting data is a dangerous operation. The system recommends using soft delete instead of permanent deletion.",
                IntentCategory.DdlIndex or IntentCategory.DdlProcedure or IntentCategory.DdlAlter or IntentCategory.DdlView =>
                    "⚠️ Database structure changes require DBA review and approval. Please create a ticket request.",
                IntentCategory.Forbidden => "🚫 This operation is forbidden for security reasons. Please contact admin.",
                _ => "❓ I don't understand your request. Please rephrase your question."
            };
        }
    }
}

/// <summary>
/// Result of intent routing analysis
/// </summary>
public class IntentRoutingResult
{
    /// <summary>Original user input</summary>
    public string OriginalInput { get; set; } = string.Empty;

    /// <summary>Normalized and expanded input</summary>
    public string NormalizedInput { get; set; } = string.Empty;

    /// <summary>Detected language (vi/en)</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Intent classification result</summary>
    public IntentClassificationResult Intent { get; set; } = new();

    /// <summary>Ambiguity detection result</summary>
    public AmbiguityResult Ambiguity { get; set; } = new();

    /// <summary>Risk level assessment</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    /// <summary>Complexity score (0.0 - 1.0)</summary>
    public double ComplexityScore { get; set; }

    /// <summary>Recommended pipeline route</summary>
    public PipelineRoute Route { get; set; }

    /// <summary>Analysis timestamp</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Warnings about potential issues</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Safe alternative suggestions for DML/DDL operations</summary>
    public List<SafeAlternative> SafeAlternatives { get; set; } = new();

    /// <summary>User-friendly rejection message</summary>
    public string RejectionMessage { get; set; } = string.Empty;
}

/// <summary>
/// Ambiguity detection result
/// </summary>
public class AmbiguityResult
{
    /// <summary>Whether the input is ambiguous</summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>Reasons for ambiguity</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>Suggestions to resolve ambiguity</summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>Confidence in ambiguity detection</summary>
    public double Confidence { get; set; }
}
