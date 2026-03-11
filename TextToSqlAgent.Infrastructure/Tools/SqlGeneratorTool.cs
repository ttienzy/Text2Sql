using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Tools;

public class SqlGeneratorTool : ITool
{
    private readonly ILLMClient _llm;
    private readonly IDatabaseAdapter _adapter;
    private readonly ILogger<SqlGeneratorTool> _logger;

    public string Name => "generate_sql";
    public string Description => "Generate SQL query from natural language with schema context";
    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new() { Name = "question", Type = "string", Description = "User question", Required = true },
            new() { Name = "schema_context", Type = "object", Description = "Schema context", Required = true }
        }
    };

    public SqlGeneratorTool(ILLMClient llm, IDatabaseAdapter adapter, ILogger<SqlGeneratorTool> logger)
    {
        _llm = llm;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var question = input.GetString("question", "query");
            var schemaContext = input.Get<RetrievedSchemaContext>("schema_context");

            var schemaText = BuildSchemaContext(schemaContext);
            var systemPrompt = _adapter.GetSystemPrompt();
            var userPrompt = $"Question: {question}\n\nSchema:\n{schemaText}\n\nGenerate SQL:";

            var sql = await _llm.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);
            sql = sql.Replace("```sql", "").Replace("```", "").Trim();

            return ToolResult.FromSuccess(sql);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError(ex.Message);
        }
    }

    private string BuildSchemaContext(RetrievedSchemaContext context)
    {
        // Compact format: Table(col1 type1, col2 type2)
        var tables = new List<string>();
        foreach (var table in context.RelevantTables)
        {
            var cols = string.Join(", ", table.Columns.Select(c => $"{c.ColumnName} {c.DataType}"));
            tables.Add($"{table.TableName}({cols})");
        }
        return string.Join("\n", tables);
    }
}
