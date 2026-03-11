namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Plugins;

/// <summary>
/// Adapter: Maps IntentAnalysisPlugin to IIntentAnalyzer port
/// </summary>
public class IntentAnalyzerAdapter : IIntentAnalyzer
{
    private readonly IntentAnalysisPlugin _plugin;

    public IntentAnalyzerAdapter(IntentAnalysisPlugin plugin)
    {
        _plugin = plugin;
    }

    public async Task<IntentAnalysisResult> AnalyzeAsync(
        string normalizedQuestion,
        List<string> availableTables,
        CancellationToken ct = default)
    {
        // Call existing plugin
        var oldResult = await _plugin.AnalyzeIntentAsync(
            normalizedQuestion,
            availableTables,
            ct);

        // Map to new rich model
        return new IntentAnalysisResult
        {
            Intent = oldResult.Intent,
            Complexity = QueryComplexity.Medium, // TODO: extract from LLM response
            Target = oldResult.Target,
            Metrics = oldResult.Metrics, // Already List<MetricDefinition>
            Filters = oldResult.Filters.Select(f => new FilterDefinition
            {
                Field = f.Field,
                Operator = f.Operator,
                Value = f.Value,
                ValueType = FilterValueType.Literal
            }).ToList(),
            NeedsClarification = oldResult.NeedsClarification,
            ClarificationQuestion = oldResult.ClarificationQuestion
        };
    }
}
