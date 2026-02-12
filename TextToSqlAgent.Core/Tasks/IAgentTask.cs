namespace TextToSqlAgent.Core.Tasks;

public interface IAgentTask<TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}