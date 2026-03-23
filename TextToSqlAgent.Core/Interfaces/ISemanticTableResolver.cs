using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Resolves semantic entity mentions (e.g., "user", "product", "order") 
/// to actual database table names using LLM-based intelligent mapping.
/// 
/// This enables natural language queries like:
/// - "Insert a new user" → Customers table
/// - "Show me products" → Products table
/// - "List all orders" → Orders table
/// </summary>
public interface ISemanticTableResolver
{
    /// <summary>
    /// Resolve entity mentions in a question to actual database table names
    /// </summary>
    /// <param name="question">User's natural language question</param>
    /// <param name="schema">Database schema with available tables</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Resolution result with matched table and confidence</returns>
    Task<TableResolutionResult> ResolveAsync(
        string question,
        DatabaseSchema schema,
        CancellationToken ct = default);

    /// <summary>
    /// Resolve a specific entity mention to a table name
    /// </summary>
    /// <param name="entityMention">Entity mention (e.g., "user", "customer", "product")</param>
    /// <param name="schema">Database schema with available tables</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Resolution result with matched table and confidence</returns>
    Task<TableResolutionResult> ResolveEntityAsync(
        string entityMention,
        DatabaseSchema schema,
        CancellationToken ct = default);
}
