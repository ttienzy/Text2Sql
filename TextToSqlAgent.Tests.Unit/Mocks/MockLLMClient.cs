using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Tests.Unit.Mocks;

/// <summary>
/// P1-06: Mock LLM client for unit testing without real API calls
/// </summary>
public class MockLLMClient : ILLMClient
{
    private readonly Dictionary<string, string> _responses = new();
    private readonly Queue<string> _responseQueue = new();
    private string? _defaultResponse;

    /// <summary>
    /// Set a specific response for a prompt pattern
    /// </summary>
    public void SetResponse(string promptPattern, string response)
    {
        _responses[promptPattern] = response;
    }

    /// <summary>
    /// Set default response for any prompt
    /// </summary>
    public void SetDefaultResponse(string response)
    {
        _defaultResponse = response;
    }

    /// <summary>
    /// Queue multiple responses (FIFO)
    /// </summary>
    public void QueueResponse(string response)
    {
        _responseQueue.Enqueue(response);
    }

    /// <summary>
    /// Clear all configured responses
    /// </summary>
    public void Clear()
    {
        _responses.Clear();
        _responseQueue.Clear();
        _defaultResponse = null;
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // Check queue first
        if (_responseQueue.Count > 0)
        {
            return Task.FromResult(_responseQueue.Dequeue());
        }

        // Check pattern matches
        foreach (var (pattern, response) in _responses)
        {
            if (prompt.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(response);
            }
        }

        // Return default or throw
        if (_defaultResponse != null)
        {
            return Task.FromResult(_defaultResponse);
        }

        throw new InvalidOperationException(
            $"No mock response configured for prompt: {prompt.Substring(0, Math.Min(100, prompt.Length))}...");
    }

    public Task<string> CompleteWithSystemPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        // Combine prompts for matching
        var combinedPrompt = $"{systemPrompt}\n{userPrompt}";
        return CompleteAsync(combinedPrompt, cancellationToken);
    }
}
