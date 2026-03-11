using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Intelligent query router that directs queries to appropriate handlers
/// based on QueryValidator classification
/// </summary>
public class QueryRouter : IQueryRouter
{
    private readonly ILogger<QueryRouter> _logger;

    public QueryRouter(ILogger<QueryRouter> logger)
    {
        _logger = logger;
    }

    public Task<QueryRoute> RouteAsync(
        string question,
        QueryValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[QueryRouter] Routing query type: {Type}", validation.QueryType);

        var route = validation.QueryType switch
        {
            QueryType.DatabaseQuery => RouteDatabaseQuery(question, validation),
            QueryType.SchemaQuery => RouteSchemaQuery(question, validation),
            QueryType.Conversation => RouteConversation(question, validation),
            QueryType.OutOfScope => RouteOutOfScope(question, validation),
            QueryType.Ambiguous => RouteAmbiguous(question, validation),
            _ => RouteDefault(question, validation)
        };

        _logger.LogInformation(
            "[QueryRouter] Route decision: {Type} - {Reasoning}",
            route.Type,
            route.Reasoning);

        return Task.FromResult(route);
    }

    private QueryRoute RouteDatabaseQuery(string question, QueryValidationResult validation)
    {
        // Database queries go through full agent pipeline
        return new QueryRoute
        {
            Type = QueryRouteType.UseAgent,
            Parameters = new Dictionary<string, object>
            {
                ["question"] = question,
                ["validation"] = validation
            },
            Reasoning = "Database query - using full agent pipeline for SQL generation and execution"
        };
    }

    private QueryRoute RouteSchemaQuery(string question, QueryValidationResult validation)
    {
        // Schema queries can use direct tool
        if (question.Contains("tables", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("bảng", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryRoute
            {
                Type = QueryRouteType.UseTool,
                ToolName = "explore_schema",
                Parameters = new Dictionary<string, object>
                {
                    ["question"] = question,
                    ["query"] = question,
                    ["mode"] = "list_tables"
                },
                Reasoning = "Schema query - using explore_schema tool to list tables"
            };
        }

        // For other schema queries, use agent
        return new QueryRoute
        {
            Type = QueryRouteType.UseAgent,
            Parameters = new Dictionary<string, object>
            {
                ["question"] = question,
                ["validation"] = validation
            },
            Reasoning = "Schema query - using agent for detailed schema exploration"
        };
    }

    private QueryRoute RouteConversation(string question, QueryValidationResult validation)
    {
        // Conversational queries get direct response
        var response = validation.SuggestedResponse ??
            "Hello! I'm a database assistant. I can help you query your data. What would you like to know?";

        return new QueryRoute
        {
            Type = QueryRouteType.DirectResponse,
            DirectResponse = response,
            Reasoning = "Conversational query - providing friendly response"
        };
    }

    private QueryRoute RouteOutOfScope(string question, QueryValidationResult validation)
    {
        // Out of scope queries get polite rejection
        var response = validation.SuggestedResponse ??
            "I'm a database assistant specialized in querying data. " +
            "I can help you retrieve information from your database. " +
            "Please ask a database-related question.";

        return new QueryRoute
        {
            Type = QueryRouteType.DirectResponse,
            DirectResponse = response,
            Reasoning = "Out of scope query - politely declining and suggesting database queries"
        };
    }

    private QueryRoute RouteAmbiguous(string question, QueryValidationResult validation)
    {
        // Ambiguous queries need clarification
        var clarification = validation.ClarificationQuestion ??
            "Your question is a bit unclear. Could you please provide more details? " +
            "For example, which table or what specific information are you looking for?";

        return new QueryRoute
        {
            Type = QueryRouteType.NeedsClarification,
            DirectResponse = clarification,
            Reasoning = "Ambiguous query - asking for clarification"
        };
    }

    private QueryRoute RouteDefault(string question, QueryValidationResult validation)
    {
        // Default: use full agent pipeline
        _logger.LogWarning("[QueryRouter] Unknown query type, using default routing");

        return new QueryRoute
        {
            Type = QueryRouteType.UseAgent,
            Parameters = new Dictionary<string, object>
            {
                ["question"] = question,
                ["validation"] = validation
            },
            Reasoning = "Unknown query type - using full agent pipeline as fallback"
        };
    }
}
