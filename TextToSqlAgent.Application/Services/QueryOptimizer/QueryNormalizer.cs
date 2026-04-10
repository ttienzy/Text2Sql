using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Normalizes SQL queries using ScriptDom AST for consistent formatting and caching
/// </summary>
public class QueryNormalizer
{
    private readonly TSql160Parser _parser;
    private readonly Sql160ScriptGenerator _generator;

    public QueryNormalizer()
    {
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
        _generator = new Sql160ScriptGenerator();
    }

    /// <summary>
    /// Normalizes SQL query to consistent format for cache key generation
    /// </summary>
    public string NormalizeQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        try
        {
            var tree = _parser.Parse(new StringReader(sql), out var errors);

            // If parsing fails, return original SQL
            if (errors.Any())
                return sql;

            // Generate normalized SQL from AST
            _generator.GenerateScript(tree, out var normalized);

            return normalized;
        }
        catch
        {
            // Fallback to original on any error
            return sql;
        }
    }

    /// <summary>
    /// Generates MD5 hash for cache key from normalized query
    /// </summary>
    public string GenerateCacheKey(string sql)
    {
        var normalized = NormalizeQuery(sql);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return $"optimizer:{BitConverter.ToString(hash).Replace("-", "").ToLower()}";
    }

    /// <summary>
    /// Generates cache key scoped to a specific context (for example, database connection fingerprint).
    /// </summary>
    public string GenerateCacheKey(string sql, string scope)
    {
        var normalized = NormalizeQuery(sql);
        var payload = string.IsNullOrWhiteSpace(scope)
            ? normalized
            : $"{scope}:{normalized}";

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"optimizer:{BitConverter.ToString(hash).Replace("-", "").ToLower()}";
    }

    /// <summary>
    /// Backward-compatible alias used by existing tests.
    /// </summary>
    public string GetNormalizedHash(string sql) => GenerateCacheKey(sql);
}
