namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

/// <summary>
/// Adapter: Wraps ConversationManager to implement IConversationStore port
/// </summary>
public class ConversationStoreAdapter : IConversationStore
{
    private readonly ConversationManager _manager;

    public ConversationStoreAdapter(ConversationManager manager)
    {
        _manager = manager;
    }

    public ConversationContext GetOrCreate(string? conversationId = null)
    {
        return _manager.GetOrCreateContext(conversationId);
    }

    public void AddTurn(ConversationContext context, ConversationTurn turn)
    {
        _manager.AddTurn(
            context,
            turn.UserQuestion,
            turn.SystemResponse,
            turn.SqlQuery,
            turn.Intent,
            turn.TargetTable,
            turn.Success);
    }

    public string EnrichQuestion(ConversationContext context, string question)
    {
        return _manager.EnrichQuestionWithContext(context, question);
    }

    public void Clear(string conversationId)
    {
        _manager.ClearContext(conversationId);
    }

    public int GetActiveCount()
    {
        return _manager.GetActiveConversationCount();
    }
}
