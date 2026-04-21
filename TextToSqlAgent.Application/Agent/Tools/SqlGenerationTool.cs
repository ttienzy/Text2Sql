using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Application.Agent.Tools;

/// <summary>
/// Tool that generates SQL from a natural language question + schema context.
/// Uses the LLM directly with schema context from WorkingMemory.
/// </summary>
public class SqlGenerationTool : IAgentTool
{
    private readonly ILLMClient _llmClient;
    private readonly PromptRegistry _promptRegistry;
    private readonly ILogger<SqlGenerationTool> _logger;

    public string Name => "SqlGeneration";
    public string Description =>
        "Generate a SQL query from a natural language question. " +
        "Requires schema context to be available in memory (use SchemaLookup first). " +
        "Returns the generated SQL query.";

    public SqlGenerationTool(
        ILLMClient llmClient,
        PromptRegistry promptRegistry,
        ILogger<SqlGenerationTool> logger)
    {
        _llmClient = llmClient;
        _promptRegistry = promptRegistry;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, WorkingMemory memory, CancellationToken ct)
    {
        try
        {
            if (memory.SchemaContext == null)
            {
                return ToolResult.Fail("No schema context available. Use SchemaLookup first.");
            }

            // Build schema description for LLM
            var schemaDescription = BuildSchemaDescription(memory);

            var extraVariables = new Dictionary<string, object>();
            
            var (systemPrompt, userPrompt) = _promptRegistry.BuildSqlGenerationPrompt(
                input.Query,
                schemaDescription,
                new List<string>(),
                extraVariables);

            string sql;
            if (memory.SqlTokenCallback != null)
            {
                sql = await _llmClient.CompleteWithSystemPromptStreamAsync(
                    systemPrompt, userPrompt, memory.SqlTokenCallback, ct);
            }
            else
            {
                sql = await _llmClient.CompleteWithSystemPromptAsync(
                    systemPrompt, userPrompt, ct);
            }

            // Clean up SQL
            sql = sql.Replace("```sql", "").Replace("```", "").Trim().TrimEnd(';');

            memory.GeneratedSql = sql;

            _logger.LogInformation("[SqlGenerationTool] Generated SQL ({Length} chars)", sql.Length);
            return ToolResult.Ok($"Generated SQL:\n{sql}", sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlGenerationTool] Failed to generate SQL");
            return ToolResult.Fail($"SQL generation failed: {ex.Message}");
        }
    }

    private static string BuildSchemaDescription(WorkingMemory memory)
    {
        if (memory.SchemaContext == null) return "No schema available.";

        var lines = new List<string>();
        foreach (var table in memory.SchemaContext.RelevantTables)
        {
            var cols = string.Join(", ", table.Columns.Select(c =>
            {
                var pk = c.IsPrimaryKey ? " PK" : "";
                var fk = c.IsForeignKey ? " FK" : "";
                return $"{c.ColumnName} {c.DataType}{pk}{fk}";
            }));
            lines.Add($"{table.TableName}({cols})");
        }

        if (memory.SchemaContext.RelevantRelationships?.Count > 0)
        {
            var rels = string.Join(", ", memory.SchemaContext.RelevantRelationships.Select(r =>
                $"{r.FromTable}.{r.FromColumn}→{r.ToTable}.{r.ToColumn}"));
            lines.Add($"JOINs: {rels}");
        }

        return string.Join("\n", lines);
    }
}
