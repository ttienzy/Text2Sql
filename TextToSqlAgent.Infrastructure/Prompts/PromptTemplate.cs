using System.Collections;
using System.Reflection;
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
        return RenderTemplate(UserPrompt, variables);
    }

    /// <summary>
    /// Render system prompt with variables
    /// </summary>
    public string RenderSystem(Dictionary<string, object> variables)
    {
        return RenderTemplate(SystemPrompt, variables);
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

    private static string RenderTemplate(string template, IDictionary<string, object> variables)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var rendered = template;
        rendered = RenderEachBlocks(rendered, variables);
        rendered = RenderConditionalBlocks(rendered, variables);
        rendered = RenderValueTokens(rendered, variables);
        return rendered;
    }

    private static string RenderEachBlocks(string template, IDictionary<string, object> variables)
    {
        const string pattern = @"\{\{#each\s+([^\s\}]+)\}\}(.*?)\{\{\/each\}\}";

        while (Regex.IsMatch(template, pattern, RegexOptions.Singleline))
        {
            template = Regex.Replace(template, pattern, match =>
            {
                var collectionName = match.Groups[1].Value;
                var block = match.Groups[2].Value;

                if (!TryResolveValue(collectionName, variables, out var value) || value is not IEnumerable enumerable || value is string)
                {
                    return string.Empty;
                }

                var renderedItems = new List<string>();
                foreach (var item in enumerable)
                {
                    var itemScope = CloneScope(variables);
                    itemScope["this"] = item ?? string.Empty;

                    if (item != null)
                    {
                        foreach (var property in ToPropertyMap(item))
                        {
                            itemScope[property.Key] = property.Value ?? string.Empty;
                        }
                    }

                    renderedItems.Add(RenderTemplate(block, itemScope));
                }

                return string.Join(string.Empty, renderedItems);
            }, RegexOptions.Singleline);
        }

        return template;
    }

    private static string RenderConditionalBlocks(string template, IDictionary<string, object> variables)
    {
        const string ifCondPattern = @"\{\{#ifCond\s+([^\s\}]+)\s+""([^""]+)""\s+""([^""]+)""\}\}(.*?)(?:\{\{else\}\}(.*?))?\{\{\/ifCond\}\}";
        const string ifPattern = @"\{\{#if\s+([^\s\}]+)\}\}(.*?)(?:\{\{else\}\}(.*?))?\{\{\/if\}\}";

        while (Regex.IsMatch(template, ifCondPattern, RegexOptions.Singleline))
        {
            template = Regex.Replace(template, ifCondPattern, match =>
            {
                var variableName = match.Groups[1].Value;
                var op = match.Groups[2].Value;
                var expected = match.Groups[3].Value;
                var truthyBlock = match.Groups[4].Value;
                var falsyBlock = match.Groups[5].Success ? match.Groups[5].Value : string.Empty;

                var actual = TryResolveValue(variableName, variables, out var value)
                    ? value?.ToString() ?? string.Empty
                    : string.Empty;

                var condition = op switch
                {
                    "===" or "==" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    "!==" or "!=" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };

                return RenderTemplate(condition ? truthyBlock : falsyBlock, CloneScope(variables));
            }, RegexOptions.Singleline);
        }

        while (Regex.IsMatch(template, ifPattern, RegexOptions.Singleline))
        {
            template = Regex.Replace(template, ifPattern, match =>
            {
                var variableName = match.Groups[1].Value;
                var truthyBlock = match.Groups[2].Value;
                var falsyBlock = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

                var hasValue = TryResolveValue(variableName, variables, out var value) && IsTruthy(value);
                return RenderTemplate(hasValue ? truthyBlock : falsyBlock, CloneScope(variables));
            }, RegexOptions.Singleline);
        }

        return template;
    }

    private static string RenderValueTokens(string template, IDictionary<string, object> variables)
    {
        template = Regex.Replace(template, @"\{\{\s*([^\{\}\s]+)\s*\}\}", match =>
        {
            var token = match.Groups[1].Value;
            return TryResolveValue(token, variables, out var value)
                ? ConvertToString(value)
                : string.Empty;
        });

        foreach (var kvp in variables)
        {
            var placeholder = $"{{{kvp.Key}}}";
            template = template.Replace(placeholder, ConvertToString(kvp.Value));
        }

        return template;
    }

    private static Dictionary<string, object> CloneScope(IDictionary<string, object> variables)
    {
        return variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static bool TryResolveValue(string path, IDictionary<string, object> variables, out object? value)
    {
        value = null;

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (!variables.TryGetValue(segments[0], out var current))
        {
            return false;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            if (!TryGetMemberValue(current, segments[i], out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryGetMemberValue(object? source, string memberName, out object? value)
    {
        value = null;
        if (source == null)
        {
            return false;
        }

        if (source is IDictionary<string, object> genericDictionary)
        {
            return genericDictionary.TryGetValue(memberName, out value);
        }

        if (source is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key?.ToString() == memberName)
                {
                    value = entry.Value;
                    return true;
                }
            }

            return false;
        }

        var property = source.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null)
        {
            return false;
        }

        value = property.GetValue(source);
        return true;
    }

    private static Dictionary<string, object?> ToPropertyMap(object source)
    {
        if (source is IDictionary<string, object> genericDictionary)
        {
            return genericDictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        if (source is IDictionary dictionary)
        {
            var mapped = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null)
                {
                    mapped[entry.Key.ToString()!] = entry.Value;
                }
            }

            return mapped;
        }

        return source.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name, p => p.GetValue(source));
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            bool b => b,
            IEnumerable enumerable when value is not string => enumerable.Cast<object?>().Any(),
            _ => true
        };
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            IEnumerable enumerable when value is not string => string.Join("\n", enumerable.Cast<object?>().Select(v => v?.ToString() ?? string.Empty)),
            _ => value.ToString() ?? string.Empty
        };
    }
}

public class FewShotExample
{
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}
