namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Infrastructure.RAG;

public class SchemaRetrieverAdapter : ISchemaRetriever
{
    private readonly SchemaRetriever _retriever;

    public SchemaRetrieverAdapter(SchemaRetriever retriever)
    {
        _retriever = retriever;
    }

    public Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        CancellationToken ct = default)
    {
        return _retriever.RetrieveAsync(question, fullSchema, ct);
    }
}
