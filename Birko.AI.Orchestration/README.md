# Birko.AI.Orchestration

Multi-agent task orchestration — dispatch, planning, dependency analysis, escalation.

## Overview

Birko.AI.Orchestration provides infrastructure for coordinating multiple AI agents on complex tasks. It handles breaking work into implementation plans, analyzing step dependencies for parallel execution, dispatching tasks to agents, and escalating failures.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `AgentTaskRecord` | `Models` | Persistent record of a task assigned to an agent |
| `ImplementationPlan` | `Models` | High-level plan composed of ordered steps |
| `ImplementationStep` | `Models` | Single step within an implementation plan |
| `TaskAssignment` | `Models` | Assignment of a task to a specific agent type |
| `EscalationAlert` | `Models` | Alert for failed tasks or human intervention |
| `ITaskDispatcher` | `Dispatch` | Interface for dispatching tasks to agents |
| `DirectTaskDispatcher` | `Dispatch` | In-process task dispatcher implementation |
| `StepDependencyAnalyzer` | `Services` | Dependency analysis for parallel step execution |

## Dependencies

- **Microsoft.Extensions.Logging** — logging abstractions

## Usage

```xml
<Import Project="..\Birko.AI.Orchestration\Birko.AI.Orchestration.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Orchestration.Dispatch;

var dispatcher = new DirectTaskDispatcher(agentFactory, logger);
await dispatcher.DispatchAsync(taskAssignment);
```

## License

MIT License - see [License.md](License.md)
