namespace TextToSqlAgent.Core.Ports;

using TextToSqlAgent.Core.Models;

/// <summary>
/// Port: Query validation and routing
/// </summary>
public interface IQueryValidator
{
    Task<QueryValidationResult> ValidateAsync(
        string question,
        CancellationToken ct = default);
}

/// <summary>
/// Port: Intent analysis
/// </summary>
public interface IIntentAnalyzer
{
    Task<IntentAnalysisResult> AnalyzeAsync(
        string normalizedQuestion,
        List<string> availableTables,
        CancellationToken ct = default);
}

/// <summary>
/// Port: Schema provider (scan database)
/// </summary>
public interface ISchemaProvider
{
    Task<DatabaseSchema> GetSchemaAsync(
        CancellationToken ct = default);

    void ClearCache();
}

/// <summary>
/// Port: Schema retrieval (RAG)
/// </summary>
public interface ISchemaRetriever
{
    Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        CancellationToken ct = default);
}

/// <summary>
/// Port: SQL generation
/// </summary>
public interface ISqlGenerator
{
    Task<string> GenerateAsync(
        IntentAnalysisResult intent,
        RetrievedSchemaContext schema,
        CancellationToken ct = default);

    bool ValidateSafety(string sql);
    string EnsureLimit(string sql, int maxRows = 1000);
}

/// <summary>
/// Port: SQL execution
/// </summary>
public interface ISqlExecutor
{
    Task<SqlExecutionResult> ExecuteAsync(
        string sql,
        CancellationToken ct = default);

    Task<bool> ValidateConnectionAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Port: SQL self-correction
/// </summary>
public interface ISqlCorrector
{
    Task<CorrectionResult> CorrectAsync(
        string failedSql,
        string errorMessage,
        RetrievedSchemaContext schema,
        IntentAnalysisResult intent,
        int attemptNumber,
        CancellationToken ct = default);

    bool ShouldRetry(
        List<CorrectionAttempt> corrections,
        int maxAttempts);
}

/// <summary>
/// Port: Result formatting
/// </summary>
public interface IResultFormatter
{
    string FormatAnswer(
        IntentAnalysisResult intent,
        SqlExecutionResult result,
        ConversationContext? context = null);
}

/// <summary>
/// Port: Conversation memory
/// </summary>
public interface IConversationStore
{
    ConversationContext GetOrCreate(string? conversationId = null);
    void AddTurn(ConversationContext context, ConversationTurn turn);
    string EnrichQuestion(ConversationContext context, string question);
    void Clear(string conversationId);
    int GetActiveCount();
}
