using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models.DbExplorer;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// AI-powered database analyzer
/// Classifies domain, assigns table roles, detects health issues
/// </summary>
public class DatabaseAnalyzer
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<DatabaseAnalyzer> _logger;
    private readonly RuleEngine _ruleEngine;
    private readonly PromptRegistry _promptRegistry;
    private readonly ImplicitRelationshipDetector _implicitFkDetector;
    private readonly DbExplorerQdrantIndexer? _qdrantIndexer;

    public DatabaseAnalyzer(
        ILLMClient llmClient,
        ILogger<DatabaseAnalyzer> logger,
        RuleEngine ruleEngine,
        PromptRegistry promptRegistry,
        ImplicitRelationshipDetector implicitFkDetector,
        DbExplorerQdrantIndexer? qdrantIndexer = null)
    {
        _llmClient = llmClient;
        _logger = logger;
        _ruleEngine = ruleEngine;
        _promptRegistry = promptRegistry;
        _implicitFkDetector = implicitFkDetector;
        _qdrantIndexer = qdrantIndexer;
    }

    /// <summary>
    /// Analyze database schema - LIGHTWEIGHT overview only (table names → domain + modules)
    /// </summary>
    public async Task<DatabaseAnalysis> AnalyzeOverviewAsync(
        EnhancedDatabaseSchema schema,
        string? systemContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DatabaseAnalyzer] Starting LIGHTWEIGHT overview analysis of {Tables} tables...",
            schema.EnhancedTables.Count);

        DatabaseAnalysis analysis;
        try
        {
            // Load rules for health checks (metadata-only, fast)
            await _ruleEngine.LoadRulesAsync();

            // Build lightweight prompt (table names + relationships only)
            var variables = new Dictionary<string, object>
            {
                ["system_context"] = systemContext ?? "No specific context provided.",
                ["domain"] = ExtractDomain(systemContext),
                ["table_count"] = schema.EnhancedTables.Count,
                ["table_names"] = string.Join(", ", schema.EnhancedTables.Select(t => t.TableName)),
                ["relationship_count"] = schema.BaseSchema.Relationships.Count,
                ["relationships"] = string.Join("\n", schema.BaseSchema.Relationships
                    .Take(20) // Limit to first 20 relationships
                    .Select(r => $"{r.FromTable} → {r.ToTable}"))
            };

            var response = await CompletePromptAsync(
                "db-explorer/schema-summary",
                variables,
                cancellationToken);

            // Parse response
            analysis = ParseOverviewResponse(response);
            analysis.AnalyzedAt = DateTime.UtcNow;

            // Run metadata-only health checks (fast, no LLM)
            analysis.HealthIssues = _ruleEngine.ExecuteRules(schema);

            // Apply basic role inference (heuristic, no LLM)
            ApplyHeuristicRoles(schema, analysis);

            _logger.LogInformation(
                "[DatabaseAnalyzer] ✅ AI Overview complete: Domain={Domain}, Modules={Modules}, Issues={Issues}",
                analysis.Domain,
                analysis.Modules.Count,
                analysis.HealthIssues.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to analyze AI overview, using fallback");
            analysis = CreateFallbackAnalysis(schema);
        }

        // ALWAYS index schema with semantic tags into Qdrant (if available)
        // This ensures search works even if AI analysis falls back to heuristics
        if (_qdrantIndexer != null)
        {
            try
            {
                _logger.LogInformation("[DatabaseAnalyzer] Indexing schema with semantic tags into Qdrant...");
                await _qdrantIndexer.IndexSchemaWithSemanticTagsAsync(
                    schema,
                    systemContext,
                    cancellationToken);
                _logger.LogInformation("[DatabaseAnalyzer] ✅ Qdrant indexing complete");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DatabaseAnalyzer] Qdrant indexing failed (non-critical)");
                // Don't fail the entire analysis if Qdrant indexing fails
            }
        }
        else
        {
            _logger.LogDebug("[DatabaseAnalyzer] Qdrant indexer not available, skipping semantic tag indexing");
        }

        return analysis;
    }

    /// <summary>
    /// Analyze single table in detail - ON-DEMAND (column interpretation + implicit FK)
    /// </summary>
    public async Task<TableDetailAnalysis> AnalyzeTableDetailAsync(
        EnhancedTableInfo table,
        EnhancedDatabaseSchema schema,
        string? systemContext = null,
        string? namingConventionNotes = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DatabaseAnalyzer] Analyzing table detail: {TableName}", table.TableName);

        var result = new TableDetailAnalysis
        {
            TableName = table.TableName,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            // 1. Column Interpretation (if has columns)
            if (table.Columns.Any())
            {
                result.ColumnInterpretations = await InterpretColumnsAsync(
                    table,
                    systemContext,
                    namingConventionNotes,
                    cancellationToken);
            }

            // 2. Implicit FK Detection (LLM-assisted validation over metadata candidates)
            result.ImplicitRelationships = await DetectImplicitForeignKeysAsync(
                table,
                schema,
                systemContext,
                cancellationToken);

            // 3. Table-specific health issues
            result.HealthIssues = _ruleEngine.ExecuteRules(schema)
                .Where(i => i.Table == table.TableName)
                .ToList();

            _logger.LogInformation(
                "[DatabaseAnalyzer] ✅ Table detail complete: {Columns} columns interpreted, {ImplicitFKs} implicit FKs",
                result.ColumnInterpretations.Count,
                result.ImplicitRelationships.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to analyze table detail for {TableName}", table.TableName);
            return result; // Return partial result
        }
    }

    /// <summary>
    /// Interpret column names using AI (with context)
    /// </summary>
    private async Task<Dictionary<string, ColumnMeaning>> InterpretColumnsAsync(
        EnhancedTableInfo table,
        string? systemContext,
        string? namingConventionNotes,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object>
        {
            ["system_context"] = systemContext ?? "No specific context provided.",
            ["domain"] = ExtractDomain(systemContext),
            ["naming_convention_notes"] = namingConventionNotes ?? string.Empty,
            ["table_name"] = table.TableName,
            ["table_description"] = string.Empty, // TODO: Get from analysis if available
            ["columns"] = string.Join("\n", table.Columns.Select(c =>
                $"- {c.ColumnName} ({c.DataType}{(c.IsNullable ? " NULL" : " NOT NULL")}{(c.IsPrimaryKey ? " PK" : "")}{(c.IsForeignKey ? " FK" : "")}"))
        };

        var response = await CompletePromptAsync(
            "db-explorer/column-analysis",
            variables,
            cancellationToken);

        return ParseColumnInterpretations(response);
    }

    /// <summary>
    /// Detect implicit foreign keys (metadata-only, no data queries)
    /// </summary>
    private async Task<List<ImplicitRelationship>> DetectImplicitForeignKeysAsync(
        EnhancedTableInfo table,
        EnhancedDatabaseSchema schema,
        string? systemContext,
        CancellationToken cancellationToken)
    {
        return await _implicitFkDetector.DetectImplicitForeignKeysAsync(
            table,
            schema,
            systemContext,
            cancellationToken);
    }

    private async Task<string> CompletePromptAsync(
        string templateName,
        Dictionary<string, object> variables,
        CancellationToken cancellationToken)
    {
        var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
            templateName,
            new List<string>(),
            variables,
            includeExamples: false);

        return await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);
    }

    /// <summary>
    /// Parse overview response (lightweight)
    /// </summary>
    private DatabaseAnalysis ParseOverviewResponse(string response)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var dto = JsonSerializer.Deserialize<OverviewAnalysisDto>(cleaned, options);

            return new DatabaseAnalysis
            {
                Domain = dto?.Domain ?? "Unknown",
                Summary = dto?.Summary ?? "",
                KeyTables = dto?.KeyTables ?? new List<string>(),
                DataFlowPattern = dto?.DataFlowPattern,
                Modules = dto?.Modules?.Select(m => new DatabaseModule
                {
                    Name = m.Name ?? "",
                    Description = m.Description ?? "",
                    Tables = m.Tables ?? new List<string>()
                }).ToList() ?? new List<DatabaseModule>(),
                TechnicalDebt = dto?.TechnicalDebt ?? new List<string>(),
                Confidence = dto?.Confidence ?? 0.5
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to parse overview response");
            return new DatabaseAnalysis
            {
                Domain = "Unknown",
                Summary = "Failed to parse AI response",
                Confidence = 0.3
            };
        }
    }

    /// <summary>
    /// Parse column interpretations
    /// </summary>
    private Dictionary<string, ColumnMeaning> ParseColumnInterpretations(string response)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<Dictionary<string, ColumnMeaningDto>>(cleaned, options);

            return result?.ToDictionary(
                kvp => kvp.Key,
                kvp => new ColumnMeaning
                {
                    Vietnamese = kvp.Value.Meaning ?? "",
                    English = kvp.Value.English ?? "",
                    Description = kvp.Value.Description ?? "",
                    Confidence = kvp.Value.Confidence
                }) ?? new Dictionary<string, ColumnMeaning>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DatabaseAnalyzer] Failed to parse column interpretations");
            return new Dictionary<string, ColumnMeaning>();
        }
    }

    /// <summary>
    /// Clean JSON response (remove markdown, extract JSON)
    /// </summary>
    private string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove markdown code blocks
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

        return cleaned;
    }

    /// <summary>
    /// Extract domain from system context
    /// </summary>
    private string ExtractDomain(string? systemContext)
    {
        if (string.IsNullOrEmpty(systemContext))
            return "Unknown";

        // Try to extract domain from context
        var lower = systemContext.ToLower();
        if (lower.Contains("e-commerce") || lower.Contains("bán lẻ"))
            return "E-commerce";
        if (lower.Contains("erp") || lower.Contains("doanh nghiệp"))
            return "ERP";
        if (lower.Contains("crm") || lower.Contains("khách hàng"))
            return "CRM";
        if (lower.Contains("healthcare") || lower.Contains("y tế"))
            return "Healthcare";

        return "Unknown";
    }

    /// <summary>
    /// Apply heuristic roles (fast, no LLM)
    /// </summary>
    private void ApplyHeuristicRoles(EnhancedDatabaseSchema schema, DatabaseAnalysis analysis)
    {
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

            table.Role = role;

            // Assign to module
            var module = analysis.Modules.FirstOrDefault(m => m.Tables.Contains(table.TableName));
            if (module != null)
            {
                table.Module = module.Name;
            }
        }
    }

    /// <summary>
    /// Analyze database schema using AI - LEGACY METHOD (full analysis)
    /// </summary>
    [Obsolete("Use AnalyzeOverviewAsync for initial load, then AnalyzeTableDetailAsync for on-demand")]
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
        // Use RuleEngine instead of hard-coded logic
        _logger.LogInformation("[DatabaseAnalyzer] Using RuleEngine for health checks");
        return _ruleEngine.ExecuteRules(schema);
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

    // DTOs for new lazy loading methods
    private class OverviewAnalysisDto
    {
        public string? Domain { get; set; }
        public string? Summary { get; set; }
        public List<string>? KeyTables { get; set; }
        public string? DataFlowPattern { get; set; }
        public List<ModuleDto>? Modules { get; set; }
        public List<string>? TechnicalDebt { get; set; }
        public double Confidence { get; set; }
    }

    private class ColumnMeaningDto
    {
        public string? Meaning { get; set; }
        public string? English { get; set; }
        public string? Description { get; set; }
        public double Confidence { get; set; }
    }
}
