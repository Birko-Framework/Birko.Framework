# Birko.AI.Orchestration

## Overview
Multi-agent task orchestration — dispatch, planning, dependency analysis, escalation.

## Project Location
`Birko.AI.Orchestration/`

## Namespace
`Birko.AI.Orchestration.Models`, `Birko.AI.Orchestration.Dispatch`, `Birko.AI.Orchestration.Services`, `Birko.AI.Orchestration.Validation`

## Components

### Models/AgentTaskRecord.cs
- `AgentTaskRecord` — Persistent record of a task assigned to an agent

### Models/ImplementationPlan.cs
- `ImplementationPlan` — ordered steps plus execution state: `CurrentStepIndex` / `CurrentStep` / `AdvanceToNextStep`,
  `MarkInProgress` / `MarkCompleted` / `MarkFailed`, `ExecutionLog` (`AddLogEntry`), per-worker
  `AssignSteps` / `GetAssignedSteps`, `GetAggregatedMetrics`, `CaptureLessonsLearned` (virtual), `Reflections`,
  `Escalations`. Not sealed — a consumer adds its own context by deriving.
- `ImplementationStep` — `Start` / `Complete` / `Fail` / `Skip`; `RetryOrFail(error, category, retryable)` is the
  retry decision (`RetryCount` / `MaxRetries`, back to `Pending` while retries remain); `ErrorMessage` keeps the
  last error across retries; `Metrics`; `ExpectedContent` (checked by `ContentExpectationValidator`).
- `PlanStatus`, `StepStatus` — **numeric values pinned** (see Rules).

### Models/ExecutionMetrics.cs
- `StepExecutionMetrics`, `PlanExecutionMetrics`, `PlanLogEntry`

### Models/TaskAssignment.cs
- `TaskAssignment` — Assignment of a task to a specific agent type

### Models/EscalationAlert.cs
- `EscalationAlert` — Alert raised when a task fails or requires human intervention

### Dispatch/ITaskDispatcher.cs
- `ITaskDispatcher` — Interface for dispatching tasks to agents

### Dispatch/DirectTaskDispatcher.cs
- `DirectTaskDispatcher` — In-process task dispatcher implementation

### Services/StepDependencyAnalyzer.cs
- `StepDependencyAnalyzer` — parallel groups, `SuggestOptimalOrder` (topological), `ValidateOrdering(plan)` (a step
  modifying a file a *later* step creates), `Reorder(plan)` (renumbers 1..n; refuses a started plan or one with
  assignments — both are positions in `Steps`)

### Validation/
- `IStepValidator` → `StepValidationResult` (named apart from `Birko.Validation.ValidationResult`)
- `FileCreationValidator`, `FileModificationValidator` (written after `StartedAt`), `ContentExpectationValidator`
- `StepValidationService` — runs them, `CombinedStepValidationResult`; a throwing validator is a failed check

## Rules

- **Enum values are pinned — never renumber.** Plans are stored as integers by consumers (a DB column, JSON with no
  string-enum converter). `PlanStatus`, `StepStatus`, `AgentTaskStatus`, `EscalationStatus`, `EscalationSource`,
  `EscalationType`, `ReflectionDecision` all carry explicit values; a test pins them. Add new members with new values.
- **"Could not check" is not "checked".** `ContentExpectationValidator` fails a step that declares
  `ExpectedContent` but names no files, or whose file cannot be read; a validator that throws is a failed check.
- **A worker missing from a split plan gets no steps**, never the whole plan (that would re-run every other worker's
  steps). An unsplit plan (no assignments at all) gives every worker every step.
- **No `Birko.MessageQueue` edge.** This project imports nothing from Birko; a queue-backed `ITaskDispatcher` is a
  52-line consumer class (DraCode's `QueueTaskDispatcher`), or a companion project if a second consumer needs it.

## Adopting from DraCode.KoboldLair (TASK-528 → DraCode TASK-126)

The numeric values were chosen to equal DraCode's, so **stored plans need no data migration**:

| DraCode | Birko | Stored int |
|---|---|---|
| `PlanStatus.Planning` | `PlanStatus.Created` | 0 |
| `Ready` / `InProgress` / `Completed` / `Failed` | same names | 1 / 2 / 3 / 4 |
| `StepStatus.Pending` / `InProgress` / `Completed` / `Skipped` / `Failed` | same names | 0 / 1 / 2 / 3 / 4 |

Member renames (JSON property names change, so map on load or keep `[JsonPropertyName]` on a derived type):

| DraCode | Birko |
|---|---|
| `ImplementationStep.LastErrorMessage` | `ErrorMessage` |
| the transient-retry block in `UpdatePlanStepTool` | `step.RetryOrFail(output, category, category == Transient)` |
| `AssignStepsToKobold(id, indices)` | `AssignSteps(workerId, indices)` |
| `EscalationAlert.KoboldId` (`Guid`) | `WorkerId` (`string`, the same key as `AssignedStepIndices`) |
| `plan.ValidateStepOrdering(analyzer)` / `plan.ReorderSteps(analyzer)` | `analyzer.ValidateOrdering(plan)` / `analyzer.Reorder(plan)` |
| `Models.Validation.ValidationResult` / `CombinedValidationResult` | `StepValidationResult` / `CombinedStepValidationResult` |
| `PlanFilename`, `Feature*`, `Specification*` | not in Birko — derive `KoboldImplementationPlan : ImplementationPlan` |

Behaviour that differs, on purpose:

- DraCode's `ValidateStepOrdering` reported a creator placed *before* its modifier as a violation (it used the
  symmetric `HasDependency`), with an empty file list.
- DraCode's own `StepDependencyAnalyzer.SuggestOptimalOrder` still has the `Reverse()` Birko removed at CR-H003, so
  it orders dependents first.
- `ReorderSteps` swallowed exceptions; `Reorder` refuses a started plan or one with assignments instead.
- `GetAssignedSteps` gave a worker missing from a split plan every step.
- `ContentExpectationValidator` passed a step it could not check.
- `ValidationAttempts` was counted once per validator; now once per validation.

## Dependencies
- **Microsoft.Extensions.Logging** — logging abstractions

## Consumers
- **DraCode.KoboldLair** — AI-powered development environment
- Consumer orchestration systems
