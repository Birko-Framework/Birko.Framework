# Birko.Workflow

## Overview
State machine engine for business process automation. Trigger-based transitions with fluent builder API, guards, actions, and diagram visualization.

## Project Location
`C:\Source\Birko.Workflow\` (shared project via `.projitems`)

## Components

### Core/ — Interfaces and types
- **WorkflowStatus.cs** — Enum: NotStarted, Active, Completed, Faulted
- **StateChangeRecord.cs** — Immutable record of a transition (FromState, ToState, Trigger, OccurredAt)
- **IWorkflowDefinition.cs** — Immutable workflow blueprint interface (Name, InitialState, States, Transitions, GetPermittedTriggers)
- **IWorkflowInstance.cs** — Read-only view of instance state (InstanceId, CurrentState, Status, Data, History)
- **IWorkflowEngine.cs** — Stateless engine interface (FireAsync, GetPermittedTriggers)
- **IWorkflowInstanceStore.cs** — Persistence contract for workflow instances (Save, Load, Delete, FindByState/Status/WorkflowName)

### Definition/ — Builder and immutable definitions
- **StateDefinition.cs** — State name, description, IsFinal, entry/exit actions
- **TransitionDefinition.cs** — From + Trigger + To + guards + actions
- **WorkflowDefinition.cs** — Immutable definition implementing IWorkflowDefinition
- **WorkflowBuilder.cs** — Fluent top-level builder (generic `<TData>`)
- **StateBuilder.cs** — Fluent state configuration (Description, IsFinal, OnEntry, OnExit)
- **TransitionBuilder.cs** — Fluent transition + guard + action configuration

### Execution/ — Engine and runtime
- **WorkflowEngine.cs** — Default engine: finds transition, evaluates guards, runs exit/transition/entry actions, updates state
- **WorkflowInstance.cs** — Mutable state holder with Create() and Restore() factories
- **TransitionResult.cs** — Success/Denied/NotFound outcome with denial reasons
- **WorkflowException.cs** — Exception hierarchy (WorkflowException, WorkflowCompletedException, WorkflowFaultedException, WorkflowActionException)

### Visualization/ — Diagram generation
- **IWorkflowDiagramGenerator.cs** — Interface for diagram output
- **MermaidDiagramGenerator.cs** — Mermaid stateDiagram-v2 output
- **DotDiagramGenerator.cs** — Graphviz DOT output

### Extensions/ — DI integration
- **WorkflowServiceCollectionExtensions.cs** — AddWorkflowEngine() with optional state change publishing

## Persistence Providers
- **Birko.Workflow.SQL** — SQL persistence via AsyncDataBaseBulkStore (any connector)
- **Birko.Workflow.ElasticSearch** — Elasticsearch persistence
- **Birko.Workflow.MongoDB** — MongoDB persistence
- **Birko.Workflow.RavenDB** — RavenDB persistence
- **Birko.Workflow.JSON** — JSON file-based (dev/testing)

All implement `IWorkflowInstanceStore<TData>`. Only instances are persisted (not definitions — definitions contain Func delegates).

## Dependencies
- None (core is dependency-free)
- Microsoft.Extensions.DependencyInjection.Abstractions (for DI extensions only)

## Key Patterns
- **Trigger-based**: `FireAsync("approve")` not `TransitionTo("Approved")`
- **Generic `WorkflowDefinition<TData>`**: Type-safe guards and actions on workflow data
- **States/triggers are strings**: Flexible, serializable. Enums via `.ToString()`
- **Engine is stateless**: `WorkflowInstance<TData>` holds mutable state
- **EventBus optional**: Engine publishes via `Action<StateChangeRecord>?` callback
- **Guards**: `Func<IWorkflowInstance<TData>, bool>` with reason string
- **Actions**: `Func<IWorkflowInstance<TData>, CancellationToken, Task>` for async

## Engine Flow (FireAsync)
1. Find TransitionDefinition matching (currentState, trigger)
2. If not found → return TransitionResult.NotFound
3. If instance is Completed/Faulted → throw exception
4. Evaluate all guards → if any fail, return TransitionResult.Denied(reasons)
5. Execute OnExit actions of current state
6. Execute transition actions
7. Set instance.CurrentState = toState
8. Execute OnEntry actions of new state
9. If toState.IsFinal → set Status = Completed
10. Append StateChangeRecord to history
11. Invoke OnStateChanged callback
12. Return TransitionResult.Success
13. If action throws → set Status = Faulted, wrap in WorkflowActionException

## Maintenance
- When adding new definition features, update WorkflowBuilder validation in Build()
- When adding new execution features, update WorkflowEngine.FireAsync flow
- Visualization generators should handle all definition features
