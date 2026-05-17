using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Infrastructure.Tools;

public class SqlGeneratorTool : ITool
{
    private readonly ILLMClient _llm;
    private readonly PromptRegistry _promptRegistry;
    private readonly DatabaseConfig _databaseConfig;
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

    public SqlGeneratorTool(
        ILLMClient llm,
        PromptRegistry promptRegistry,
        DatabaseConfig databaseConfig,
        ILogger<SqlGeneratorTool> logger)
    {
        _llm = llm;
        _promptRegistry = promptRegistry;
        _databaseConfig = databaseConfig;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var question = input.GetString("question", "query");
            var schemaContext = input.Get<RetrievedSchemaContext>("schema_context");

            var schemaText = BuildSchemaContext(schemaContext);

            // ✅ T7 MULTI-DB: Select system prompt based on current provider (AsyncLocal aware)
            var provider = _databaseConfig.Provider;
            var (systemPrompt, userPrompt) = _promptRegistry.BuildSqlGenerationPromptForProvider(
                question,
                schemaText,
                provider,
                new List<string>(),
                new Dictionary<string, object>());

            _logger.LogDebug("[SqlGeneratorTool] Using {Provider} system prompt for SQL generation", provider);

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
