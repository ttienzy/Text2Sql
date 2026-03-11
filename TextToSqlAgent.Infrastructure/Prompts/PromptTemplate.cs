using System.Text.RegularExpressions;

namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// Represents a versioned prompt template
/// </summary>
public class PromptTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Model { get; set; } = "gpt-4o";
    public double Temperature { get; set; } = 0.1;
    public int MaxTokens { get; set; } = 2048;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public List<FewShotExample> FewShotExamples { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Render template with variables
    /// </summary>
    public string Render(Dictionary<string, object> variables)
    {
        var rendered = UserPrompt;

        foreach (var kvp in variables)
        {
            var placeholder = $"{{{kvp.Key}}}";
            var value = kvp.Value?.ToString() ?? "";
            rendered = rendered.Replace(placeholder, value);
        }

        return rendered;
    }

    /// <summary>
    /// Render system prompt with variables
    /// </summary>
    public string RenderSystem(Dictionary<string, object> variables)
    {
        var rendered = SystemPrompt;

        foreach (var kvp in variables)
        {
            var placeholder = $"{{{kvp.Key}}}";
            var value = kvp.Value?.ToString() ?? "";
            rendered = rendered.Replace(placeholder, value);
        }

        return rendered;
    }

    /// <summary>
    /// Get full prompt with few-shot examples
    /// </summary>
    public string GetFullPrompt(Dictionary<string, object> variables, bool includeFewShot = true)
    {
        var parts = new List<string>();

        // Add system prompt
        if (!string.IsNullOrEmpty(SystemPrompt))
        {
            parts.Add(RenderSystem(variables));
        }

        // Add few-shot examples
        if (includeFewShot && FewShotExamples.Count > 0)
        {
            parts.Add("\n# Examples:\n");
            foreach (var example in FewShotExamples)
            {
                parts.Add($"Q: {example.Input}");
                parts.Add($"A: {example.Output}\n");
            }
        }

        // Add user prompt
        parts.Add(Render(variables));

        return string.Join("\n", parts);
    }
}

public class FewShotExample
{
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}
