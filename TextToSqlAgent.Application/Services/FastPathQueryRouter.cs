using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Fast-path query router using heuristics to avoid unnecessary LLM calls
/// Handles greetings, thanks, and obvious out-of-scope queries instantly
/// </summary>
public class FastPathQueryRouter : IQueryRouter
{
    private readonly ILogger<FastPathQueryRouter> _logger;

    // Fast-path patterns (no LLM needed)
    private static readonly HashSet<string> GreetingPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "hi", "hey", "xin chào", "chào", "good morning", "good afternoon",
        "good evening", "chao", "xin chao", "helo", "halo"
    };

    private static readonly HashSet<string> ThanksPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "thanks", "thank you", "cảm ơn", "cám ơn", "cam on", "bye", "goodbye",
        "tạm biệt", "tam biet", "see you", "ok", "okay"
    };

    private static readonly string[] OutOfScopeKeywords =
    {
        "weather", "thời tiết", "thoi tiet",
        "news", "tin tức", "tin tuc",
        "president", "tổng thống", "tong thong",
        "calculate", "tính toán", "tinh toan",
        "math", "toán", "toan",
        "recipe", "công thức", "cong thuc",
        "movie", "phim",
        "music", "nhạc", "nhac",
        "game", "trò chơi", "tro choi"
    };

    // ✅ Vietnamese database keywords - if present, definitely NOT out-of-scope
    private static readonly string[] VietnameseDatabaseKeywords =
    {
        "hiển thị", "hien thi", "lấy", "lay", "lọc", "loc", "tìm", "tim",
        "đếm", "dem", "tổng", "tong", "trung bình", "trung binh",
        "so sánh", "so sanh", "doanh thu", "doanh thu", "đơn hàng", "don hang",
        "khách hàng", "khach hang", "sản phẩm", "san pham", "nhân viên", "nhan vien",
        "báo cáo", "bao cao", "thống kê", "thong ke", "danh sách", "danh sach",
        "theo", "nhóm", "nhom", "sắp xếp", "sap xep", "tháng", "thang",
        "năm", "nam", "quý", "quy", "trạng thái", "trang thai",
        "phương thức", "phuong thuc", "thanh toán", "thanh toan"
    };

    public FastPathQueryRouter(ILogger<FastPathQueryRouter> logger)
    {
        _logger = logger;
    }

    public Task<QueryRoute> RouteAsync(
        string question,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = question.Trim().ToLowerInvariant();

        _logger.LogDebug("[FastPathRouter] Routing: {Question}", question);

        // Fast path: Greeting
        if (IsGreeting(normalized))
        {
            _logger.LogInformation("[FastPathRouter] Detected greeting (fast path)");

            return Task.FromResult(new QueryRoute
            {
                Type = RouteType.Greeting,
                DirectResponse = "Hello! I'm your database assistant. Ask me anything about your data.",
                Confidence = 1.0,
                Reason = "Greeting detected",
                RequiresSchema = false,
                RequiresLLM = false
            });
        }

        // Fast path: Thanks/Goodbye
        if (IsThanks(normalized))
        {
            _logger.LogInformation("[FastPathRouter] Detected thanks/goodbye (fast path)");

            return Task.FromResult(new QueryRoute
            {
                Type = RouteType.Greeting,
                DirectResponse = "You're welcome! Feel free to ask more questions about your database.",
                Confidence = 1.0,
                Reason = "Thanks/goodbye detected",
                RequiresSchema = false,
                RequiresLLM = false
            });
        }

        // Fast path: Out of scope (heuristic)
        if (IsLikelyOutOfScope(normalized))
        {
            _logger.LogInformation("[FastPathRouter] Detected out-of-scope (fast path)");

            return Task.FromResult(new QueryRoute
            {
                Type = RouteType.OutOfScope,
                DirectResponse = "I'm a database assistant specialized in querying your data. " +
                                "I can't answer general questions about weather, news, or other topics. " +
                                "Please ask me about your database tables and data.",
                Confidence = 0.8,
                Reason = "Likely out of scope (heuristic)",
                RequiresSchema = false,
                RequiresLLM = false
            });
        }

        // Needs full pipeline (LLM validation + schema)
        _logger.LogInformation("[FastPathRouter] Routing to full pipeline (needs LLM validation)");

        return Task.FromResult(new QueryRoute
        {
            Type = RouteType.DatabaseQuery,  // Assume database query
            Confidence = 0.5,
            Reason = "Needs LLM validation",
            RequiresSchema = true,
            RequiresLLM = true
        });
    }

    private bool IsGreeting(string text)
    {
        // Check if text is ONLY a greeting (not part of a longer question)
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 3)
        {
            return false;  // Too long to be just a greeting
        }

        return GreetingPatterns.Any(p => text.Contains(p));
    }

    private bool IsThanks(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 5)
        {
            return false;  // Too long
        }

        return ThanksPatterns.Any(p => text.Contains(p));
    }

    private bool IsLikelyOutOfScope(string text)
    {
        // ✅ Check Vietnamese DB keywords FIRST - if present, definitely NOT out-of-scope
        if (VietnameseDatabaseKeywords.Any(kw => text.Contains(kw)))
        {
            _logger.LogDebug("[FastPathRouter] Vietnamese database keywords detected - NOT out-of-scope");
            return false;
        }

        // Heuristic: Check for out-of-scope keywords
        return OutOfScopeKeywords.Any(k => text.Contains(k));
    }
}
