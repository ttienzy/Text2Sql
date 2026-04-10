using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Static analyzer for SQL queries using ScriptDom AST parsing
/// Detects anti-patterns and extracts metadata WITHOUT LLM (fast, deterministic)
/// </summary>
public class StaticAnalyzer
{
    private readonly TSql160Parser _parser;

    public StaticAnalyzer()
    {
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
    }

    /// <summary>
    /// Analyzes SQL query and returns metadata with detected anti-patterns
    /// </summary>
    public Task<QueryMetadata> AnalyzeAsync(string sql, CancellationToken cancellationToken = default)
    {
        var metadata = new QueryMetadata();

        if (string.IsNullOrWhiteSpace(sql))
            return Task.FromResult(metadata);

        try
        {
            // Parse SQL to AST
            var tree = _parser.Parse(new StringReader(sql), out var errors);

            // If parsing fails, add error to metadata
            if (errors.Any())
            {
                metadata.DetectedIssues.Add(new AntiPattern
                {
                    Code = "PARSE_ERROR",
                    Severity = Severity.Critical,
                    Title = "SQL parsing failed",
                    Description = string.Join("; ", errors.Select(e => e.Message)),
                    Impact = "Cannot analyze query"
                });
                return Task.FromResult(metadata);
            }

            // Create visitor and traverse AST
            var visitor = new QueryMetadataVisitor();
            tree.Accept(visitor);

            // Populate metadata from visitor
            metadata.Tables = visitor.Tables;
            metadata.Columns = visitor.Columns;
            metadata.JoinCount = visitor.JoinCount;
            metadata.SubqueryCount = visitor.SubqueryCount;
            metadata.WindowFunctionCount = visitor.WindowFunctionCount;
            metadata.CteCount = visitor.CteCount;
            metadata.DetectedIssues = visitor.DetectedIssues;
            metadata.ComplexityScore = visitor.CalculateComplexityScore();

            // Phase 2: Populate critical columns
            metadata.WhereColumns = visitor.GetWhereClauseColumns();
            metadata.JoinColumns = visitor.GetJoinColumns();
            metadata.OrderByColumns = visitor.GetOrderByColumns();
            metadata.GroupByColumns = visitor.GetGroupByColumns();

            return Task.FromResult(metadata);
        }
        catch (System.Exception ex)
        {
            metadata.DetectedIssues.Add(new AntiPattern
            {
                Code = "ANALYSIS_ERROR",
                Severity = Severity.Critical,
                Title = "Analysis failed",
                Description = ex.Message,
                Impact = "Cannot analyze query"
            });
            return Task.FromResult(metadata);
        }
    }
}
