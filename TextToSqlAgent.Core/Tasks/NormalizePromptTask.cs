using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Tasks;

public class NormalizePromptTask : IAgentTask<string, NormalizedPrompt>
{
    private readonly ILogger<NormalizePromptTask> _logger;

    // Vietnamese character mapping for common typos
    private static readonly Dictionary<string, string> VietnameseNormalization = new()
    {
        { "á", "á" }, { "à", "à" }, { "ả", "ả" }, { "ã", "ã" }, { "ạ", "ạ" },
        { "ă", "ă" }, { "ắ", "ắ" }, { "ằ", "ằ" }, { "ẳ", "ẳ" }, { "ẵ", "ẵ" }, { "ặ", "ặ" },
        { "â", "â" }, { "ấ", "ấ" }, { "ầ", "ầ" }, { "ẩ", "ẩ" }, { "ẫ", "ẫ" }, { "ậ", "ậ" },
        { "đ", "đ" },
        { "é", "é" }, { "è", "è" }, { "ẻ", "ẻ" }, { "ẽ", "ẽ" }, { "ẹ", "ẹ" },
        { "ê", "ê" }, { "ế", "ế" }, { "ề", "ề" }, { "ể", "ể" }, { "ễ", "ễ" }, { "ệ", "ệ" },
        { "í", "í" }, { "ì", "ì" }, { "ỉ", "ỉ" }, { "ĩ", "ĩ" }, { "ị", "ị" },
        { "ó", "ó" }, { "ò", "ò" }, { "ỏ", "ỏ" }, { "õ", "õ" }, { "ọ", "ọ" },
        { "ô", "ô" }, { "ố", "ố" }, { "ồ", "ồ" }, { "ổ", "ổ" }, { "ỗ", "ỗ" }, { "ộ", "ộ" },
        { "ơ", "ơ" }, { "ớ", "ớ" }, { "ờ", "ờ" }, { "ở", "ở" }, { "ỡ", "ỡ" }, { "ợ", "ợ" },
        { "ú", "ú" }, { "ù", "ù" }, { "ủ", "ủ" }, { "ũ", "ũ" }, { "ụ", "ụ" },
        { "ư", "ư" }, { "ứ", "ứ" }, { "ừ", "ừ" }, { "ử", "ử" }, { "ữ", "ữ" }, { "ự", "ự" },
        { "ý", "ý" }, { "ỳ", "ỳ" }, { "ỷ", "ỷ" }, { "ỹ", "ỹ" }, { "ỵ", "ỵ" }
    };

    // Common abbreviations in Vietnamese/English
    private static readonly Dictionary<string, string> Abbreviations = new()
    {
        { @"\bdb\b", "database" },
        { @"\bds\b", "danh sách" },
        { @"\btb\b", "bảng" },
        { @"\bkh\b", "khách hàng" },
        { @"\bdh\b", "đơn hàng" },
        { @"\bsp\b", "sản phẩm" },
        { @"\bdt\b", "doanh thu" },
        { @"\bsl\b", "số lượng" }
    };

    public NormalizePromptTask(ILogger<NormalizePromptTask> logger)
    {
        _logger = logger;
    }

    public async Task<NormalizedPrompt> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Agent] Nhận prompt từ người dùng");
        _logger.LogDebug("[Agent] Prompt gốc: {RawPrompt}", input);

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Prompt cannot be empty", nameof(input));
        }

        // Step 1: Basic normalization
        var normalized = input.Trim();

        // Step 2: Normalize whitespace (multiple spaces -> single space)
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // Step 3: Expand abbreviations
        normalized = ExpandAbbreviations(normalized);

        // Step 4: Fix common typos
        normalized = FixCommonTypos(normalized);

        // Step 5: Detect language
        var language = DetectLanguage(normalized);

        _logger.LogInformation("[Agent] Prompt sau chuẩn hóa: {Normalized}", normalized);
        _logger.LogDebug("[Agent] Ngôn ngữ phát hiện: {Language}", language);

        return await Task.FromResult(new NormalizedPrompt
        {
            OriginalPrompt = input,
            NormalizedText = normalized,
            Language = language,
            Timestamp = DateTime.UtcNow
        });
    }

    private string ExpandAbbreviations(string text)
    {
        foreach (var (pattern, replacement) in Abbreviations)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }
        return text;
    }

    private string FixCommonTypos(string text)
    {
        // Common typo fixes
        var typos = new Dictionary<string, string>
        {
            { "cho toi", "cho tôi" },
            { "bao nhieu", "bao nhiêu" },
            { "tat ca", "tất cả" },
            { "tim kiem", "tìm kiếm" }
        };

        foreach (var (typo, correct) in typos)
        {
            text = Regex.Replace(text, typo, correct, RegexOptions.IgnoreCase);
        }

        return text;
    }

    private string DetectLanguage(string text)
    {
        // Simple heuristic: check for Vietnamese characters
        var vietnameseChars = new[] { 'ă', 'â', 'đ', 'ê', 'ô', 'ơ', 'ư',
                                       'á', 'à', 'ả', 'ã', 'ạ',
                                       'ắ', 'ằ', 'ẳ', 'ẵ', 'ặ',
                                       'ấ', 'ầ', 'ẩ', 'ẫ', 'ậ' };

        var hasVietnamese = text.Any(c => vietnameseChars.Contains(char.ToLower(c)));

        return hasVietnamese ? "vi" : "en";
    }
}