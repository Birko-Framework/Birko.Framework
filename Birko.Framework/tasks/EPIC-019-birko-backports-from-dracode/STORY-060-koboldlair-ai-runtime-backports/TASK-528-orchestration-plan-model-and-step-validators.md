---
id: TASK-528
parent: STORY-060
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.AI.Orchestration: a plan model with behaviour, step validators, and a pinned enum encoding

## Context

`Birko.AI.Orchestration`'s `ImplementationPlan` / `ImplementationStep` are a 62-line data bag. DraCode kept its own
`Models/Agents/KoboldImplementationPlan.cs` (735 lines):

- step lifecycle `Start` / `Complete` / `Fail` / `Skip`
- per-step `RetryCount`, `MaxRetries`, `ErrorCategory`, `Output`
- `StepExecutionMetrics` / `PlanExecutionMetrics`, `CurrentStepIndex` / `AdvanceToNextStep`, an execution log
- per-worker step assignment, `ReorderSteps` / `ValidateStepOrdering` on the dependency analyzer

`ImplementationStep.ExpectedContent` is documented "for verification" and nothing in Birko verifies it. DraCode's
`Models/Validation/IStepValidator.cs` + `StepValidators.cs` + `StepValidationService.cs` (~290 lines) do. DraCode also
forks `ITaskDispatcher` / `DirectTaskDispatcher`, `StepDependencyAnalyzer`, `EscalationAlert` (adds `KoboldId`),
`ReflectionEntry` and `TaskAssignment` — none of the Birko originals is referenced.

⚠ **Adoption hazard.** DraCode stores plan and step status as **integers** (`PlanEntity.Status` is `int`;
`KoboldPlanService._jsonOptions` has no `JsonStringEnumConverter`), and the enums are ordered differently:

- `StepStatus`: Birko `…Completed, Failed, Skipped`; DraCode `…Completed, Skipped, Failed`
- `PlanStatus`: Birko `Created, InProgress, Completed, Failed`; DraCode `Planning, Ready, InProgress, Completed, Failed`

A naive type swap silently turns every stored Failed step into Skipped and shifts every plan status.

Adopted in the consumer by DraCode TASK-126 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] Plan / step lifecycle, retry bookkeeping, metrics and current-step navigation on Birko's model
- [x] Step validators (files created / modified, expected content present) ship and drive `ExpectedContent`
- [x] The validator result type does not shadow `Birko.Validation.ValidationResult` (DraCode's does)
- [x] `EscalationAlert` carries a worker id
- [x] Enum encoding pinned (explicit numeric values or string serialization) and documented; the DraCode → Birko value mapping written down for the adoption
- [ ] ~~Optional~~ (declined, see Resolution): DraCode's `MessageQueue/QueueTaskDispatcher.cs` (52 lines, `ITaskDispatcher` over `Birko.MessageQueue`) — in `Birko.AI.Orchestration` only if CLAUDE-projects.md § Dependency Flow allows the edge, otherwise a small companion
- [x] Tests: lifecycle transitions, reorder and ordering validation, each validator

## Out of scope

- DraCode's `ReflectionTool` — it keeps the current plan and run id in `static` fields (rule 36 shape; parallel Kobolds
  overwrite each other). Fixed DraCode-side in its TASK-120; backport later if at all

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
Done directly.

## Resolution (2026-10-11)

- **Model** (`Models/ImplementationPlan.cs`, `Models/ExecutionMetrics.cs`):
  - Step: `Start` / `Complete` / `Fail` / `Skip`, `Output`, `ErrorCategory`, `RetryCount` / `MaxRetries` / `CanRetry`,
    `Metrics`. `RetryOrFail(error, category, retryable)` is DraCode's `UpdatePlanStepTool` retry block made a
    method.
  - Plan: `CurrentStepIndex` / `CurrentStep` / `AdvanceToNextStep`, `MarkInProgress` / `MarkCompleted` /
    `MarkFailed`, `ExecutionLog`, `GetAggregatedMetrics`, virtual `CaptureLessonsLearned`, `AssignSteps` /
    `GetAssignedSteps`.
  - Birko's `ErrorMessage` is kept, not renamed to DraCode's `LastErrorMessage`.
  - Not sealed, so DraCode derives its own plan for the spec fields (`PlanFilename`, `Feature*`, `Specification*`).
- **Analyzer:** `ValidateOrdering(plan)` and `Reorder(plan)`. They live on `StepDependencyAnalyzer`, not on the plan.
- **Validators:** `Validation/` holds `IStepValidator`, `StepValidationResult` (no shadowing),
  `CombinedStepValidationResult`, the three validators, and `StepValidationService`.
- **`EscalationAlert.WorkerId`** is a `string`, the same key as `AssignedStepIndices`. DraCode's `KoboldId` is a `Guid`.
- **Encoding:** every orchestration enum has explicit values. Plan and step status were **renumbered to DraCode's
  values**, so the adoption needs no data migration. That was possible because no consumer stored Birko's:
  - DraCode uses its fork;
  - Symbio and the Sandbox import the projitems but reference no type, as both `grep` and Symbio's build show.

  The mapping table is in `Birko.AI.Orchestration/CLAUDE.md` § Adopting from DraCode.KoboldLair. The CHANGELOG
  carries a BREAKING migration for anyone who persisted the old integers.
- **QueueTaskDispatcher declined.** Orchestration imports nothing from Birko. The `Birko.MessageQueue` edge would
  reach every importer (Symbio, the Sandbox) for one 52-line class, so it stays a consumer class.

**DraCode defects not carried over** (each one is pinned by a test):

| Defect | What Birko does instead |
|---|---|
| `ValidateStepOrdering` used the symmetric `HasDependency`, so a correct creator-then-modifier order was reported as a violation, with an empty file list | Only a step that modifies a file a later step creates is a violation |
| `ReorderSteps` swallowed exceptions | `Reorder` refuses a started plan or one with assignments |
| `GetAssignedSteps` gave a worker missing from a split plan every step | That worker gets no steps |
| `ContentExpectationValidator` passed when there were no files to check or a file could not be read | Both fail |
| `ValidationAttempts` was counted once per validator | Counted once per validation |

DraCode's own analyzer copy still has the CR-H003 `Reverse()` defect; this is recorded for its adoption.

**Proof.** `tests/Birko.AI.Orchestration.Tests` passes 39/39: 7 existing analyzer tests plus 32 new ones in
`ImplementationPlanTests`, `StepOrderingTests` and `StepValidatorTests`. The validators run against a real temporary
directory.

| Mutation | Red tests |
|---|---|
| Missing worker gets every step | 1 |
| Symmetric ordering check | 3 |
| Expected content with no files passes | 1 |
| DraCode's old `Skipped`/`Failed` values | 2 |
| Attempt count removed | 1 |

**Consumer builds:**
- `Symbio.Birko` builds clean.
- `DraCode.KoboldLair.Server` fails only on the known TASK-527 `CheckpointInterval` removal. There are no ambiguity
  errors from the new type names.
