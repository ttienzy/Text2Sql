# Prompt V2.0 Integration Implementation Plan

## Goal
- Complete the migration from legacy prompt assets/classes to the V2 YAML `PromptRegistry` architecture.
- Eliminate the legacy SQL correction monolith.
- Wire the previously orphaned V2 YAML prompts into runtime usage.
- Remove prompt naming mismatches and legacy DbExplorer prompt service drift.

## Audit Findings
- `TextToSqlAgent.Application/Services/DbExplorer/DatabaseAnalyzer.cs` still uses legacy `PromptTemplateService` and requests `column-interpretation`, while the V2 YAML file is `Prompts/v2.0/db-explorer/column-analysis.yaml`.
- `TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs` also still uses legacy `PromptTemplateService`, so `semantic-tagging.yaml` is not actually part of the runtime path yet.
- `TextToSqlAgent.Plugins/SqlCorrectorPlugin.cs` still depends on `SqlCorrectionPrompt.BuildUserPrompt(...)` and `IDatabaseAdapter.GetCorrectionSystemPrompt()`.
- `TextToSqlAgent.Infrastructure/Prompts/SqlCorrectionPrompt.cs` is a large legacy hardcoded prompt and should be removed after migration.
- `PromptRegistry` currently assumes a flatter template shape than the actual V2 YAML files provide, so loader/rendering hardening is needed for reliable V2 adoption.
- `IntentRoutingTask.cs` and `ImplicitRelationshipDetector.cs` do not currently execute the V2 YAML prompts selected in Option B.

## Files Planned For Change
- `TextToSqlAgent.Infrastructure/Prompts/PromptRegistry.cs`
- `TextToSqlAgent.Infrastructure/Prompts/PromptTemplate.cs`
- `Prompts/v2.0/sql-generation/correction.yaml`
- `TextToSqlAgent.Plugins/SqlCorrectorPlugin.cs`
- `TextToSqlAgent.Infrastructure/Prompts/SqlCorrectionPrompt.cs` (delete)
- `TextToSqlAgent.Core/Interfaces/IDatabaseAdapter.cs`
- `TextToSqlAgent.Infrastructure/Database/Adapters/SqlServer/SqlServerAdapter.cs`
- `TextToSqlAgent.Application/Services/DbExplorer/DatabaseAnalyzer.cs`
- `TextToSqlAgent.Application/Services/DbExplorer/SemanticTagGenerator.cs`
- `TextToSqlAgent.Application/Services/DbExplorer/ImplicitRelationshipDetector.cs`
- `TextToSqlAgent.Application/Services/DbExplorer/PromptTemplateService.cs` (likely delete if no callers remain)
- `TextToSqlAgent.Core/Tasks/IntentRoutingTask.cs`
- `TextToSqlAgent.API/Program.cs`
- `TextToSqlAgent.Console/Setup/DependencyInjection.cs`
- `TextToSqlAgent.Tests.Unit/Tasks/IntentRoutingTaskTests.cs`
- Additional unit tests for `PromptRegistry` and/or prompt-backed components if needed during refactor

## Execution Plan
1. Harden `PromptRegistry` so it can load and render the actual V2 YAML format used under `Prompts/v2.0/`.
2. Migrate SQL correction to `Prompts/v2.0/sql-generation/correction.yaml`, refactor `SqlCorrectorPlugin`, and remove the legacy `SqlCorrectionPrompt`.
3. Move DbExplorer prompt-backed flows from `PromptTemplateService` to `PromptRegistry`, resolving the `column-analysis` naming mismatch in the process.
4. Implement Option B:
   - use `core/ambiguity-resolution.yaml` and `sql-generation/rejection.yaml` from the intent-routing flow
   - use `db-explorer/fk-detection.yaml` from implicit relationship detection
5. Remove dead legacy prompt service code if it becomes unused.
6. Run focused tests/build validation and document any remaining risk.

## Design Notes
- Preferred fix for Step 1: align code to the canonical V2 YAML template name `column-analysis` rather than renaming the YAML file backward.
- `IntentRoutingTask` lives in `Core`, so direct `PromptRegistry` injection would be architecture-hostile. The likely solution is a small abstraction in `Core` with an implementation outside `Core` that uses `PromptRegistry` + `ILLMClient`.
- DbExplorer migration should keep safe fallbacks so prompt failures do not break schema analysis.
