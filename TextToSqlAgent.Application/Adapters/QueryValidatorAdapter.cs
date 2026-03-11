namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Plugins;

public class QueryValidatorAdapter : IQueryValidator
{
    private readonly QueryValidatorPlugin _plugin;

    public QueryValidatorAdapter(QueryValidatorPlugin plugin)
    {
        _plugin = plugin;
    }

    public async Task<QueryValidationResult> ValidateAsync(
        string question,
        CancellationToken ct = default)
    {
        var result = await _plugin.ValidateQueryAsync(question, new List<string>(), ct);

        return new QueryValidationResult
        {
            IsRelevant = result.IsRelevant,
            NeedsClarification = result.NeedsClarification,
            SuggestedResponse = result.SuggestedResponse,
            ClarificationQuestion = result.ClarificationQuestion,
            QueryType = result.IsRelevant ? QueryType.DatabaseQuery : QueryType.OutOfScope,
            Confidence = result.Confidence
        };
    }
}
