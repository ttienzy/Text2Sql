using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

public interface ISchemaSemanticProfileStore
{
    Task<SchemaSemanticProfile?> GetAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string connectionId,
        SchemaSemanticProfile profile,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
