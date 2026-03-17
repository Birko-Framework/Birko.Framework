# Birko.Workflow

State machine engine for business process automation in the Birko Framework. Provides trigger-based transitions, fluent builder API, guards, actions, and diagram visualization.

## Features

- **Fluent Builder API** — Define workflows with a readable, chainable syntax
- **Trigger-based Transitions** — Fire named triggers (`"approve"`, `"cancel"`) rather than direct state changes
- **Generic `WorkflowDefinition<TData>`** — Type-safe guards and actions on your workflow data
- **Guards** — Conditional transitions with denial reasons
- **Actions** — Async entry/exit/transition actions with cancellation support
- **State History** — Full transition audit trail via `StateChangeRecord`
- **Visualization** — Generate Mermaid and Graphviz DOT diagrams from definitions
- **DI Integration** — `AddWorkflowEngine()` for Microsoft.Extensions.DependencyInjection
- **Persistence** — `IWorkflowInstanceStore<TData>` with SQL, ElasticSearch, MongoDB, RavenDB, and JSON providers
- **Fault handling** — Automatic faulted state on action failures

## Quick Start

### Define a Workflow

```csharp
var workflow = new WorkflowBuilder<OrderData>("OrderProcessing")
    .InitialState("Pending")
    .State("Pending").Description("Awaiting payment").And()
    .State("Paid").OnEntry(async (inst, ct) => { /* send confirmation */ }).And()
    .State("Shipped").And()
    .State("Delivered").IsFinal().And()
    .State("Cancelled").IsFinal().And()
    .Transition("pay", "Pending", "Paid")
        .Guard(inst => inst.Data.PaymentReceived, "Payment required")
        .And()
    .Transition("ship", "Paid", "Shipped").And()
    .Transition("deliver", "Shipped", "Delivered").And()
    .Transition("cancel", "Pending", "Cancelled").And()
    .Transition("cancel", "Paid", "Cancelled").And()
    .Build();
```

### Execute Transitions

```csharp
var engine = new WorkflowEngine();
var instance = WorkflowInstance<OrderData>.Create(workflow, orderData);

var result = await engine.FireAsync(workflow, instance, "pay");

if (result.IsSuccess)
    Console.WriteLine($"Moved from {result.FromState} to {result.ToState}");
else if (result.IsDenied)
    Console.WriteLine($"Denied: {string.Join(", ", result.DenialReasons)}");
else if (result.IsNotFound)
    Console.WriteLine($"No transition for trigger '{result.Trigger}' in state '{result.FromState}'");
```

### Query Permitted Triggers

```csharp
var triggers = engine.GetPermittedTriggers(workflow, instance);
// ["pay", "cancel"] when in "Pending" state
```

### Generate Diagrams

```csharp
var mermaid = new MermaidDiagramGenerator();
Console.WriteLine(mermaid.Generate(workflow));
// stateDiagram-v2
//     [*] --> Pending
//     Pending : Awaiting payment
//     Pending --> Paid : pay
//     ...

var dot = new DotDiagramGenerator();
Console.WriteLine(dot.Generate(workflow));
// digraph "OrderProcessing" { ... }
```

### Dependency Injection

```csharp
services.AddWorkflowEngine();

// With state change publishing
services.AddWorkflowEngine(options =>
{
    options.PublishStateChanges = true;
});
```

### Restore Instance (for persistence)

```csharp
var restored = WorkflowInstance<OrderData>.Restore(
    instanceId: savedId,
    currentState: "Paid",
    status: WorkflowStatus.Active,
    data: loadedData,
    history: savedHistory);
```

## Architecture

### Engine Flow

1. Find matching transition (currentState + trigger)
2. Evaluate guards — collect denial reasons
3. Execute OnExit actions → transition actions → set state → OnEntry actions
4. Mark as Completed if final state reached
5. Record history and invoke state change callback
6. On action failure: set Faulted status, throw `WorkflowActionException`

### Key Types

| Type | Purpose |
|------|---------|
| `WorkflowBuilder<TData>` | Fluent API to define workflows |
| `WorkflowDefinition<TData>` | Immutable workflow blueprint |
| `WorkflowInstance<TData>` | Mutable state holder |
| `WorkflowEngine` | Stateless transition executor |
| `TransitionResult` | Success/Denied/NotFound outcome |
| `MermaidDiagramGenerator` | Mermaid stateDiagram-v2 output |
| `DotDiagramGenerator` | Graphviz DOT output |

## License

MIT License - see [License.md](License.md)
