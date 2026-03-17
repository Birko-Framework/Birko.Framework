# Workflow Engine

Birko.Workflow provides a trigger-based state machine engine for business process automation.

## Core Concepts

- **WorkflowDefinition** — Immutable blueprint: states, transitions, guards, actions
- **WorkflowInstance** — Mutable state holder: current state, status, history, data
- **WorkflowEngine** — Stateless executor: evaluates guards, runs actions, transitions state
- **Trigger** — Named event that causes a transition (e.g., `"approve"`, `"cancel"`)
- **Guard** — Condition that must be true for a transition to proceed
- **Action** — Async callback executed on state entry, exit, or during transition

## Defining a Workflow

```csharp
using Birko.Workflow.Definition;

var workflow = new WorkflowBuilder<ReservationData>("HotelReservation")
    .InitialState("Pending")
    .State("Pending").Description("Awaiting confirmation").And()
    .State("Confirmed")
        .OnEntry(async (inst, ct) => await SendConfirmationEmail(inst.Data))
        .And()
    .State("CheckedIn").And()
    .State("CheckedOut").IsFinal().And()
    .State("Cancelled").IsFinal().And()
    .Transition("confirm", "Pending", "Confirmed")
        .Guard(inst => inst.Data.PaymentReceived, "Payment required")
        .Action(async (inst, ct) => await ProcessPayment(inst.Data))
        .And()
    .Transition("checkIn", "Confirmed", "CheckedIn").And()
    .Transition("checkOut", "CheckedIn", "CheckedOut").And()
    .Transition("cancel", "Pending", "Cancelled").And()
    .Transition("cancel", "Confirmed", "Cancelled")
        .Action(async (inst, ct) => await RefundPayment(inst.Data))
        .And()
    .Build();
```

## Executing Transitions

```csharp
using Birko.Workflow.Execution;

var engine = new WorkflowEngine();
var instance = WorkflowInstance<ReservationData>.Create(workflow, data);

// Fire a trigger
var result = await engine.FireAsync(workflow, instance, "confirm");

if (result.IsSuccess)
{
    // Transitioned: result.FromState -> result.ToState
}
else if (result.IsDenied)
{
    // Guards failed: result.DenialReasons
}
else if (result.IsNotFound)
{
    // No transition for this trigger in current state
}
```

## Engine Flow

1. Find transition matching `(currentState, trigger)`
2. Evaluate all guards — collect denial reasons if any fail
3. Execute OnExit actions of the current state
4. Execute transition actions
5. Update `CurrentState` to the target state
6. Execute OnEntry actions of the new state
7. If the new state is final, set `Status = Completed`
8. Append `StateChangeRecord` to history
9. Invoke optional state change callback

If any action throws, the instance is set to `Faulted` and a `WorkflowActionException` is thrown.

## Guards

Guards are predicates with a reason string. All guards must pass for the transition to proceed.

```csharp
.Transition("approve", "Review", "Approved")
    .Guard(inst => inst.Data.Score >= 70, "Score must be at least 70")
    .Guard(inst => inst.Data.ReviewerApproved, "Reviewer approval required")
    .And()
```

If multiple guards fail, all failure reasons are returned in `TransitionResult.DenialReasons`.

## Actions

Actions are async callbacks with cancellation support:

```csharp
// State entry/exit actions
.State("Approved")
    .OnEntry(async (inst, ct) => await NotifyApplicant(inst.Data))
    .OnExit(async (inst, ct) => await LogStateExit(inst.Data))
    .And()

// Transition actions
.Transition("approve", "Review", "Approved")
    .Action(async (inst, ct) => await RecordApprovalTime(inst.Data))
    .And()
```

Execution order: OnExit (from) → Transition actions → state update → OnEntry (to).

## Workflow Status

| Status | Description |
|--------|-------------|
| `NotStarted` | Instance created but not yet used |
| `Active` | In progress, accepting triggers |
| `Completed` | Reached a final state |
| `Faulted` | An action threw an exception |

## Persistence (Restore)

The engine is stateless — `WorkflowInstance` holds all mutable state. To persist and restore:

```csharp
// Save: serialize instance.InstanceId, instance.CurrentState, instance.Status, instance.Data, instance.History

// Restore:
var instance = WorkflowInstance<MyData>.Restore(
    instanceId: savedId,
    currentState: "Confirmed",
    status: WorkflowStatus.Active,
    data: loadedData,
    history: savedHistory);

// Continue
var result = await engine.FireAsync(workflow, instance, "checkIn");
```

## Visualization

### Mermaid

```csharp
var mermaid = new MermaidDiagramGenerator();
var diagram = mermaid.Generate(workflow);
// stateDiagram-v2
//     [*] --> Pending
//     Pending --> Confirmed : confirm
//     ...
```

### Graphviz DOT

```csharp
var dot = new DotDiagramGenerator();
var diagram = dot.Generate(workflow);
// digraph "HotelReservation" { ... }
```

## Dependency Injection

```csharp
// Basic registration
services.AddWorkflowEngine();

// With state change publishing
services.AddWorkflowEngine(options =>
{
    options.PublishStateChanges = true;
});
```

Registers `IWorkflowEngine` (singleton) and `IWorkflowDiagramGenerator` (MermaidDiagramGenerator).

## Querying Permitted Triggers

```csharp
var triggers = engine.GetPermittedTriggers(workflow, instance);
// Returns triggers available from the current state
// Empty for Completed/Faulted instances
```

## Exception Hierarchy

| Exception | When |
|-----------|------|
| `WorkflowCompletedException` | FireAsync on a Completed instance |
| `WorkflowFaultedException` | FireAsync on a Faulted instance |
| `WorkflowActionException` | An entry/exit/transition action threw (wraps inner exception) |

## Future: Persistence Projects

- **Birko.Workflow.SQL** — SQL-based workflow instance persistence
- **Birko.Workflow.MongoDB** — MongoDB-based workflow instance persistence
