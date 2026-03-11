namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Schema defining tool's input parameters
/// </summary>
public class ToolSchema
{
    public List<ToolParameter> Parameters { get; set; } = new();

    public string ToJsonSchema()
    {
        // Simple JSON schema representation for LLM
        var parameters = string.Join(", ", Parameters.Select(p =>
            $"\"{p.Name}\": {{ \"type\": \"{p.Type}\", \"description\": \"{p.Description}\", \"required\": {p.Required.ToString().ToLower()} }}"));

        return $"{{ {parameters} }}";
    }
}

public class ToolParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // string, number, boolean, object, array
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
    public object? DefaultValue { get; set; }
}
