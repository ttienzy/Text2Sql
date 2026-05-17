using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;
using System.Collections;
using TextToSqlAgent.Core.Enums;

namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// Central registry for all prompt templates
/// Supports versioning, composition, and dynamic loading from YAML files
/// 
/// v2.0 Features:
/// - Template composition: merge multiple templates together
/// - Module-based loading from Prompts/v2.0/
/// - Pattern injection for SQL generation
/// - Intent routing support
/// </summary>
public class PromptRegistry
{
    private readonly Dictionary<string, PromptTemplate> _templates = new();
    private readonly Dictionary<string, PromptModule> _modules = new();
    private readonly ILogger<PromptRegistry> _logger;
    private readonly string _promptsDirectory;
    private readonly PromptComposer _composer;

    public PromptRegistry(ILogger<PromptRegistry> logger, string? promptsDirectory = null)
    {
        _logger = logger;
        _promptsDirectory = promptsDirectory ?? GetDefaultPromptsDirectory();
        _composer = new PromptComposer(_logger);

        _logger.LogInformation("PromptRegistry using prompts directory: {PromptsDirectory}", _promptsDirectory);

        LoadModules();
        LoadTemplates();
    }

    /// <summary>
    /// Get the default prompts directory (Prompts/v2.0/)
    /// </summary>
    private static string GetDefaultPromptsDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TEXTTOSQL_PROMPTS_DIR");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expandedConfiguredPath = Environment.ExpandEnvironmentVariables(configuredPath);
            var normalizedConfiguredPath = Path.GetFullPath(expandedConfiguredPath);
            if (Directory.Exists(normalizedConfiguredPath))
            {
                return normalizedConfiguredPath;
            }
        }

        var candidateRoots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            Path.GetDirectoryName(typeof(PromptRegistry).Assembly.Location)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in candidateRoots)
        {
            var resolved = FindPromptsDirectory(root);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Prompts", "v2.0"));
    }

    private static string? FindPromptsDirectory(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Prompts", "v2.0");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    #region Template Management

    /// <summary>
    /// Get template by name and version
    /// </summary>
    public PromptTemplate GetTemplate(string name, string version = "latest")
    {
        name = NormalizeTemplateName(name);

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
    /// Get full prompt with system + few-shot + user template rendered
    /// Convenience method for common use case
    /// </summary>
    /// <param name="templateName">Name of the template</param>
    /// <param name="variables">Variables to render in the template</param>
    /// <param name="version">Template version (default: latest)</param>
    /// <param name="includeFewShot">Include few-shot examples (default: true)</param>
    /// <returns>Rendered prompt string</returns>
    public string GetFullPrompt(string templateName, Dictionary<string, object> variables, string version = "latest", bool includeFewShot = true)
    {
        var template = GetTemplate(templateName, version);
        return template.GetFullPrompt(variables, includeFewShot);
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
    /// Register a template programmatically
    /// </summary>
    public void RegisterTemplate(PromptTemplate template)
    {
        template.Name = NormalizeTemplateName(template.Name);
        var key = $"{template.Name}:{template.Version}";
        _templates[key] = template;
        _logger.LogInformation("Registered prompt template: {Name} v{Version}", template.Name, template.Version);
    }

    /// <summary>
    /// Unregister a template
    /// </summary>
    public bool UnregisterTemplate(string name, string version = "latest")
    {
        name = NormalizeTemplateName(name);

        var key = version == "latest"
            ? GetLatestVersionKey(name)
            : $"{name}:{version}";

        return _templates.Remove(key);
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

    #endregion

    #region Template Composition

    /// <summary>
    /// Compose a template by combining base template with modules
    /// </summary>
    /// <param name="baseTemplateName">Name of the base template (e.g., "sql-generation/base")</param>
    /// <param name="modulesToInclude">List of module names to inject (e.g., "window-functions", "vietnamese-mapping")</param>
    /// <param name="variables">Variables for template rendering</param>
    /// <param name="version">Template version</param>
    /// <returns>Composed prompt string</returns>
    public string Compose(string baseTemplateName, List<string> modulesToInclude, Dictionary<string, object> variables, string version = "latest")
    {
        var baseTemplate = GetTemplate(baseTemplateName, version);
        
        var composed = _composer.Compose(baseTemplate, modulesToInclude, _modules, variables);
        
        _logger.LogDebug("Composed template {Base} with {ModuleCount} modules", baseTemplateName, modulesToInclude.Count);
        
        return composed;
    }

    /// <summary>
    /// Get composed prompt with all components (system + patterns + examples + user)
    /// </summary>
    public string GetComposedPrompt(string baseTemplateName, List<string> modulesToInclude, Dictionary<string, object> variables, string version = "latest", bool includeExamples = true)
    {
        var baseTemplate = GetTemplate(baseTemplateName, version);
        
        return _composer.GetFullComposedPrompt(baseTemplate, modulesToInclude, _modules, variables, includeExamples);
    }

    /// <summary>
    /// Get composed system and user prompts separately
    /// </summary>
    public (string SystemPrompt, string UserPrompt) GetSystemAndUserPrompts(string baseTemplateName, List<string> modulesToInclude, Dictionary<string, object> variables, string version = "latest", bool includeExamples = true)
    {
        var baseTemplate = GetTemplate(baseTemplateName, version);
        
        return _composer.GetSystemAndUserPrompts(baseTemplate, modulesToInclude, _modules, variables, includeExamples);
    }

    /// <summary>
    /// Build a SQL generation prompt with pattern injection
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildSqlGenerationPrompt(
        string question,
        string schemaContext,
        List<string> patternsToInclude,
        Dictionary<string, object>? extraVariables = null)
    {
        var variables = new Dictionary<string, object>
        {
            { "question", question },
            { "schema_context", schemaContext }
        };

        if (extraVariables != null)
        {
            foreach (var kvp in extraVariables)
            {
                variables[kvp.Key] = kvp.Value;
            }
        }

        // Add system role if not already included
        if (!patternsToInclude.Contains("system-role"))
        {
            patternsToInclude.Insert(0, "system-role");
        }

        return GetSystemAndUserPrompts("sql-generation/base", patternsToInclude, variables);
    }

    /// <summary>
    /// Build a SQL generation prompt WITH SUGGESTIONS
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildSqlGenerationPromptWithSuggestions(
        string question,
        string schemaContext,
        List<string> patternsToInclude,
        Dictionary<string, object>? extraVariables = null)
    {
        var variables = new Dictionary<string, object>
        {
            { "question", question },
            { "schema_context", schemaContext }
        };

        if (extraVariables != null)
        {
            foreach (var kvp in extraVariables)
            {
                variables[kvp.Key] = kvp.Value;
            }
        }

        if (!patternsToInclude.Contains("system-role"))
        {
            patternsToInclude.Insert(0, "system-role");
        }

        return GetSystemAndUserPrompts("sql-generation/with-suggestions", patternsToInclude, variables);
    }
    /// <summary>
    /// Build a SQL generation prompt selecting the system prompt based on database provider.
    /// PostgreSQL → PostgreSqlPrompts.SystemPrompt, MySQL → MySqlPrompts.SystemPrompt,
    /// SQL Server → standard YAML template system prompt.
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildSqlGenerationPromptForProvider(
        string question,
        string schemaContext,
        DatabaseProvider provider,
        List<string>? patternsToInclude = null,
        Dictionary<string, object>? extraVariables = null)
    {
        var patterns = patternsToInclude ?? new List<string>();

        // Get the base system+user prompts from YAML templates (SQL Server default)
        var (baseSystem, userPrompt) = BuildSqlGenerationPrompt(
            question, schemaContext, patterns, extraVariables);

        // Override system prompt for non-SQL Server providers
        var systemPrompt = provider switch
        {
            DatabaseProvider.PostgreSql => PostgreSqlPrompts.SystemPrompt,
            DatabaseProvider.MySql      => MySqlPrompts.SystemPrompt,
            _                           => baseSystem  // SqlServer uses YAML template as-is
        };

        return (systemPrompt, userPrompt);
    }

    #endregion

    #region Module Management

    /// <summary>
    /// Get a module by name
    /// </summary>
    public PromptModule? GetModule(string moduleName)
    {
        return _modules.TryGetValue(moduleName, out var module) ? module : null;
    }

    /// <summary>
    /// List all available modules
    /// </summary>
    public List<string> ListModules()
    {
        return _modules.Keys.OrderBy(k => k).ToList();
    }

    /// <summary>
    /// Register a module programmatically
    /// </summary>
    public void RegisterModule(PromptModule module)
    {
        _modules[module.Name] = module;
        _logger.LogInformation("Registered prompt module: {Name}", module.Name);
    }

    #endregion

    #region Loading Logic

    /// <summary>
    /// Load module definitions from config.yaml
    /// </summary>
    private void LoadModules()
    {
        var configPath = Path.Combine(_promptsDirectory, "config", "config.yaml");
        
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Config file not found: {Path}", configPath);
            LoadDefaultModules();
            return;
        }

        try
        {
            var yaml = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var config = deserializer.Deserialize<PromptConfig>(yaml);
            
            // Create modules from config
            if (config.Modules != null)
            {
                foreach (var moduleConfig in config.Modules)
                {
                    var module = new PromptModule
                    {
                        Name = moduleConfig.Key,
                        Path = moduleConfig.Value.Path,
                        Description = moduleConfig.Value.Description,
                        Version = config.Version
                    };
                    
                    _modules[module.Name] = module;
                }
            }

            _logger.LogInformation("Loaded {Count} prompt modules from config", _modules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load modules from config");
            LoadDefaultModules();
        }
    }

    /// <summary>
    /// Load templates from YAML files in v2.0 structure
    /// </summary>
    private void LoadTemplates()
    {
        if (!Directory.Exists(_promptsDirectory))
        {
            _logger.LogWarning("Prompts directory not found: {Directory}", _promptsDirectory);
            LoadDefaultTemplates();
            return;
        }

        // Load from subdirectories based on structure
        var templateFiles = new[]
        {
            Path.Combine(_promptsDirectory, "core", "*.yaml"),
            Path.Combine(_promptsDirectory, "sql-generation", "*.yaml"),
            Path.Combine(_promptsDirectory, "sql-generation", "few-shot-examples", "*.yaml"),
            Path.Combine(_promptsDirectory, "db-explorer", "*.yaml"),
            Path.Combine(_promptsDirectory, "optimizer", "*.yaml")
        };

        foreach (var pattern in templateFiles)
        {
            var directory = Path.GetDirectoryName(pattern)!;
            if (!Directory.Exists(directory))
            {
                _logger.LogDebug("Skipping missing prompt template directory: {Directory}", directory);
                continue;
            }

            var files = Directory.GetFiles(directory, Path.GetFileName(pattern), SearchOption.TopDirectoryOnly);
            
            foreach (var file in files)
            {
                try
                {
                    LoadTemplateFromFile(file);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load template from {File}", file);
                }
            }
        }

        if (_templates.Count == 0)
        {
            _logger.LogWarning("No templates loaded from directory, using defaults");
            LoadDefaultTemplates();
        }
        else
        {
            _logger.LogInformation("Loaded {Count} prompt templates", _templates.Count);
        }
    }

    private void LoadTemplateFromFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var document = deserializer.Deserialize<Dictionary<object, object>>(yaml);
        var template = CreateTemplateFromDocument(document, filePath);
        if (template != null)
        {
            RegisterTemplate(template);

            if (template.Metadata.TryGetValue("canonical_name", out var aliasValue) &&
                aliasValue is string alias &&
                !string.Equals(alias, template.Name, StringComparison.OrdinalIgnoreCase))
            {
                RegisterTemplate(new PromptTemplate
                {
                    Name = alias,
                    Version = template.Version,
                    Model = template.Model,
                    Temperature = template.Temperature,
                    MaxTokens = template.MaxTokens,
                    SystemPrompt = template.SystemPrompt,
                    UserPrompt = template.UserPrompt,
                    FewShotExamples = template.FewShotExamples,
                    Metadata = new Dictionary<string, object>(template.Metadata)
                });
            }
        }
    }

    private PromptTemplate? CreateTemplateFromDocument(Dictionary<object, object>? document, string filePath)
    {
        if (document == null || document.Count == 0)
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(_promptsDirectory, filePath).Replace('\\', '/');
        var extension = Path.GetExtension(relativePath);
        var pathName = string.IsNullOrEmpty(extension)
            ? relativePath
            : relativePath[..^extension.Length];
        pathName = NormalizeTemplateName(pathName);

        var metadata = GetMap(document, "metadata");
        var config = GetMap(document, "config");

        var canonicalName = GetString(metadata, "name");
        var version = GetString(metadata, "version")
            ?? GetString(document, "version")
            ?? "1.0.0";

        var systemPrompt = ResolveSystemPrompt(document, filePath);
        var userPrompt = GetString(document, "user_template")
            ?? GetString(document, "prompt_template")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(systemPrompt) && string.IsNullOrWhiteSpace(userPrompt))
        {
            return null;
        }

        var template = new PromptTemplate
        {
            Name = pathName,
            Version = version,
            Model = GetString(config, "model") ?? "gpt-4o",
            Temperature = GetDouble(config, "temperature") ?? 0.1,
            MaxTokens = GetInt(config, "max_tokens") ?? 2048,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Metadata = new Dictionary<string, object>
            {
                ["source_path"] = relativePath
            }
        };

        if (!string.IsNullOrWhiteSpace(canonicalName))
        {
            template.Metadata["canonical_name"] = NormalizeTemplateName(canonicalName!);
        }

        return template;
    }

    private string ResolveSystemPrompt(Dictionary<object, object> document, string filePath)
    {
        var directSystem = GetString(document, "system")
            ?? GetString(document, "system_template");

        if (!string.IsNullOrWhiteSpace(directSystem))
        {
            return directSystem!;
        }

        var systemMap = GetMap(document, "system");
        if (systemMap == null)
        {
            return string.Empty;
        }

        var promptParts = new List<string>();
        var loadFrom = GetString(systemMap, "load_from");
        if (!string.IsNullOrWhiteSpace(loadFrom))
        {
            var loadedContent = LoadPromptAsset(loadFrom!, filePath);
            if (!string.IsNullOrWhiteSpace(loadedContent))
            {
                promptParts.Add(loadedContent.Trim());
            }
        }

        var additionalContext = GetString(systemMap, "additional_context");
        if (!string.IsNullOrWhiteSpace(additionalContext))
        {
            promptParts.Add(additionalContext.Trim());
        }

        return string.Join("\n\n", promptParts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private string LoadPromptAsset(string relativePath, string sourceFilePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Trim();

        var candidates = new List<string>
        {
            Path.Combine(_promptsDirectory, normalized.TrimStart('.', Path.DirectorySeparatorChar)),
            Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? _promptsDirectory, normalized.TrimStart('.', Path.DirectorySeparatorChar))
        };

        foreach (var candidate in candidates.Distinct())
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        _logger.LogWarning("Prompt asset not found: {Path}", relativePath);
        return string.Empty;
    }

    private static Dictionary<object, object>? GetMap(Dictionary<object, object> source, string key)
    {
        if (!source.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as Dictionary<object, object>;
    }

    private static string? GetString(Dictionary<object, object>? source, string key)
    {
        if (source == null || !source.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        if (value is string stringValue)
        {
            return stringValue;
        }

        return value is Dictionary<object, object> or IList
            ? null
            : value.ToString();
    }

    private static double? GetDouble(Dictionary<object, object>? source, string key)
    {
        var raw = GetString(source, key);
        return double.TryParse(raw, out var value) ? value : null;
    }

    private static int? GetInt(Dictionary<object, object>? source, string key)
    {
        var raw = GetString(source, key);
        return int.TryParse(raw, out var value) ? value : null;
    }

    /// <summary>
    /// Load default modules
    /// </summary>
    private void LoadDefaultModules()
    {
        _modules["core"] = new PromptModule
        {
            Name = "core",
            Path = "./core",
            Description = "Core system prompts and patterns",
            Version = "2.0.0"
        };

        _modules["db-explorer"] = new PromptModule
        {
            Name = "db-explorer",
            Path = "./db-explorer",
            Description = "Database exploration and schema analysis",
            Version = "2.0.0"
        };

        _modules["sql-generation"] = new PromptModule
        {
            Name = "sql-generation",
            Path = "./sql-generation",
            Description = "SQL generation with patterns and examples",
            Version = "2.0.0"
        };

        _modules["optimizer"] = new PromptModule
        {
            Name = "optimizer",
            Path = "./optimizer",
            Description = "Query optimization and performance",
            Version = "2.0.0"
        };

        _logger.LogInformation("Loaded {Count} default modules", _modules.Count);
    }

    /// <summary>
    /// Load default hardcoded templates as fallback
    /// </summary>
    private void LoadDefaultTemplates()
    {
        // SQL Generation Template v2.0
        RegisterTemplate(new PromptTemplate
        {
            Name = "sql-generation/base",
            Version = "2.0.0",
            Model = "gpt-4o",
            Temperature = 0.1,
            MaxTokens = 2048,
            SystemPrompt = @"You are an expert SQL generator for SQL Server (T-SQL).

# DATABASE TYPE
{database_type}

# SECURITY RULES
- ONLY generate SELECT queries
- Use square brackets for identifiers: [TableName].[ColumnName]
- All string values must use N prefix: N'value'
- Add TOP clause when LIMIT is specified

# SQL PATTERNS
{sql_patterns}

# LANGUAGE GUIDE
{language_guide}",
            UserPrompt = @"# QUESTION
{question}

# SCHEMA CONTEXT
{schema_context}

# OUTPUT FORMAT
Generate a valid T-SQL query. Return ONLY the SQL query.

If the question is ambiguous, return the most likely interpretation.

If you cannot generate a SQL query, return: -- CANNOT_GENERATE: [reason]",
            Metadata = new Dictionary<string, object>
            {
                { "category", "sql-generation" },
                { "supports_composition", true },
                { "requires_schema", true }
            }
        });

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
}",
            Metadata = new Dictionary<string, object>
            {
                { "category", "agent" },
                { "agent_type", "react" }
            }
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
{
  ""tables"": [""table_name1"", ""table_name2""],
  ""columns"": [""column_name1"", ""column_name2""],
  ""values"": [""literal_value1"", ""literal_value2""],
  ""operators"": ["">"", ""<"", ""=""],
  ""aggregations"": [""SUM"", ""COUNT"", ""AVG""]
}

Only return the JSON, no explanation.",
            Metadata = new Dictionary<string, object>
            {
                { "category", "schema-linking" }
            }
        });

        _logger.LogInformation("Loaded {Count} default prompt templates", _templates.Count);
    }

    private string GetLatestVersionKey(string name)
    {
        name = NormalizeTemplateName(name);

        var match = _templates.Keys
            .Select(k =>
            {
                var parts = k.Split(':');
                return new
                {
                    Key = k,
                    Name = NormalizeTemplateName(parts[0]),
                    Version = parts[1]
                };
            })
            .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => new Version(x.Version))
            .FirstOrDefault();

        if (match == null)
        {
            throw new KeyNotFoundException($"No versions found for template '{name}'");
        }

        return match.Key;
    }

    private static string NormalizeTemplateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name.Replace('\\', '/').Trim();
        normalized = normalized.TrimEnd('/', '.');
        return normalized;
    }

    #endregion
}

/// <summary>
/// Configuration loaded from config.yaml
/// </summary>
public class PromptConfig
{
    public string Version { get; set; } = "2.0.0";
    public string Description { get; set; } = "";
    public PromptConfigGlobal? Global { get; set; }
    public Dictionary<string, PromptModuleConfig>? Modules { get; set; }
    public PromptConfigSecurity? Security { get; set; }
    public PromptConfigAmbiguity? Ambiguity { get; set; }
    public PromptConfigChaining? Chaining { get; set; }
}

public class PromptConfigGlobal
{
    public string DefaultModel { get; set; } = "gpt-4o";
    public double DefaultTemperature { get; set; } = 0.1;
    public int MaxTokens { get; set; } = 4096;
    public string OutputFormat { get; set; } = "json";
}

public class PromptModuleConfig
{
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Templates { get; set; } = new();
    public string? ExamplesPath { get; set; }
}

public class PromptConfigSecurity
{
    public List<string> AllowedOperations { get; set; } = new();
    public List<string> ForbiddenKeywords { get; set; } = new();
}

public class PromptConfigAmbiguity
{
    public bool Enabled { get; set; } = true;
    public double Threshold { get; set; } = 0.7;
}

public class PromptConfigChaining
{
    public bool Enabled { get; set; } = true;
    public int MaxSteps { get; set; } = 5;
}

/// <summary>
/// Represents a prompt module for composition
/// </summary>
public class PromptModule
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, string> Patterns { get; set; } = new();
}

/// <summary>
/// Composes multiple prompt templates and patterns together
/// </summary>
public class PromptComposer
{
    private readonly ILogger _logger;

    public PromptComposer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compose a base template with additional pattern modules
    /// </summary>
    public string Compose(
        PromptTemplate baseTemplate,
        List<string> modulesToInclude,
        Dictionary<string, PromptModule> availableModules,
        Dictionary<string, object> variables)
    {
        var parts = new List<string>();

        // 1. System Prompt (with pattern injection)
        var systemPrompt = baseTemplate.RenderSystem(variables);
        
        // Inject patterns into system prompt
        foreach (var moduleName in modulesToInclude)
        {
            if (availableModules.TryGetValue(moduleName, out var module))
            {
                // Load patterns from module files
                var patterns = LoadPatternsFromModule(module);
                foreach (var pattern in patterns)
                {
                    systemPrompt += "\n\n" + pattern.Value;
                }
            }
        }

        parts.Add(systemPrompt);

        // 2. User Prompt
        parts.Add(baseTemplate.Render(variables));

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Get full composed prompt with few-shot examples
    /// </summary>
    public string GetFullComposedPrompt(
        PromptTemplate baseTemplate,
        List<string> modulesToInclude,
        Dictionary<string, PromptModule> availableModules,
        Dictionary<string, object> variables,
        bool includeExamples = true)
    {
        var parts = new List<string>();

        // System prompt with patterns
        parts.Add(Compose(baseTemplate, modulesToInclude, availableModules, variables));

        // Few-shot examples
        if (includeExamples && baseTemplate.FewShotExamples.Count > 0)
        {
            parts.Add("\n# EXAMPLES:");
            foreach (var example in baseTemplate.FewShotExamples)
            {
                parts.Add($"\nQ: {example.Input}");
                parts.Add($"A: {example.Output}");
            }
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Get system and user prompts separately
    /// </summary>
    public (string SystemPrompt, string UserPrompt) GetSystemAndUserPrompts(
        PromptTemplate baseTemplate,
        List<string> modulesToInclude,
        Dictionary<string, PromptModule> availableModules,
        Dictionary<string, object> variables,
        bool includeExamples = true)
    {
        // 1. System Prompt (with pattern injection)
        var systemPrompt = baseTemplate.RenderSystem(variables);
        
        // Inject patterns into system prompt
        foreach (var moduleName in modulesToInclude)
        {
            if (availableModules.TryGetValue(moduleName, out var module))
            {
                // Load patterns from module files
                var patterns = LoadPatternsFromModule(module);
                foreach (var pattern in patterns)
                {
                    systemPrompt += "\n\n" + pattern.Value;
                }
            }
        }

        // 2. User Prompt
        var userPromptParts = new List<string>
        {
            baseTemplate.Render(variables)
        };

        // Few-shot examples
        if (includeExamples && baseTemplate.FewShotExamples.Count > 0)
        {
            userPromptParts.Add("\n# EXAMPLES:");
            foreach (var example in baseTemplate.FewShotExamples)
            {
                userPromptParts.Add($"\nQ: {example.Input}");
                userPromptParts.Add($"A: {example.Output}");
            }
        }

        return (systemPrompt, string.Join("\n", userPromptParts));
    }

    private Dictionary<string, string> LoadPatternsFromModule(PromptModule module)
    {
        var patterns = new Dictionary<string, string>();
        var moduleDir = module.Path;

        // If path is relative, resolve from current directory
        if (!Path.IsPathRooted(moduleDir))
        {
            moduleDir = Path.Combine(Directory.GetCurrentDirectory(), "Prompts", "v2.0", moduleDir);
        }

        if (!Directory.Exists(moduleDir))
        {
            _logger.LogWarning("Module directory not found: {Path}", moduleDir);
            return patterns;
        }

        // Load pattern files (.txt files)
        var patternFiles = Directory.GetFiles(moduleDir, "*.txt", SearchOption.AllDirectories);
        foreach (var file in patternFiles)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            patterns[name] = File.ReadAllText(file);
        }

        return patterns;
    }
}
