namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for LLM (Large Language Model) client implementations.
/// Supports multiple providers (Gemini, OpenAI, etc.)
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Generate completion from a single prompt
    /// </summary>
    /// <param name="prompt">The prompt text to send to LLM</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated text response</returns>
    Task<string> CompleteAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate completion with separate system and user prompts
    /// </summary>
    /// <param name="systemPrompt">System instructions/context</param>
    /// <param name="userPrompt">User's actual prompt</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated text response</returns>
    Task<string> CompleteWithSystemPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
