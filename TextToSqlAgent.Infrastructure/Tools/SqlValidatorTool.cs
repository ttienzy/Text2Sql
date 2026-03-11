using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Tools;

public class SqlValidatorTool : ITool
{
    private readonly ILogger<SqlValidatorTool> _logger;

    public string Name => "validate_sql";
    public string Description => "Validate SQL safety (no DROP, DELETE, etc.)";
    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new() { Name = "sql", Type = "string", Description = "SQL to validate", Required = true }
        }
    };

    public SqlValidatorTool(ILogger<SqlValidatorTool> logger)
    {
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var sql = input.GetString("sql");
            var isValid = ValidateSql(sql);

            if (isValid)
                return ToolResult.FromSuccess(new { IsValid = true });
            else
                return ToolResult.FromError("SQL contains unsafe operations");
        }
        catch (Exception ex)
        {
            return ToolResult.FromError(ex.Message);
        }
    }

    private bool ValidateSql(string sql)
    {
        var upperSql = sql.ToUpper();
        var dangerousKeywords = new[] { "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC" };

        foreach (var keyword in dangerousKeywords)
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
                return false;

        return upperSql.Contains("SELECT");
    }
}
