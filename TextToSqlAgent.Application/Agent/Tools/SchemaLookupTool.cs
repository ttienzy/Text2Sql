using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;

namespace TextToSqlAgent.Application.Agent.Tools;

/// <summary>
/// Tool that retrieves relevant schema context for a natural language question.
/// Wraps the existing SchemaRetriever's hybrid search (vector + keyword + graph traversal).
/// </summary>
public class SchemaLookupTool : IAgentTool
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly ILogger<SchemaLookupTool> _logger;

    public string Name => "SchemaLookup";
    public string Description =>
        "Look up database schema information relevant to a question. " +
        "Returns tables, columns, relationships, and data types that match the query. " +
        "Use this when you need to understand the database structure before generating SQL.";

    public SchemaLookupTool(IAgentServiceFactory serviceFactory, ILogger<SchemaLookupTool> logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, WorkingMemory memory, CancellationToken ct)
    {
        try
        {
            var schemaRetriever = _serviceFactory.GetSchemaRetriever();

            // Use full schema from memory if available, otherwise scan
            if (memory.FullSchema == null)
            {
                var scanner = _serviceFactory.GetSchemaScanner();
                memory.FullSchema = await scanner.ScanAsync(ct);
            }

            var context = await schemaRetriever.RetrieveAsync(
                input.Query, memory.FullSchema, ct);

            memory.SchemaContext = context;

            // Track discovered tables
            if (context?.RelevantTables != null)
            {
                foreach (var table in context.RelevantTables)
                {
                    memory.DiscoveredTables.Add(table.TableName);
                }
            }

            var tableCount = context?.RelevantTables?.Count ?? 0;
            var summary = $"Found {tableCount} relevant tables: " +
                string.Join(", ", context?.RelevantTables?.Select(t => t.TableName) ?? Array.Empty<string>());

            _logger.LogInformation("[SchemaLookupTool] {Summary}", summary);
            return ToolResult.Ok(summary, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaLookupTool] Failed to retrieve schema");
            return ToolResult.Fail($"Schema lookup failed: {ex.Message}");
        }
    }
}
