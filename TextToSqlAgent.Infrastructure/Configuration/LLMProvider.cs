namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Enum for LLM provider selection
/// </summary>
public enum LLMProvider
{
    /// <summary>
    /// Google Gemini API
    /// </summary>
    Gemini,

    /// <summary>
    /// OpenAI API (ChatGPT)
    /// </summary>
    OpenAI
}
