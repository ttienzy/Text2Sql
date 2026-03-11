using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for exploring database schema with advanced schema linking
/// </summary>
public class SchemaExplorerTool : ITool
{
    private readonly SchemaScanner _schemaScanner;
    private readonly AdvancedSchemaLinker _advancedLinker;
    private readonly SchemaRetriever _fallbackRetriever;
    private readonly ILogger<SchemaExplorerTool> _logger;
    private readonly bool _useAdvancedLinking;

    public string Name => "explore_schema";

    public string Description => @"Explore database schema to find relevant tables and columns for a question.
Uses advanced schema linking with entity recognition, hybrid search, and relationship inference.
Input: query (string) - the user's question
Output: Relevant schema information including tables, columns, and relationships";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new()
            {
                Name = "query",
                Type = "string",
                Description = "The user's natural language question",
                Required = true
            }
        }
    };

    public SchemaExplorerTool(
        SchemaScanner schemaScanner,
        AdvancedSchemaLinker advancedLinker,
        SchemaRetriever fallbackRetriever,
        ILogger<SchemaExplorerTool> logger,
        bool useAdvancedLinking = true)
    {
        _schemaScanner = schemaScanner;
        _advancedLinker = advancedLinker;
        _fallbackRetriever = fallbackRetriever;
        _logger = logger;
        _useAdvancedLinking = useAdvancedLinking;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var query = input.GetString("query", "question");
            _logger.LogInformation("[SchemaExplorerTool] Exploring schema for: {Query}", query);

            // Scan schema (cached)
            var fullSchema = await _schemaScanner.ScanAsync(ct);

            // Use advanced linking or fallback to basic RAG
            var relevantSchema = _useAdvancedLinking
                ? await _advancedLinker.LinkSchemaAsync(query, fullSchema, topK: 5, ct)
                : await _fallbackRetriever.RetrieveAsync(query, fullSchema, ct);

            _logger.LogInformation("[SchemaExplorerTool] Found {Tables} relevant tables, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            return ToolResult.FromSuccess(relevantSchema);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaExplorerTool] Error exploring schema");
            return ToolResult.FromError($"Failed to explore schema: {ex.Message}");
        }
    }
}
