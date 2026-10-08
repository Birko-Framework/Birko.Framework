---
id: TASK-528
parent: STORY-060
feature: null
status: todo
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

- [ ] Plan / step lifecycle, retry bookkeeping, metrics and current-step navigation on Birko's model
- [ ] Step validators (files created / modified, expected content present) ship and drive `ExpectedContent`
- [ ] The validator result type does not shadow `Birko.Validation.ValidationResult` (DraCode's does)
- [ ] `EscalationAlert` carries a worker id
- [ ] Enum encoding pinned (explicit numeric values or string serialization) and documented; the DraCode → Birko value mapping written down for the adoption
- [ ] Optional: DraCode's `MessageQueue/QueueTaskDispatcher.cs` (52 lines, `ITaskDispatcher` over `Birko.MessageQueue`) — in `Birko.AI.Orchestration` only if CLAUDE-projects.md § Dependency Flow allows the edge, otherwise a small companion
- [ ] Tests: lifecycle transitions, reorder and ordering validation, each validator

## Out of scope

- DraCode's `ReflectionTool` — it keeps the current plan and run id in `static` fields (rule 36 shape; parallel Kobolds
  overwrite each other). Fixed DraCode-side in its TASK-120; backport later if at all

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
