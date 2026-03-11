using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// Central registry for all prompt templates
/// Supports versioning and dynamic loading from YAML files
/// </summary>
public class PromptRegistry
{
    private readonly Dictionary<string, PromptTemplate> _templates = new();
    private readonly ILogger<PromptRegistry> _logger;
    private readonly string _promptsDirectory;

    public PromptRegistry(ILogger<PromptRegistry> logger, string? promptsDirectory = null)
    {
        _logger = logger;
        _promptsDirectory = promptsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", "Templates");

        LoadTemplates();
    }

    /// <summary>
    /// Get template by name and version
    /// </summary>
    public PromptTemplate GetTemplate(string name, string version = "latest")
    {
        var key = version == "latest"
            ? GetLatestVersionKey(name)
            : $"{name}:{version}";

        if (_templates.TryGetValue(key, out var template))
        {
            return template;
        }

        throw new KeyNotFoundException($"Prompt template '{name}' version '{version}' not found");
    }

    /// <summary>
    /// Register a template programmatically
    /// </summary>
    public void RegisterTemplate(PromptTemplate template)
    {
        var key = $"{template.Name}:{template.Version}";
        _templates[key] = template;
        _logger.LogInformation("Registered prompt template: {Name} v{Version}", template.Name, template.Version);
    }

    /// <summary>
    /// Render a template with variables
    /// </summary>
    public string Render(string templateName, Dictionary<string, object> variables, string version = "latest")
    {
        var template = GetTemplate(templateName, version);
        return template.Render(variables);
    }

    /// <summary>
    /// Get full prompt with system + few-shot + user
    /// </summary>
    public string GetFullPrompt(string templateName, Dictionary<string, object> variables, string version = "latest", bool includeFewShot = true)
    {
        var template = GetTemplate(templateName, version);
        return template.GetFullPrompt(variables, includeFewShot);
    }

    /// <summary>
    /// List all available templates
    /// </summary>
    public List<(string Name, string Version)> ListTemplates()
    {
        return _templates.Keys
            .Select(k =>
            {
                var parts = k.Split(':');
                return (Name: parts[0], Version: parts[1]);
            })
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Version)
            .ToList();
    }

    /// <summary>
    /// Load templates from YAML files
    /// </summary>
    private void LoadTemplates()
    {
        if (!Directory.Exists(_promptsDirectory))
        {
            _logger.LogWarning("Prompts directory not found: {Directory}", _promptsDirectory);
            LoadDefaultTemplates();
            return;
        }

        var yamlFiles = Directory.GetFiles(_promptsDirectory, "*.yaml", SearchOption.AllDirectories);

        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var template = deserializer.Deserialize<PromptTemplate>(yaml);
                RegisterTemplate(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load prompt template from {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} prompt templates", _templates.Count);
    }

    /// <summary>
    /// Load default hardcoded templates as fallback
    /// </summary>
    private void LoadDefaultTemplates()
    {
        // Agent Reasoning Template
        RegisterTemplate(new PromptTemplate
        {
            Name = "agent_reasoning",
            Version = "1.0.0",
            Model = "gpt-4o",
            Temperature = 0.1,
            SystemPrompt = @"You are an intelligent Text-to-SQL agent using the ReAct (Reasoning + Acting) pattern.

Your task is to convert natural language questions into SQL queries by:
1. THINKING about what information you need
2. ACTING by using available tools
3. OBSERVING the results
4. REFLECTING on whether you have the answer

Available tools:
- explore_schema: Find relevant tables and columns
- generate_sql: Generate SQL query
- execute_sql: Run SQL query
- validate_sql: Check SQL syntax
- verify_result: Verify if result makes sense",
            UserPrompt = @"Question: {question}
Database: {database_name}

Current Step: {current_step}/{max_steps}

Available Tools:
{available_tools}

Previous Steps:
{previous_steps}

Think step by step and decide the next best move.

Return ONLY valid JSON:
{
  ""thought"": ""your reasoning about what to do next"",
  ""plan"": ""concise next-step plan""
}"
        });

        // Schema Linking Template
        RegisterTemplate(new PromptTemplate
        {
            Name = "schema_linking",
            Version = "1.0.0",
            Model = "gpt-4o",
            Temperature = 0.0,
            SystemPrompt = @"You are a database schema expert. Extract entities from natural language questions.",
            UserPrompt = @"Extract database entities from this question:

Question: {question}

Return JSON with:
{{
  ""tables"": [""table_name1"", ""table_name2""],
  ""columns"": [""column_name1"", ""column_name2""],
  ""values"": [""literal_value1"", ""literal_value2""],
  ""operators"": ["">"", ""<"", ""=""],
  ""aggregations"": [""SUM"", ""COUNT"", ""AVG""]
}}

Only return the JSON, no explanation."
        });

        // SQL Generation Template
        RegisterTemplate(new PromptTemplate
        {
            Name = "sql_generation",
            Version = "2.0.0",
            Model = "gpt-4o",
            Temperature = 0.1,
            MaxTokens = 2048,
            SystemPrompt = @"You are an expert SQL generator for {database_type}.

# CORE RULES
- Use provided schema EXACTLY as given
- Use proper JOINs (INNER/LEFT) and table aliases
- Handle NULLs with COALESCE
- Group by all non-aggregated SELECT columns

# FORBIDDEN
- NO SELECT *
- NO implicit JOINs
- NO data modification unless explicitly asked",
            UserPrompt = @"Question: {question}

Schema:
{schema_context}

Generate SQL query. Return ONLY the SQL, no explanation.",
            FewShotExamples = new List<FewShotExample>
            {
                new() { Input = "Count customers\nSchema: Customers(CustomerId, FullName)", Output = "SELECT COUNT(*) AS Total FROM Customers;" },
                new() { Input = "Top 10 by revenue\nSchema: Customers(CustomerId, FullName), Orders(OrderId, CustomerId, TotalAmount)", Output = "SELECT TOP 10 c.FullName, SUM(o.TotalAmount) AS Revenue\nFROM Customers c\nJOIN Orders o ON c.CustomerId = o.CustomerId\nGROUP BY c.CustomerId, c.FullName\nORDER BY Revenue DESC;" }
            }
        });

        _logger.LogInformation("Loaded {Count} default prompt templates", _templates.Count);
    }

    private string GetLatestVersionKey(string name)
    {
        var versions = _templates.Keys
            .Where(k => k.StartsWith($"{name}:"))
            .Select(k => k.Split(':')[1])
            .OrderByDescending(v => new Version(v))
            .FirstOrDefault();

        if (versions == null)
        {
            throw new KeyNotFoundException($"No versions found for template '{name}'");
        }

        return $"{name}:{versions}";
    }
}
