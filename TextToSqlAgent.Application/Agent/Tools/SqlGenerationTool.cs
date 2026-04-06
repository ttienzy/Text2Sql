using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Application.Agent.Tools;

/// <summary>
/// Tool that generates SQL from a natural language question + schema context.
/// Uses the LLM directly with schema context from WorkingMemory.
/// </summary>
public class SqlGenerationTool : IAgentTool
{
    private readonly ILLMClient _llmClient;
    private readonly IDatabaseAdapter _databaseAdapter;
    private readonly ILogger<SqlGenerationTool> _logger;

    public string Name => "SqlGeneration";
    public string Description =>
        "Generate a SQL query from a natural language question. " +
        "Requires schema context to be available in memory (use SchemaLookup first). " +
        "Returns the generated SQL query.";

    public SqlGenerationTool(
        ILLMClient llmClient,
        IDatabaseAdapter databaseAdapter,
        ILogger<SqlGenerationTool> logger)
    {
        _llmClient = llmClient;
        _databaseAdapter = databaseAdapter;
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

            var systemPrompt = _databaseAdapter.GetSystemPrompt() + @"

You are a SQL query generator. Given a user question and database schema, generate ONLY the SQL query.
Return ONLY the raw SQL, no explanations, no markdown code blocks.
Always use SELECT only. Never use INSERT, UPDATE, DELETE, DROP.
Add TOP 100 or LIMIT 100 if not specified.";

            var userPrompt = $"""
                Schema:
                {schemaDescription}

                Question: {input.Query}

                Generate the SQL query:
                """;

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
