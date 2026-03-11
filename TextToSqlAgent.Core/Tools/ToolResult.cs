namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Result from tool execution
/// </summary>
public class ToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static ToolResult FromSuccess(object? data = null)
    {
        return new ToolResult
        {
            Success = true,
            Data = data
        };
    }

    public static ToolResult FromError(string errorMessage)
    {
        return new ToolResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    public T GetData<T>()
    {
        if (Data == null)
            throw new InvalidOperationException("Data is null");

        return (T)Data;
    }
}
