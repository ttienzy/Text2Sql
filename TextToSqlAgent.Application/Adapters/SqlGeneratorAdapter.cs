namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Plugins;

public class SqlGeneratorAdapter : ISqlGenerator
{
    private readonly SqlGeneratorPlugin _plugin;

    public SqlGeneratorAdapter(SqlGeneratorPlugin plugin)
    {
        _plugin = plugin;
    }

    public async Task<string> GenerateAsync(
        IntentAnalysisResult intent,
        RetrievedSchemaContext schema,
        CancellationToken ct = default)
    {
        // Map back to old IntentAnalysis model
        var oldIntent = new IntentAnalysis
        {
            Intent = intent.Intent,
            Target = intent.Target,
            Metrics = intent.Metrics, // Already List<MetricDefinition>
            Filters = intent.Filters.Select(f => new FilterCondition
            {
                Field = f.Field,
                Operator = f.Operator,
                Value = f.Value
            }).ToList(),
            NeedsClarification = intent.NeedsClarification,
            ClarificationQuestion = intent.ClarificationQuestion
        };

        return await _plugin.GenerateSqlWithContextAsync(oldIntent, schema, ct);
    }

    public bool ValidateSafety(string sql)
    {
        return _plugin.ValidateSql(sql);
    }

    public string EnsureLimit(string sql, int maxRows = 1000)
    {
        return _plugin.EnsureLimit(sql, maxRows);
    }
}
