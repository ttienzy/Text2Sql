using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Console.Agent;

public class TextToSqlAgentOrchestrator
{
    private readonly NormalizePromptTask _normalizeTask;
    private readonly IntentAnalysisPlugin _intentPlugin;
    private readonly SchemaScanner _schemaScanner;
    private readonly SchemaIndexer _schemaIndexer;
    private readonly SchemaRetriever _schemaRetriever;
    private readonly QdrantService _qdrantService;
    private readonly SqlGeneratorPlugin _sqlGenerator;
    private readonly SqlCorrectorPlugin _sqlCorrector;
    private readonly SqlExecutor _sqlExecutor;
    private readonly AgentConfig _agentConfig;
    private readonly DatabaseConfig _dbConfig;
    private readonly ILogger<TextToSqlAgentOrchestrator> _logger;

    private DatabaseSchema? _cachedSchema;
    private bool _schemaIndexed = false;

    public TextToSqlAgentOrchestrator(
        NormalizePromptTask normalizeTask,
        IntentAnalysisPlugin intentPlugin,
        SchemaScanner schemaScanner,
        SchemaIndexer schemaIndexer,
        SchemaRetriever schemaRetriever,
        QdrantService qdrantService,
        SqlGeneratorPlugin sqlGenerator,
        SqlCorrectorPlugin sqlCorrector,
        SqlExecutor sqlExecutor,
        AgentConfig agentConfig,
        DatabaseConfig dbConfig,
        ILogger<TextToSqlAgentOrchestrator> logger)
    {
        _normalizeTask = normalizeTask;
        _intentPlugin = intentPlugin;
        _schemaScanner = schemaScanner;
        _schemaIndexer = schemaIndexer;
        _schemaRetriever = schemaRetriever;
        _qdrantService = qdrantService;
        _sqlGenerator = sqlGenerator;
        _sqlCorrector = sqlCorrector;
        _sqlExecutor = sqlExecutor;
        _agentConfig = agentConfig;
        _dbConfig = dbConfig;
        _logger = logger;
    }

    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        CancellationToken cancellationToken = default)
    {
        var response = new AgentResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("[Agent] Bắt đầu xử lý câu hỏi");
            _logger.LogInformation("========================================");

            // ====================================
            // STEP 1: Normalize Prompt
            // ====================================
            steps.Add("Step 1: Chuẩn hóa câu hỏi");
            var normalized = await _normalizeTask.ExecuteAsync(userQuestion, cancellationToken);

            // ====================================
            // STEP 1.5: Setup Qdrant Collection Name
            // ====================================
            try 
            {
                var builder = new SqlConnectionStringBuilder(_dbConfig.ConnectionString);
                var dbName = builder.InitialCatalog; // Or builder.DataSource if appropriate, but InitialCatalog is usually the DB name
                if (!string.IsNullOrEmpty(dbName))
                {
                    _qdrantService.SetCollectionName(dbName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Agent] Không thể lấy tên database từ connection string. Sử dụng default collection name.");
            }

            // ====================================
            // STEP 2: Scan Schema (if not cached)
            // ====================================
            if (_cachedSchema == null)
            {
                steps.Add("Step 2: Quét schema database");

                try
                {
                    _cachedSchema = await _schemaScanner.ScanAsync(cancellationToken);
                }
                catch (DatabaseConnectionException ex)
                {
                    _logger.LogError(ex, "[Agent] Cannot connect to database");
                    response.Success = false;
                    response.ErrorMessage = "Cannot connect to database. Please check your connection string.";
                    response.ProcessingSteps = steps;
                    return response;
                }
                catch (DatabasePermissionException ex)
                {
                    _logger.LogError(ex, "[Agent] Insufficient database permissions");
                    response.Success = false;
                    response.ErrorMessage = "Insufficient database permissions. Please grant SELECT on INFORMATION_SCHEMA.";
                    response.ProcessingSteps = steps;
                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Agent] Failed to scan database schema");
                    response.Success = false;
                    response.ErrorMessage = $"Failed to scan database schema: {ex.Message}";
                    response.ProcessingSteps = steps;
                    return response;
                }

                // Auto-index schema after first scan
                if (!_schemaIndexed)
                {
                    steps.Add("Step 2.5: Index schema vào vector database");

                    // Use TryEnsure instead of Ensure to prevent crash
                    if (!await TryEnsureSchemaIndexedAsync(_cachedSchema, cancellationToken))
                    {
                        AnsiConsole.MarkupLine("[yellow]⚠️  Warning: RAG is not available. Using full schema.[/]");
                        AnsiConsole.MarkupLine("[dim]   Reason: Could not connect to Qdrant vector database[/]");
                        AnsiConsole.MarkupLine("[dim]   Impact: Queries may be slower and less accurate[/]");
                    }

                    _schemaIndexed = true;
                }
            }
            else
            {
                steps.Add("Step 2: Sử dụng schema đã cache");
                _logger.LogInformation("[Agent] Sử dụng schema đã cache");
            }

            // ====================================
            // STEP 3: RAG - Retrieve Relevant Schema
            // ====================================
            steps.Add("Step 3: RAG - Tìm kiếm schema liên quan");
            var relevantSchema = await _schemaRetriever.RetrieveAsync(
                normalized.NormalizedText,
                _cachedSchema,
                cancellationToken);

            _logger.LogInformation(
                "[Agent] RAG tìm thấy: {Tables} bảng, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            // ====================================
            // STEP 4: Intent Analysis (với RAG context)
            // ====================================

            // ========================================
            // FALLBACK: Nếu RAG không tìm thấy gì
            // ========================================
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[Agent] RAG không tìm thấy schema, fallback sang full schema");

                // Get table names for intent analysis
                var tableNamess = _cachedSchema.Tables.Select(t => t.TableName).ToList();

                // Continue with intent analysis...
                var intents = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNamess,
                    cancellationToken);

                // Build relevant schema based on intent target
                relevantSchema = BuildFallbackSchema(intents.Target, _cachedSchema);

                _logger.LogInformation(
                    "[Agent] Fallback schema: {Tables} bảng",
                    relevantSchema.RelevantTables.Count);
            }
            steps.Add("Step 4: Phân tích ý định");
            var tableNames = relevantSchema.RelevantTables.Select(t => t.TableName).ToList();
            var intent = await _intentPlugin.AnalyzeIntentAsync(
                normalized.NormalizedText,
                tableNames,
                cancellationToken);

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Câu hỏi chưa rõ ràng.";
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 5: Generate SQL (với RAG context)
            // ====================================
            steps.Add("Step 5: Sinh SQL query với RAG context");
            var sql = await _sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                cancellationToken);

            // ====================================
            // STEP 6: Validate SQL
            // ====================================
            steps.Add("Step 6: Validate SQL");
            if (!_sqlGenerator.ValidateSql(sql))
            {
                response.Success = false;
                response.ErrorMessage = "SQL không an toàn";
                response.SqlGenerated = sql;
                response.ProcessingSteps = steps;
                return response;
            }

            sql = _sqlGenerator.EnsureLimit(sql);

            // ====================================
            // STEP 7: Execute SQL với Self-Correction
            // ====================================
            steps.Add("Step 7: Thực thi SQL với self-correction");
            var (executionResult, corrections) = await ExecuteWithSelfCorrectionAsync(
                sql,
                relevantSchema,
                intent,
                cancellationToken);

            response.CorrectionHistory = corrections;
            response.WasCorrected = corrections.Any();
            response.CorrectionAttempts = corrections.Count;

            if (!executionResult.Success)
            {
                response.Success = false;
                response.ErrorMessage = executionResult.ErrorMessage;
                response.SqlGenerated = sql;
                response.QueryResult = executionResult;
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 8: Format Answer
            // ====================================
            steps.Add("Step 8: Diễn giải kết quả");
            var answer = FormatAnswer(intent, executionResult, corrections);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            _logger.LogInformation("[Agent] ✓ Hoàn thành xử lý");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Agent] Lỗi khi xử lý câu hỏi");

            response.Success = false;
            response.ErrorMessage = $"Lỗi: {ex.Message}";
            response.ProcessingSteps = steps;

            return response;
        }
    }
    private RetrievedSchemaContext BuildFallbackSchema(string targetTable, DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();

        // Find target table
        var table = fullSchema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

        if (table != null)
        {
            context.RelevantTables.Add(table);
            context.TableColumns[table.TableName] = table.Columns;

            // Find related tables via FK
            var relatedRels = fullSchema.Relationships.Where(r =>
                r.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase) ||
                r.ToTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var rel in relatedRels)
            {
                context.RelevantRelationships.Add(rel);

                // Add related table
                var relatedTableName = rel.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)
                    ? rel.ToTable
                    : rel.FromTable;

                var relatedTable = fullSchema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(ExtractTableName(relatedTableName), StringComparison.OrdinalIgnoreCase));

                if (relatedTable != null && !context.RelevantTables.Contains(relatedTable))
                {
                    context.RelevantTables.Add(relatedTable);
                    context.TableColumns[relatedTable.TableName] = relatedTable.Columns;
                }
            }
        }

        return context;
    }

    private string ExtractTableName(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    // ====================================
    // SELF-CORRECTION LOOP
    // ====================================
    private async Task<(SqlExecutionResult Result, List<CorrectionAttempt> Corrections)> ExecuteWithSelfCorrectionAsync(
    string initialSql,
    RetrievedSchemaContext schemaContext,
    IntentAnalysis intent,  // ← ADD
    CancellationToken cancellationToken)
    {
        var corrections = new List<CorrectionAttempt>();
        var currentSql = initialSql;
        var attemptNumber = 0;

        while (attemptNumber < _agentConfig.MaxSelfCorrectionAttempts)
        {
            _logger.LogInformation("[Agent] Thực thi SQL (Attempt #{Attempt})", attemptNumber + 1);

            // Execute
            var result = await _sqlExecutor.ExecuteAsync(currentSql, cancellationToken);

            // Success!
            if (result.Success)
            {
                if (corrections.Any())
                {
                    _logger.LogInformation("[Agent] ✓ SQL đã được tự động sửa và chạy thành công sau {Count} lần thử",
                        attemptNumber);
                }
                return (result, corrections);
            }

            // Failed - try to correct
            _logger.LogWarning("[Agent] SQL lỗi: {Error}", result.ErrorMessage);

            attemptNumber++;

            if (attemptNumber >= _agentConfig.MaxSelfCorrectionAttempts)
            {
                _logger.LogError("[Agent] Đã hết số lần tự sửa ({Max})", _agentConfig.MaxSelfCorrectionAttempts);
                return (result, corrections);
            }

            // Attempt correction
            _logger.LogInformation("[Agent] Bắt đầu tự sửa lỗi...");

            var correction = await _sqlCorrector.CorrectSqlAsync(
                currentSql,
                result.ErrorMessage ?? "Unknown error",
                schemaContext,
                intent,  // ← PASS intent
                attemptNumber,
                cancellationToken);

            corrections.Add(correction);

            if (!correction.Success)
            {
                _logger.LogWarning("[Agent] Không thể tự sửa lỗi");
                return (result, corrections);
            }

            if (!_sqlCorrector.ShouldRetry(corrections, _agentConfig.MaxSelfCorrectionAttempts))
            {
                _logger.LogWarning("[Agent] Dừng retry");
                return (result, corrections);
            }

            // Use corrected SQL for next attempt
            currentSql = correction.CorrectedSql;

            _logger.LogInformation("[Agent] Thử lại với SQL đã sửa...");
        }

        var finalResult = await _sqlExecutor.ExecuteAsync(currentSql, cancellationToken);
        return (finalResult, corrections);
    }

    private string FormatAnswer(
        IntentAnalysis intent,
        SqlExecutionResult result,
        List<CorrectionAttempt> corrections)
    {
        var answer = "";

        // Add correction info if any
        if (corrections.Any())
        {
            answer += $"ℹ️  SQL đã được tự động sửa {corrections.Count} lần.\n";
        }
        if (result.RowCount == 0)
        {
            return answer + "Không tìm thấy kết quả nào.";
        }
        answer += intent.Intent switch
        {
            QueryIntent.COUNT => $"Có tất cả {result.Rows[0].Values.First()} bản ghi.",
            QueryIntent.LIST => $"Tìm thấy {result.RowCount} kết quả.",
            QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                $"Database có {result.RowCount} bảng.",
            QueryIntent.AGGREGATE => $"Kết quả phân tích: {result.RowCount} nhóm dữ liệu.",
            QueryIntent.DETAIL => $"Thông tin chi tiết: {result.RowCount} bản ghi.",
            _ => $"Truy vấn thành công, trả về {result.RowCount} kết quả."
        }; return answer;
    }
    public void ClearSchemaCache()
    {
        _cachedSchema = null;
        _schemaIndexed = false;
        _logger.LogInformation("[Agent] Schema cache đã được xóa");
    }

    private async Task<bool> TryEnsureSchemaIndexedAsync(
    DatabaseSchema schema,
    CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[Agent] Kiểm tra Qdrant collection...");

            await _qdrantService.EnsureCollectionAsync(cancellationToken);
            var pointCount = await _qdrantService.GetPointCountAsync(cancellationToken);

            if (pointCount == 0)
            {
                _logger.LogInformation("[Agent] Index schema vào Qdrant...");
                await _schemaIndexer.IndexSchemaAsync(schema, cancellationToken);
                _logger.LogInformation("[Agent] ✓ Schema đã được index");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Agent] Lỗi khi index schema");
            return false;
        }
    }
}