using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// AI-powered database analyzer
/// Classifies domain, assigns table roles, detects health issues
/// </summary>
public class DatabaseAnalyzer
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<DatabaseAnalyzer> _logger;

    public DatabaseAnalyzer(
        ILLMClient llmClient,
        ILogger<DatabaseAnalyzer> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>
    /// Analyze database schema using AI
    /// </summary>
    public async Task<DatabaseAnalysis> AnalyzeAsync(
        EnhancedDatabaseSchema schema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DatabaseAnalyzer] Starting AI analysis of {Tables} tables...",
            schema.EnhancedTables.Count);

        try
        {
            // Build analysis prompt
            var prompt = BuildAnalysisPrompt(schema);

            // Call LLM
            var systemPrompt = GetSystemPrompt();
            var response = await _llmClient.CompleteWithSystemPromptAsync(
                systemPrompt,
                prompt,
                cancellationToken);

            // Parse response
            var analysis = ParseAnalysisResponse(response);
            analysis.AnalyzedAt = DateTime.UtcNow;

            // Apply analysis to schema
            ApplyAnalysisToSchema(schema, analysis);

            _logger.LogInformation(
                "[DatabaseAnalyzer] ✅ Analysis complete: Domain={Domain}, Modules={Modules}, Issues={Issues}",
                analysis.Domain,
                analysis.Modules.Count,
                analysis.HealthIssues.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to analyze schema");

            // Return fallback analysis with heuristics
            return CreateFallbackAnalysis(schema);
        }
    }

    private string GetSystemPrompt()
    {
        return @"You are an expert database consultant specializing in schema analysis.

Your task is to analyze database schemas and provide:
1. Domain classification (E-commerce, CRM, ERP, Healthcare, etc.)
2. Table role assignments (Master, Transaction, Bridge, Config, LogAudit)
3. Logical module grouping
4. Health issue detection

CRITICAL: Return ONLY valid JSON, no markdown, no explanations.

Table Role Definitions:
- Master: Core business entities, rarely change (Products, Customers, Categories)
- Transaction: Records business events (Orders, Payments, Invoices)
- Bridge: Junction tables for many-to-many (OrderItems, UserRoles)
- Config: System configuration (Settings, Permissions)
- LogAudit: Tracking and history (AuditLogs, ActivityLog)

Health Issues to detect:
- missing_index: FK columns without indexes
- orphan_table: Tables with no relationships
- inconsistent_naming: Similar columns with different names
- missing_primary_key: Tables without PK
- nullable_required: Important columns that should be NOT NULL";
    }

    private string BuildAnalysisPrompt(EnhancedDatabaseSchema schema)
    {
        var tables = schema.EnhancedTables
            .Select(t => new
            {
                name = t.TableName,
                row_count = t.RowCount,
                columns = t.Columns.Select(c => new
                {
                    name = c.ColumnName,
                    type = c.DataType,
                    is_pk = c.IsPrimaryKey,
                    is_fk = c.IsForeignKey,
                    nullable = c.IsNullable
                }).ToList(),
                primary_keys = t.PrimaryKeys,
                foreign_keys = t.ForeignKeys,
                indexes = t.Indexes.Select(i => i.IndexName).ToList()
            })
            .ToList();

        var relationships = schema.BaseSchema.Relationships
            .Select(r => $"{r.FromTable}.{r.FromColumn} → {r.ToTable}.{r.ToColumn}")
            .ToList();

        var prompt = $@"Analyze this database schema:

TABLES ({tables.Count} total):
{JsonSerializer.Serialize(tables, new JsonSerializerOptions { WriteIndented = true })}

RELATIONSHIPS ({relationships.Count} total):
{string.Join("\n", relationships)}

Provide analysis in this EXACT JSON format:
{{
  ""domain"": ""<domain classification>"",
  ""summary"": ""<1-2 sentence description>"",
  ""modules"": [
    {{
      ""name"": ""<module name>"",
      ""description"": ""<brief description>"",
      ""tables"": [""Table1"", ""Table2""]
    }}
  ],
  ""table_roles"": {{
    ""TableName"": {{
      ""role"": ""master|transaction|bridge|config|logaudit"",
      ""description"": ""<why this role>"",
      ""confidence"": 0.95
    }}
  }},
  ""health_issues"": [
    {{
      ""severity"": ""info|warning|critical"",
      ""type"": ""missing_index|orphan_table|inconsistent_naming|missing_primary_key|nullable_required"",
      ""table"": ""TableName"",
      ""column"": ""ColumnName"",
      ""description"": ""<issue description>"",
      ""recommendation"": ""<how to fix>""
    }}
  ],
  ""confidence"": 0.9
}}

Return ONLY the JSON, no markdown formatting.";

        return prompt;
    }

    private DatabaseAnalysis ParseAnalysisResponse(string response)
    {
        try
        {
            // Clean response
            var cleaned = response.Trim();

            // Remove markdown code blocks if present
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();
            }

            // Find JSON block
            var jsonStart = cleaned.IndexOf('{');
            var jsonEnd = cleaned.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            // Parse JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var result = JsonSerializer.Deserialize<DatabaseAnalysisDto>(cleaned, options);

            if (result == null)
            {
                throw new JsonException("Deserialization returned null");
            }

            // Convert DTO to domain model
            return ConvertDtoToModel(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to parse AI response: {Response}",
                response.Length > 500 ? response.Substring(0, 500) + "..." : response);
            throw;
        }
    }

    private DatabaseAnalysis ConvertDtoToModel(DatabaseAnalysisDto dto)
    {
        var analysis = new DatabaseAnalysis
        {
            Domain = dto.Domain ?? "Unknown",
            Summary = dto.Summary ?? "",
            Confidence = dto.Confidence
        };

        // Convert modules
        if (dto.Modules != null)
        {
            analysis.Modules = dto.Modules.Select(m => new DatabaseModule
            {
                Name = m.Name ?? "",
                Description = m.Description ?? "",
                Tables = m.Tables ?? new List<string>()
            }).ToList();
        }

        // Convert table roles
        if (dto.TableRoles != null)
        {
            foreach (var (tableName, roleDto) in dto.TableRoles)
            {
                var role = ParseTableRole(roleDto.Role);
                analysis.TableRoles[tableName] = new TableRoleInfo
                {
                    TableName = tableName,
                    Role = role,
                    Description = roleDto.Description ?? "",
                    Confidence = roleDto.Confidence
                };
            }
        }

        // Convert health issues
        if (dto.HealthIssues != null)
        {
            analysis.HealthIssues = dto.HealthIssues.Select(i => new HealthIssue
            {
                Severity = ParseSeverity(i.Severity),
                Type = ParseIssueType(i.Type),
                Table = i.Table ?? "",
                Column = i.Column,
                Description = i.Description ?? "",
                Recommendation = i.Recommendation ?? ""
            }).ToList();
        }

        return analysis;
    }

    private TableRole ParseTableRole(string? role)
    {
        return role?.ToLower() switch
        {
            "master" => TableRole.Master,
            "transaction" => TableRole.Transaction,
            "bridge" => TableRole.Bridge,
            "config" => TableRole.Config,
            "logaudit" => TableRole.LogAudit,
            _ => TableRole.Unknown
        };
    }

    private IssueSeverity ParseSeverity(string? severity)
    {
        return severity?.ToLower() switch
        {
            "critical" => IssueSeverity.Critical,
            "warning" => IssueSeverity.Warning,
            _ => IssueSeverity.Info
        };
    }

    private IssueType ParseIssueType(string? type)
    {
        return type?.ToLower() switch
        {
            "missing_index" => IssueType.MissingIndex,
            "orphan_table" => IssueType.OrphanTable,
            "inconsistent_naming" => IssueType.InconsistentNaming,
            "missing_primary_key" => IssueType.MissingPrimaryKey,
            "nullable_required" => IssueType.NullableRequired,
            _ => IssueType.UnusedTable
        };
    }

    private void ApplyAnalysisToSchema(EnhancedDatabaseSchema schema, DatabaseAnalysis analysis)
    {
        // Apply roles and modules to enhanced tables
        foreach (var table in schema.EnhancedTables)
        {
            if (analysis.TableRoles.TryGetValue(table.TableName, out var roleInfo))
            {
                table.Role = roleInfo.Role;
            }

            // Find module
            var module = analysis.Modules.FirstOrDefault(m => m.Tables.Contains(table.TableName));
            if (module != null)
            {
                table.Module = module.Name;
            }
        }

        // Store analysis in schema
        schema.Analysis = analysis;
    }

    private DatabaseAnalysis CreateFallbackAnalysis(EnhancedDatabaseSchema schema)
    {
        _logger.LogWarning("[DatabaseAnalyzer] Using fallback heuristic analysis");

        var analysis = new DatabaseAnalysis
        {
            Domain = "Unknown",
            Summary = "Database schema analysis using heuristics",
            Confidence = 0.5
        };

        // Simple heuristic role assignment
        foreach (var table in schema.EnhancedTables)
        {
            var role = InferTableRole(table, schema);
            analysis.TableRoles[table.TableName] = new TableRoleInfo
            {
                TableName = table.TableName,
                Role = role,
                Description = $"Inferred as {role} based on naming and structure",
                Confidence = 0.6
            };
        }

        // Detect basic health issues
        analysis.HealthIssues = DetectBasicHealthIssues(schema);

        return analysis;
    }

    private TableRole InferTableRole(EnhancedTableInfo table, EnhancedDatabaseSchema schema)
    {
        var name = table.TableName.ToLower();

        // Log/Audit tables
        if (name.Contains("log") || name.Contains("audit") || name.Contains("history"))
            return TableRole.LogAudit;

        // Config tables
        if (name.Contains("setting") || name.Contains("config") || name.Contains("permission"))
            return TableRole.Config;

        // Bridge tables (many FKs, few other columns)
        if (table.ForeignKeys.Count >= 2 && table.ColumnCount <= table.ForeignKeys.Count + 2)
            return TableRole.Bridge;

        // Transaction tables
        if (name.Contains("order") || name.Contains("payment") || name.Contains("invoice") ||
            name.Contains("transaction") || table.Columns.Any(c => c.ColumnName.ToLower().Contains("date")))
            return TableRole.Transaction;

        // Master tables (default)
        return TableRole.Master;
    }

    private List<HealthIssue> DetectBasicHealthIssues(EnhancedDatabaseSchema schema)
    {
        var issues = new List<HealthIssue>();

        foreach (var table in schema.EnhancedTables)
        {
            // Check for missing PK
            if (!table.PrimaryKeys.Any())
            {
                issues.Add(new HealthIssue
                {
                    Severity = IssueSeverity.Critical,
                    Type = IssueType.MissingPrimaryKey,
                    Table = table.TableName,
                    Description = $"Table '{table.TableName}' has no primary key",
                    Recommendation = "Add a primary key to ensure data integrity"
                });
            }

            // Check for FK without index
            foreach (var fk in table.ForeignKeys)
            {
                var hasIndex = table.Indexes.Any(i => i.Columns.Contains(fk));
                if (!hasIndex)
                {
                    issues.Add(new HealthIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Type = IssueType.MissingIndex,
                        Table = table.TableName,
                        Column = fk,
                        Description = $"Foreign key column '{fk}' has no index",
                        Recommendation = $"CREATE INDEX IX_{table.TableName}_{fk} ON [{table.TableName}]([{fk}])"
                    });
                }
            }

            // Check for orphan tables
            var hasRelationships = schema.BaseSchema.Relationships.Any(r =>
                r.FromTable == table.TableName || r.ToTable == table.TableName);

            if (!hasRelationships && table.ForeignKeys.Count == 0)
            {
                issues.Add(new HealthIssue
                {
                    Severity = IssueSeverity.Info,
                    Type = IssueType.OrphanTable,
                    Table = table.TableName,
                    Description = $"Table '{table.TableName}' has no relationships with other tables",
                    Recommendation = "Verify if this table is still needed or should be connected to other tables"
                });
            }
        }

        return issues;
    }

    // DTOs for JSON deserialization
    private class DatabaseAnalysisDto
    {
        public string? Domain { get; set; }
        public string? Summary { get; set; }
        public List<ModuleDto>? Modules { get; set; }
        public Dictionary<string, TableRoleDto>? TableRoles { get; set; }
        public List<HealthIssueDto>? HealthIssues { get; set; }
        public double Confidence { get; set; }
    }

    private class ModuleDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string>? Tables { get; set; }
    }

    private class TableRoleDto
    {
        public string? Role { get; set; }
        public string? Description { get; set; }
        public double Confidence { get; set; }
    }

    private class HealthIssueDto
    {
        public string? Severity { get; set; }
        public string? Type { get; set; }
        public string? Table { get; set; }
        public string? Column { get; set; }
        public string? Description { get; set; }
        public string? Recommendation { get; set; }
    }
}
