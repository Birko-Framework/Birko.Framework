# Birko.AI.Orchestration

Multi-agent task orchestration — dispatch, planning, dependency analysis, escalation.

## Overview

Birko.AI.Orchestration provides infrastructure for coordinating multiple AI agents on complex tasks. It handles breaking work into implementation plans, analyzing step dependencies for parallel execution, dispatching tasks to agents, and escalating failures.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `AgentTaskRecord` | `Models` | Persistent record of a task assigned to an agent |
| `ImplementationPlan` | `Models` | Ordered steps + execution state: current step, per-worker assignment, log, metrics, lessons |
| `ImplementationStep` | `Models` | One step: `Start` / `Complete` / `Fail` / `Skip`, `RetryOrFail`, metrics |
| `TaskAssignment` | `Models` | Assignment of a task to a specific agent type |
| `EscalationAlert` | `Models` | Alert for failed tasks or human intervention |
| `ITaskDispatcher` | `Dispatch` | Interface for dispatching tasks to agents |
| `DirectTaskDispatcher` | `Dispatch` | In-process task dispatcher implementation |
| `StepDependencyAnalyzer` | `Services` | Parallel groups, topological order, `ValidateOrdering`, `Reorder` |
| `StepValidationService` | `Validation` | Runs `IStepValidator`s: files created, files modified, `ExpectedContent` present |

## Dependencies

- **Microsoft.Extensions.Logging** — logging abstractions

## Usage

```xml
<Import Project="..\Birko.AI.Orchestration\Birko.AI.Orchestration.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Orchestration.Dispatch;

var dispatcher = new DirectTaskDispatcher((assignment, ct) => RunAgentAsync(assignment, ct), logger);
await dispatcher.DispatchTaskAsync(taskAssignment);
```

```csharp
using Birko.AI.Orchestration.Validation;

step.Start();
// ... the agent works ...
var check = await new StepValidationService().ValidateStepAsync(step, workspacePath);
if (check.Success) step.Complete();
else if (step.RetryOrFail(string.Join("; ", check.AllIssues), "Validation", retryable: true)) { /* run it again */ }
```

Status enums have **pinned numeric values**, so they are safe to store as integers — see [CLAUDE.md](CLAUDE.md).

## License

MIT License - see [License.md](License.md)
