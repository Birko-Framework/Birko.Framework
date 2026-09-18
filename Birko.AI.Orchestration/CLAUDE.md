# Birko.AI.Orchestration

## Overview
Multi-agent task orchestration — dispatch, planning, dependency analysis, escalation.

## Project Location
`C:\Source\Birko.AI.Orchestration\`

## Namespace
`Birko.AI.Orchestration.Models`, `Birko.AI.Orchestration.Dispatch`, `Birko.AI.Orchestration.Services`

## Components

### Models/AgentTaskRecord.cs
- `AgentTaskRecord` — Persistent record of a task assigned to an agent

### Models/ImplementationPlan.cs
- `ImplementationPlan` — High-level plan composed of ordered steps

### Models/ImplementationStep.cs
- `ImplementationStep` — Single step within an implementation plan

### Models/TaskAssignment.cs
- `TaskAssignment` — Assignment of a task to a specific agent type

### Models/EscalationAlert.cs
- `EscalationAlert` — Alert raised when a task fails or requires human intervention

### Dispatch/ITaskDispatcher.cs
- `ITaskDispatcher` — Interface for dispatching tasks to agents

### Dispatch/DirectTaskDispatcher.cs
- `DirectTaskDispatcher` — In-process task dispatcher implementation

### Services/StepDependencyAnalyzer.cs
- `StepDependencyAnalyzer` — Analyzes dependencies between plan steps for parallel execution

## Dependencies
- **Microsoft.Extensions.Logging** — logging abstractions

## Consumers
- **DraCode.KoboldLair** — AI-powered development environment
- Consumer orchestration systems
