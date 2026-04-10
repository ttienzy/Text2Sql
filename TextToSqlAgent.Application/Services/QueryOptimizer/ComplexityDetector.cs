using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Detects query complexity and recommends appropriate LLM model
/// </summary>
public class ComplexityDetector
{
    /// <summary>
    /// Determines which model to use based on query complexity
    /// </summary>
    public ModelType DetermineModel(QueryMetadata metadata)
    {
        var complexity = metadata.ComplexityScore;

        // Simple queries: GPT-4o-mini (fast, cheap)
        if (complexity <= 5)
            return ModelType.GPT4oMini;

        // Medium queries: GPT-4o (balanced)
        if (complexity <= 15)
            return ModelType.GPT4o;

        // Complex queries: o3-mini (reasoning model, best for complex optimization)
        return ModelType.O3Mini;
    }

    /// <summary>
    /// Gets model display name
    /// </summary>
    public string GetModelName(ModelType modelType)
    {
        return modelType switch
        {
            ModelType.GPT4oMini => "GPT-4o-mini",
            ModelType.GPT4o => "GPT-4o",
            ModelType.O3Mini => "o3-mini",
            _ => "GPT-4o-mini"
        };
    }
}

/// <summary>
/// Available LLM models for optimization
/// </summary>
public enum ModelType
{
    GPT4oMini,
    GPT4o,
    O3Mini
}
