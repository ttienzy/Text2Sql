namespace TextToSqlAgent.Application.Options;

/// <summary>
/// Configuration options for DB Explorer
/// </summary>
public class DbExplorerOptions
{
    public const string SectionName = "DbExplorer";

    public HealthCheckOptions HealthCheck { get; set; } = new();
    public NamingConventionOptions NamingConvention { get; set; } = new();
    public AIOptions AI { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public PerformanceOptions Performance { get; set; } = new();
    public ImplicitFkDetectionOptions ImplicitFkDetection { get; set; } = new();
    public SemanticSearchOptions SemanticSearch { get; set; } = new();
}

public class HealthCheckOptions
{
    public int MaxColumnsPerTable { get; set; } = 50;
    public double ImplicitFkConfidenceThreshold { get; set; } = 0.85;
    public long MinRowsForStatistics { get; set; } = 1000000;
    public string IgnoreTablesRegex { get; set; } = "^(dbo|sys|__EFMigrationsHistory|sysdiagrams)";
    public List<string> PasswordColumnPatterns { get; set; } = new() { "password", "pwd", "pass", "secret" };
    public List<string> AuditColumnNames { get; set; } = new() { "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy" };
}

public class NamingConventionOptions
{
    public string PreferredStyle { get; set; } = "PascalCase";
    public List<string> AllowedStyles { get; set; } = new() { "PascalCase", "snake_case", "camelCase" };
    public bool StrictMode { get; set; } = false;
    public CustomPatternsOptions CustomPatterns { get; set; } = new();
}

public class CustomPatternsOptions
{
    public string TablePrefix { get; set; } = "";
    public string ColumnPrefix { get; set; } = "";
    public string ForeignKeyPattern { get; set; } = "^{ParentTable}Id$";
}

public class AIOptions
{
    public bool LazyLoadingEnabled { get; set; } = true;
    public BatchSizeOptions BatchSize { get; set; } = new();
    public CacheTTLOptions CacheTTL { get; set; } = new();
    public PromptsOptions Prompts { get; set; } = new();
}

public class BatchSizeOptions
{
    public int Tables { get; set; } = 10;
    public int Columns { get; set; } = 20;
}

public class CacheTTLOptions
{
    public TimeSpan SchemaAnalysis { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ColumnInterpretation { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan SemanticTags { get; set; } = TimeSpan.FromDays(30);
}

public class PromptsOptions
{
    public string BasePath { get; set; } = "Prompts/DbExplorer";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;
}

public class SecurityOptions
{
    public bool AllowSampleDataQuery { get; set; } = false;
    public int MaxSampleRows { get; set; } = 5;
    public bool RequireExplicitConsent { get; set; } = true;
    public bool AuditDataAccess { get; set; } = true;
}

public class PerformanceOptions
{
    public int MaxTablesForInitialLoad { get; set; } = 500;
    public TimeoutOptions TimeoutSeconds { get; set; } = new();
    public bool EnableParallelProcessing { get; set; } = true;
}

public class TimeoutOptions
{
    public int SchemaScanning { get; set; } = 60;
    public int AIAnalysis { get; set; } = 30;
    public int HealthCheck { get; set; } = 10;
}

public class ImplicitFkDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.75;
    public List<string> NamingPatterns { get; set; } = new()
    {
        "^{ParentTable}Id$",
        "^{ParentTable}_ID$",
        "^Ma{ParentTable}$",
        "^{ParentTable}Code$",
        "^ID{ParentTable}$"
    };
    public bool RequireLLMConfirmation { get; set; } = true;
    public bool AllowDataValidation { get; set; } = false;
}

public class SemanticSearchOptions
{
    public bool Enabled { get; set; } = true;
    public double MinRelevanceScore { get; set; } = 0.6;
    public int MaxResults { get; set; } = 20;
    public bool GenerateSemanticTags { get; set; } = true;
    public List<string> SupportedLanguages { get; set; } = new() { "vi", "en" };
}
