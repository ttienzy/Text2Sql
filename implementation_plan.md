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

---

# Python Sidecar Enterprise Implementation Roadmap

## Executive Direction
- Treat `python-sidecar` as an enterprise service, not a demo utility.
- Position the sidecar as an advisory ML capability by default, while final safety and policy enforcement remains authoritative in the .NET platform.
- Prioritize governance, safety, observability, reproducibility, and controlled rollout before adding model complexity.
- Separate concerns cleanly across serving, training, evaluation, packaging, and runtime operations.

## Enterprise Objectives
- Make intent classification safe enough for production-assisted routing.
- Ensure degraded behavior is explicit and observable.
- Create a reproducible ML lifecycle for data, model artifacts, evaluation, and rollout.
- Reduce operational risk in deployment, dependency management, and runtime behavior.
- Clarify whether visualization belongs in the critical path or should be treated as an optional UX capability.

## Current Concerns
- The current model quality is not yet strong enough to act as a sole authority for dangerous intents.
- Health signaling does not distinguish healthy ML serving from rule-based fallback or degraded mode.
- Artifact loading is permissive and lacks strong compatibility and integrity guarantees.
- Visualization is synchronous, memory-heavy, and optimized more for convenience than production resilience.
- Runtime and image composition are heavier than necessary for the current feature set.
- The training pipeline is functional but not yet governed like an enterprise ML workflow.

## Governance Principles
- Safety decisions for `WRITE_*`, `DDL`, and high-risk ambiguity must remain enforceable in .NET even if the sidecar fails, degrades, or misclassifies.
- Every model artifact must be traceable to a dataset version, evaluation result, threshold set, and release decision.
- Fallback behavior must be visible in logs, health, metrics, and client-facing service state.
- Production rollout should be phased, reversible, and measurable.

## Workstreams

### 1. Safety and Service Contract
- Define the formal responsibility boundary between the sidecar and the .NET application.
- Classify each sidecar output as advisory, enforceable, or informational.
- Introduce explicit service states:
  - `ready`
  - `degraded`
  - `model_missing`
  - `rule_fallback`
  - `not_ready`
- Define expected orchestrator behavior for each state.
- Specify response schema versioning and backward compatibility rules.

### 2. ML Quality and Evaluation
- Build an enterprise evaluation baseline before retraining.
- Define per-class targets, especially for:
  - `WRITE_INSERT`
  - `WRITE_UPDATE`
  - `WRITE_DELETE`
  - `DDL`
  - `AMBIGUOUS`
- Measure:
  - per-class precision
  - per-class recall
  - false negative rate for dangerous intents
  - confusion between `SELECT`, `AGGREGATE`, and `AMBIGUOUS`
  - language-specific performance for Vietnamese and English
- Introduce calibrated thresholds rather than relying on a single generic confidence cutoff.

### 3. Data and Label Governance
- Expand the labeled dataset substantially before the next production model release.
- Prioritize data collection for:
  - dangerous write intent phrasing
  - indirect imperative language
  - Vietnamese diacritic/no-diacritic variants
  - mixed Vietnamese-English prompts
  - ambiguous analytics phrasing
  - schema questions that resemble normal selects
- Create a review process for labels with spot-checking and disagreement resolution.
- Version datasets and keep evaluation splits fixed for comparability over time.

### 4. Runtime Hardening and Observability
- Distinguish startup liveness from true readiness.
- Expose model mode and degradation signals through health/readiness endpoints.
- Add structured metrics for:
  - request count
  - latency
  - fallback rate
  - low-confidence rate
  - model load failures
  - chart generation failures
- Add request correlation and traceability across .NET and Python.
- Define payload limits, concurrency expectations, and timeout behavior.

### 5. Artifact and Supply Chain Integrity
- Introduce a manifest for model artifacts containing:
  - model version
  - dataset version
  - feature configuration
  - label set
  - threshold profile
  - training timestamp
  - evaluation summary
- Add artifact compatibility validation at startup.
- Add artifact integrity checks before loading serialized model files.
- Separate training outputs from runtime-trusted artifacts.

### 6. Packaging and Deployment
- Split training dependencies from runtime dependencies.
- Reduce the runtime image to the minimum required footprint.
- Move toward reproducible builds and deterministic dependency resolution.
- Run the container with least privilege and a tighter runtime posture.
- Define environment-specific config strategy for local, staging, and production deployment.

### 7. Visualization Strategy
- Decide whether chart generation is a core enterprise capability or an optional convenience feature.
- If retained:
  - keep it out of the critical path of query execution
  - define stricter limits on rows, columns, and payload size
  - define a standard fallback when visualization is skipped
- If not retained as a service concern:
  - reduce the sidecar to intent-focused responsibility
  - move chart rendering closer to the frontend or a dedicated visualization service

## Phased Delivery Plan

### Phase 1. Contract and Safety Foundation
- Finalize the sidecar role in the overall architecture.
- Define readiness/degraded semantics.
- Define .NET fallback behavior for all sidecar states.
- Document enterprise safety rules and ownership boundaries.

Acceptance criteria:
- The platform can continue operating safely if the sidecar is unavailable or degraded.
- Health semantics are unambiguous to both operators and the .NET orchestrator.

### Phase 2. Evaluation and Data Baseline
- Audit current dataset coverage.
- Build a frozen evaluation suite.
- Define go/no-go quality thresholds.
- Identify the top confusion cases and missing data domains.

Acceptance criteria:
- A release candidate model can be judged against a fixed benchmark rather than intuition.
- Dangerous-intent failure modes are quantified.

### Phase 3. Model Improvement and Thresholding
- Retrain on expanded, reviewed data.
- Tune thresholds per class.
- Compare model families only if the current baseline cannot meet enterprise targets.
- Produce a release package with evaluation evidence.

Acceptance criteria:
- The model demonstrates measurable improvement on the fixed benchmark.
- Threshold behavior is documented and justified by risk class.

### Phase 4. Runtime and Operational Hardening
- Improve health/readiness contracts.
- Add metrics, tracing, and clearer degraded-state reporting.
- Formalize payload and timeout policies.
- Harden failure handling for model load and visualization paths.

Acceptance criteria:
- Operators can detect degraded service quickly.
- The .NET side can respond appropriately to sidecar state.

### Phase 5. Packaging and Release Readiness
- Slim the runtime image.
- separate serving and training concerns
- add artifact manifest and compatibility validation
- prepare staged rollout and rollback guidance

Acceptance criteria:
- The service is reproducible, supportable, and safer to promote across environments.

### Phase 6. Visualization Rationalization
- Reassess visualization’s ownership and runtime cost.
- Either harden it as a bounded enterprise feature or de-scope it from the sidecar.

Acceptance criteria:
- Visualization no longer introduces disproportionate runtime risk relative to its business value.

## Task Inventory

### Discovery Tasks
- Map all current integration points between .NET and `python-sidecar`.
- Inventory runtime dependencies, model artifacts, and environment variables.
- Identify which user-facing flows currently depend on sidecar availability.

### Architecture Tasks
- Write a short service contract for intent classification.
- Write a short degraded-mode contract for orchestration.
- Define ownership of safety decisions across services.

### ML Tasks
- Audit label quality and class balance.
- Create evaluation datasets for dangerous and ambiguous intents.
- Define per-class thresholds and acceptance metrics.
- Establish artifact versioning and promotion rules.

### Runtime Tasks
- Redesign health endpoints into liveness/readiness/degraded signaling.
- Add observability requirements and failure-state telemetry.
- Define limits for request size, latency, and concurrency.

### Packaging Tasks
- Separate training and serving dependency profiles.
- Define artifact trust model and startup validation rules.
- Create a deployment profile for enterprise environments.

### Visualization Tasks
- Evaluate whether visualization remains in scope for the sidecar.
- If yes, define non-critical execution semantics and stricter data shape limits.
- If no, define deprecation or relocation strategy.

## Risks to Manage
- Over-trusting ML confidence for dangerous intents.
- Shipping a stronger model without stronger governance.
- Hidden degraded mode causing silent behavior drift.
- Artifact drift between environments.
- Sidecar growth into a mixed-responsibility service that is hard to operate and reason about.

## Recommended Execution Order
1. Safety and service contract
2. Evaluation baseline and data audit
3. Runtime health and degraded semantics
4. Model improvement and thresholding
5. Packaging and artifact governance
6. Visualization decision and rationalization

## Delivery Recommendation
- Start with enterprise controls before model iteration.
- Do not optimize for raw model accuracy first if operational semantics are still weak.
- Treat the next implementation cycle as a platform hardening milestone, not just an ML tuning pass.

## Implementation Status
- Completed foundation work:
  - explicit sidecar service state contract
  - liveness and readiness endpoints
  - degraded-mode signaling in sidecar responses
  - .NET fallback behavior when sidecar is not enterprise-ready
  - advisory-only signaling between Python and .NET
- Completed evaluation baseline scaffolding:
  - fixed benchmark split generator
  - benchmark dataset manifest
  - release gates definition for dangerous intents
  - evaluation script scaffold for artifact-vs-benchmark checks
  - training script upgraded to prefer fixed benchmark mode when available
- Completed review workflow scaffolding:
  - review-lane documentation and record schema guidance
  - candidate dataset validator
  - approved-sample promotion script
  - seeded risky-intent review batch for `DDL`, `WRITE_DELETE`, `WRITE_UPDATE`, and `AMBIGUOUS`
- Remaining next-phase work:
  - human review and approval pass over seeded risky-intent candidates
  - merge approved samples into canonical training flow
  - model retraining and threshold tuning
  - packaging split between training and serving
  - visualization scope rationalization
