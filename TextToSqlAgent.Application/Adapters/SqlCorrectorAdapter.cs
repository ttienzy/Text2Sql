namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Plugins;

public class SqlCorrectorAdapter : ISqlCorrector
{
    private readonly SqlCorrectorPlugin _plugin;

    public SqlCorrectorAdapter(SqlCorrectorPlugin plugin)
    {
        _plugin = plugin;
    }

    public async Task<CorrectionResult> CorrectAsync(
        string failedSql,
        string errorMessage,
        RetrievedSchemaContext schema,
        IntentAnalysisResult intent,
        int attemptNumber,
        CancellationToken ct = default)
    {
        // Map to old model
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
            }).ToList()
        };

        var correction = await _plugin.CorrectSqlAsync(
            failedSql,
            errorMessage,
            schema,
            oldIntent,
            attemptNumber,
            ct);

        return new CorrectionResult
        {
            Success = correction.Success,
            CorrectedSql = correction.CorrectedSql,
            Explanation = correction.Reasoning
        };
    }

    public bool ShouldRetry(List<CorrectionAttempt> corrections, int maxAttempts)
    {
        return _plugin.ShouldRetry(corrections, maxAttempts);
    }
}
