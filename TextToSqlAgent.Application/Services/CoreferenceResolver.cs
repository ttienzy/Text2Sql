using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Resolves pronouns and references in follow-up questions using conversation context
/// Enables natural multi-turn conversations: "Show products" → "How many of them?"
/// </summary>
public class CoreferenceResolver
{
    private readonly ILogger<CoreferenceResolver> _logger;

    // Pronouns to detect (English + Vietnamese)
    private static readonly string[] EnglishPronouns =
    {
        "them", "those", "these", "that", "it", "they",
        "the same", "similar", "like that", "such"
    };

    private static readonly string[] VietnamesePronouns =
    {
        "chúng", "những cái đó", "những cái này", "cái đó", "cái này",
        "nó", "chúng nó", "như vậy", "tương tự", "giống vậy"
    };

    public CoreferenceResolver(ILogger<CoreferenceResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if question contains pronouns that need resolution
    /// </summary>
    public bool ContainsPronouns(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return false;

        var lowerQuestion = question.ToLowerInvariant();

        // Check English pronouns
        foreach (var pronoun in EnglishPronouns)
        {
            if (lowerQuestion.Contains(pronoun))
            {
                _logger.LogDebug("[CoreferenceResolver] Detected English pronoun: '{Pronoun}'", pronoun);
                return true;
            }
        }

        // Check Vietnamese pronouns
        foreach (var pronoun in VietnamesePronouns)
        {
            if (lowerQuestion.Contains(pronoun))
            {
                _logger.LogDebug("[CoreferenceResolver] Detected Vietnamese pronoun: '{Pronoun}'", pronoun);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolve pronouns and rewrite question to be explicit
    /// </summary>
    public string ResolveAndRewrite(string question, ConversationContext context)
    {
        if (string.IsNullOrWhiteSpace(question) || context.History.Count == 0)
        {
            return question;
        }

        var lastTurn = context.History.Last();
        var rewritten = question;

        // Get primary entity from last turn
        var primaryEntity = lastTurn.PrimaryEntity ?? lastTurn.TargetTable;

        if (string.IsNullOrEmpty(primaryEntity))
        {
            _logger.LogWarning("[CoreferenceResolver] No primary entity found in context, cannot resolve pronouns");
            return question;
        }

        _logger.LogInformation("[CoreferenceResolver] 🔍 Resolving pronouns with context entity: {Entity}", primaryEntity);

        // Detect language
        var isVietnamese = ContainsVietnamese(question);

        if (isVietnamese)
        {
            rewritten = ResolveVietnamesePronouns(question, primaryEntity);
        }
        else
        {
            rewritten = ResolveEnglishPronouns(question, primaryEntity);
        }

        if (rewritten != question)
        {
            _logger.LogInformation(
                "[CoreferenceResolver] ✏️ Rewritten: '{Original}' → '{Rewritten}'",
                question,
                rewritten);
        }

        return rewritten;
    }

    /// <summary>
    /// Resolve English pronouns
    /// </summary>
    private string ResolveEnglishPronouns(string question, string entity)
    {
        var rewritten = question;

        // "them" → entity name
        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bof them\b",
            $"of {entity}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bthem\b",
            entity,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // "those" → entity name
        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bthose\b",
            entity,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // "these" → entity name
        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bthese\b",
            entity,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // "it" → entity name (careful with this one)
        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bit\b",
            entity,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // "they" → entity name
        rewritten = System.Text.RegularExpressions.Regex.Replace(
            rewritten,
            @"\bthey\b",
            entity,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return rewritten;
    }

    /// <summary>
    /// Resolve Vietnamese pronouns
    /// </summary>
    private string ResolveVietnamesePronouns(string question, string entity)
    {
        var rewritten = question;

        // "chúng" → entity name
        rewritten = rewritten.Replace("chúng", entity);
        rewritten = rewritten.Replace("Chúng", entity);

        // "những cái đó" → entity name
        rewritten = rewritten.Replace("những cái đó", entity);
        rewritten = rewritten.Replace("Những cái đó", entity);

        // "những cái này" → entity name
        rewritten = rewritten.Replace("những cái này", entity);
        rewritten = rewritten.Replace("Những cái này", entity);

        // "cái đó" → entity name
        rewritten = rewritten.Replace("cái đó", entity);
        rewritten = rewritten.Replace("Cái đó", entity);

        // "nó" → entity name
        rewritten = rewritten.Replace("nó", entity);
        rewritten = rewritten.Replace("Nó", entity);

        return rewritten;
    }

    /// <summary>
    /// Check if text contains Vietnamese characters
    /// </summary>
    private bool ContainsVietnamese(string text)
    {
        var vietnameseKeywords = new[] { "bao nhiêu", "hiển thị", "có", "là", "của", "trong", "với" };
        var lowerText = text.ToLowerInvariant();

        return vietnameseKeywords.Any(keyword => lowerText.Contains(keyword));
    }

    /// <summary>
    /// Get detected pronouns for logging/debugging
    /// </summary>
    public List<string> GetDetectedPronouns(string question)
    {
        var detected = new List<string>();

        if (string.IsNullOrWhiteSpace(question))
            return detected;

        var lowerQuestion = question.ToLowerInvariant();

        foreach (var pronoun in EnglishPronouns.Concat(VietnamesePronouns))
        {
            if (lowerQuestion.Contains(pronoun))
            {
                detected.Add(pronoun);
            }
        }

        return detected;
    }
}
