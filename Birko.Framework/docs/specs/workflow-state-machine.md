---
area: workflow-state-machine
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Workflow.CosmosDB/CosmosDBWorkflowInstanceSchema.cs
  - ../Birko.Workflow.CosmosDB/CosmosDBWorkflowInstanceStore.cs
  - ../Birko.Workflow.CosmosDB/Models/CosmosWorkflowInstanceModel.cs
  - ../Birko.Workflow.ElasticSearch/ElasticSearchWorkflowInstanceSchema.cs
  - ../Birko.Workflow.ElasticSearch/ElasticSearchWorkflowInstanceStore.cs
  - ../Birko.Workflow.ElasticSearch/Models/ElasticWorkflowInstanceModel.cs
  - ../Birko.Workflow.JSON/JsonWorkflowInstanceSchema.cs
  - ../Birko.Workflow.JSON/JsonWorkflowInstanceStore.cs
  - ../Birko.Workflow.JSON/Models/JsonWorkflowInstanceModel.cs
  - ../Birko.Workflow.MongoDB/Models/MongoWorkflowInstanceModel.cs
  - ../Birko.Workflow.MongoDB/MongoDBWorkflowInstanceSchema.cs
  - ../Birko.Workflow.MongoDB/MongoDBWorkflowInstanceStore.cs
  - ../Birko.Workflow.RavenDB/Models/RavenWorkflowInstanceModel.cs
  - ../Birko.Workflow.RavenDB/RavenDBWorkflowInstanceSchema.cs
  - ../Birko.Workflow.RavenDB/RavenDBWorkflowInstanceStore.cs
  - ../Birko.Workflow.SQL/Models/WorkflowInstanceModel.cs
  - ../Birko.Workflow.SQL/SqlWorkflowInstanceSchema.cs
  - ../Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs
  - ../Birko.Workflow.XML/Models/XmlWorkflowInstanceModel.cs
  - ../Birko.Workflow.XML/XmlWorkflowInstanceSchema.cs
  - ../Birko.Workflow.XML/XmlWorkflowInstanceStore.cs
  - ../Birko.Workflow/Core/IWorkflowDefinition.cs
  - ../Birko.Workflow/Core/IWorkflowEngine.cs
  - ../Birko.Workflow/Core/IWorkflowInstance.cs
  - ../Birko.Workflow/Core/IWorkflowInstanceStore.cs
  - ../Birko.Workflow/Core/StateChangeRecord.cs
  - ../Birko.Workflow/Core/WorkflowStatus.cs
  - ../Birko.Workflow/Definition/StateBuilder.cs
  - ../Birko.Workflow/Definition/StateDefinition.cs
  - ../Birko.Workflow/Definition/TransitionBuilder.cs
  - ../Birko.Workflow/Definition/TransitionDefinition.cs
  - ../Birko.Workflow/Definition/WorkflowBuilder.cs
  - ../Birko.Workflow/Definition/WorkflowDefinition.cs
  - ../Birko.Workflow/Execution/TransitionResult.cs
  - ../Birko.Workflow/Execution/WorkflowEngine.cs
  - ../Birko.Workflow/Execution/WorkflowException.cs
  - ../Birko.Workflow/Execution/WorkflowInstance.cs
  - ../Birko.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs
  - ../Birko.Workflow/Visualization/DotDiagramGenerator.cs
  - ../Birko.Workflow/Visualization/IWorkflowDiagramGenerator.cs
  - ../Birko.Workflow/Visualization/MermaidDiagramGenerator.cs
source-commits:   # sibling HEADs when this spec was last written (2026-07-30 16:07:38,
                  # commit acbbe9d). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Workflow: d8fc1ea
  ../Birko.Workflow.CosmosDB: f5c4f6a
  ../Birko.Workflow.ElasticSearch: 6c0f5e3
  ../Birko.Workflow.JSON: a77cc6c
  ../Birko.Workflow.MongoDB: 6cf4a45
  ../Birko.Workflow.RavenDB: 040f2c2
  ../Birko.Workflow.SQL: c279867
  ../Birko.Workflow.XML: d357d38
shaped-by: []
shaped-by-derived: true
shaped-by-unresolved: 80
---

# Workflow state machine, guards, actions and persistence

## Purpose

This capability lets an application describe a finite state machine in code — named states with
entry/exit actions, and named triggers that move an instance from one state to another subject to
guard predicates — and then drive concrete instances of that machine over a typed payload
(`TData`). A definition is built once with a fluent builder (`WorkflowBuilder<TData>`) and validated
at `Build()` time; instances (`WorkflowInstance<TData>`) carry the current state, a status, the
payload, and an append-only history of state changes. `WorkflowEngine` is the only component that
mutates an instance: it resolves the transition for a trigger, evaluates guards, runs the
exit/transition/entry actions in a fixed order, records history, and reports the outcome as a
`TransitionResult` (success / denied / not-found) or throws for the illegal cases.

Because definitions hold `Func` delegates they are never persisted; only instances are.
`IWorkflowInstanceStore<TData>` is the persistence seam, implemented seven times — SQL, JSON, XML,
ElasticSearch, MongoDB, RavenDB and CosmosDB — each of which serializes the payload and the history
to strings and stores them alongside the current state and status. The backends are deliberately
uniform but not identical: their upsert race characteristics differ, and CosmosDB is the only one
that scopes its "find" queries to a workflow name. Two diagram generators (Mermaid, Graphviz DOT)
render a definition for documentation.

Consumers are application services that need approval chains, order/document lifecycles or any
state-gated process, plus the DI extension `AddWorkflowEngine` that registers the engine and the
default diagram generator.

## Requirements

### Requirement: Workflow name is validated at builder construction

The system SHALL reject a null, empty or whitespace-only workflow name when `WorkflowBuilder<TData>`
is constructed, by throwing `ArgumentException` with parameter name `name`.

#### Scenario: Whitespace workflow name

- **Given** an application constructing a workflow definition
- **When** `new WorkflowBuilder<OrderData>("   ")` is called
- **Then** an `ArgumentException` is thrown with message "Workflow name cannot be null or empty." and `ParamName` `"name"`

#### Scenario: Valid workflow name

- **Given** an application constructing a workflow definition
- **When** `new WorkflowBuilder<OrderData>("OrderApproval")` is called
- **Then** the builder is created and the eventual `WorkflowDefinition<TData>.Name` is `"OrderApproval"`

### Requirement: State and transition names are validated when declared

The system SHALL reject a null, empty or whitespace-only state name in `State(name)` and reject a
null, empty or whitespace-only `trigger`, `fromState` or `toState` in
`Transition(trigger, fromState, toState)`, throwing `ArgumentException` naming the offending
parameter.

#### Scenario: Empty state name

- **Given** a `WorkflowBuilder<TData>`
- **When** `State("")` is called
- **Then** an `ArgumentException` is thrown with message "State name cannot be null or empty." and `ParamName` `"name"`

#### Scenario: Empty trigger

- **Given** a `WorkflowBuilder<TData>`
- **When** `Transition("", "Draft", "Submitted")` is called
- **Then** an `ArgumentException` is thrown with message "Trigger cannot be null or empty." and `ParamName` `"trigger"`

#### Scenario: Empty destination state

- **Given** a `WorkflowBuilder<TData>`
- **When** `Transition("submit", "Draft", " ")` is called
- **Then** an `ArgumentException` is thrown with message "ToState cannot be null or empty." and `ParamName` `"toState"`

### Requirement: Build validates the initial state

The system SHALL, in `WorkflowBuilder<TData>.Build()`, throw `InvalidOperationException` when
`InitialState` was never set, when the configured initial state is not among the declared state
names, or when the state it names was declared `IsFinal()`.

#### Scenario: InitialState never set

- **Given** a builder with states declared but no `InitialState(...)` call
- **When** `Build()` is called
- **Then** an `InvalidOperationException` with message "InitialState must be set before building." is thrown

#### Scenario: InitialState names an undeclared state

- **Given** a builder with states `Draft` and `Approved` and `InitialState("New")`
- **When** `Build()` is called
- **Then** an `InvalidOperationException` with message "InitialState 'New' is not defined as a state." is thrown

#### Scenario: InitialState is a final state

- **Given** a builder where `State("Closed").IsFinal()` and `InitialState("Closed")`
- **When** `Build()` is called
- **Then** an `InvalidOperationException` with message "InitialState 'Closed' cannot be a final state." is thrown

### Requirement: Build validates that every transition endpoint is a declared state

The system SHALL, in `Build()`, throw `InvalidOperationException` for the first transition whose
`FromState` or `ToState` is not among the declared state names, naming the trigger and the offending
state.

#### Scenario: Transition to an undeclared state

- **Given** a builder with states `Draft` and `Submitted` and `Transition("approve", "Submitted", "Approved")`
- **When** `Build()` is called
- **Then** an `InvalidOperationException` with message "Transition trigger 'approve' references undefined ToState 'Approved'." is thrown

#### Scenario: Transition from an undeclared state

- **Given** a builder with states `Draft` and `Submitted` and `Transition("reopen", "Archived", "Draft")`
- **When** `Build()` is called
- **Then** an `InvalidOperationException` with message "Transition trigger 'reopen' references undefined FromState 'Archived'." is thrown

### Requirement: Build performs no other structural validation

The system SHALL NOT reject duplicate state names, duplicate `(trigger, fromState)` pairs,
unreachable states, states with no outgoing transitions, or the absence of any final state; such
definitions SHALL build successfully.

#### Scenario: Duplicate state declarations

- **Given** a builder where `State("Draft")` is called twice, the second carrying an `OnEntry` action
- **When** `Build()` is called
- **Then** the definition builds and `States` contains two `StateDefinition<TData>` entries both named `Draft`, of which only the first is ever selected by the engine's `FirstOrDefault(s => s.Name == ...)` lookups

#### Scenario: Two transitions sharing trigger and source state

- **Given** `Transition("submit", "Draft", "Fast")` with a guard and `Transition("submit", "Draft", "Slow")` with no guard
- **When** `Build()` is called
- **Then** the definition builds with both transitions present in `Transitions` in declaration order

#### Scenario: No final state at all

- **Given** a builder whose states are all non-final
- **When** `Build()` is called
- **Then** the definition builds and no instance of it can ever reach `WorkflowStatus.Completed`

### Requirement: Fluent builders accumulate configuration in declaration order

The system SHALL collect multiple `OnEntry` / `OnExit` actions per state and multiple `Guard` /
`Action` entries per transition in the order they were declared, expose `And()` returning the parent
`WorkflowBuilder<TData>` from both `StateBuilder<TData>` and `TransitionBuilder<TData>`, and default
a guard's denial reason to `"Guard failed"` when none is supplied.

#### Scenario: Multiple entry actions run in declaration order

- **Given** `State("Approved").OnEntry(a).OnEntry(b)`
- **When** the definition is built
- **Then** `StateDefinition<TData>.OnEntryActions` contains `a` then `b`, and the engine awaits them in that order

#### Scenario: Guard reason defaults

- **Given** `Transition("approve", "Submitted", "Approved").Guard(i => i.Data.Amount < 1000)`
- **When** the guard predicate returns false during a fire
- **Then** the resulting `TransitionResult.DenialReasons` contains the single entry `"Guard failed"`

#### Scenario: Chaining back to the parent builder

- **Given** a `StateBuilder<TData>` or `TransitionBuilder<TData>`
- **When** `And()` is called
- **Then** the originating `WorkflowBuilder<TData>` instance is returned so further states/transitions can be declared

### Requirement: Definition-level permitted triggers are state-only and de-duplicated

The system SHALL, in `WorkflowDefinition<TData>.GetPermittedTriggers(state)`, return the distinct
triggers of all transitions whose `FromState` equals the given state, without evaluating any guard
and without consulting any instance.

#### Scenario: Duplicate triggers collapse

- **Given** two transitions both triggered by `"submit"` from state `Draft`
- **When** `GetPermittedTriggers("Draft")` is called
- **Then** the result contains `"submit"` exactly once

#### Scenario: Guards are ignored at definition level

- **Given** a `"submit"` transition from `Draft` whose guard would fail for every instance
- **When** `GetPermittedTriggers("Draft")` is called
- **Then** `"submit"` is still returned

#### Scenario: State with no outgoing transitions

- **Given** a definition where no transition has `FromState == "Closed"`
- **When** `GetPermittedTriggers("Closed")` is called
- **Then** an empty list is returned

### Requirement: Instances are created in the definition's initial state and Active

The system SHALL, in `WorkflowInstance<TData>.Create(definition, data)`, mint a new `Guid` as
`InstanceId`, set `CurrentState` to `definition.InitialState`, set `Status` to
`WorkflowStatus.Active`, keep the supplied `data` reference as `Data`, and start with an empty
`History`.

#### Scenario: Freshly created instance

- **Given** a definition whose `InitialState` is `"Draft"`
- **When** `WorkflowInstance<OrderData>.Create(definition, data)` is called
- **Then** `CurrentState == "Draft"`, `Status == WorkflowStatus.Active`, `History` is empty, `Data` is reference-equal to `data`, and `InstanceId != Guid.Empty`

#### Scenario: Two creates never share an id

- **Given** the same definition and payload
- **When** `Create` is called twice
- **Then** the two instances have different `InstanceId` values

### Requirement: Instances can be restored with an explicit state, status and history

The system SHALL, in `WorkflowInstance<TData>.Restore(instanceId, currentState, status, data, history)`,
reconstruct an instance verbatim from the supplied values, treating a null `history` as an empty
history, and SHALL expose `History` as an `IReadOnlyList<StateChangeRecord>` typed over the
internally appended `List<StateChangeRecord>`, which it returns directly rather than as a copy or
wrapper.

#### Scenario: Restore with history

- **Given** a persisted state `"Submitted"`, status `Active` and two `StateChangeRecord` entries
- **When** `Restore` is called with them
- **Then** `CurrentState == "Submitted"`, `Status == WorkflowStatus.Active` and `History` contains the two records in the supplied order

#### Scenario: Restore without history

- **Given** `history` passed as `null`
- **When** `Restore` is called
- **Then** `History` is an empty list rather than null

#### Scenario: Restore accepts a state the definition does not contain

- **Given** a persisted `CurrentState` of `"Retired"` that no longer exists in the current definition
- **When** `Restore` is called and a trigger is later fired
- **Then** `Restore` succeeds, and the engine finds no matching transition and returns `TransitionResult.NotFound("Retired", trigger)`

### Requirement: Firing observes cancellation before doing any work

The system SHALL call `cancellationToken.ThrowIfCancellationRequested()` as the first statement of
`WorkflowEngine.FireAsync`, before type-checking the instance, before status checks and before any
guard or action runs.

#### Scenario: Already-cancelled token on a guard-only transition

- **Given** an active instance and a transition with guards but no actions
- **When** `FireAsync(definition, instance, "submit", new CancellationToken(true))` is awaited
- **Then** an `OperationCanceledException` is thrown, the instance's `CurrentState` and `Status` are unchanged, and no history record is appended

### Requirement: Firing requires the concrete WorkflowInstance type

The system SHALL throw `ArgumentException` (parameter name `instance`) when the `IWorkflowInstance<TData>`
passed to `FireAsync` is not a `WorkflowInstance<TData>`, because the engine mutates the instance
through internal setters.

#### Scenario: Foreign IWorkflowInstance implementation

- **Given** a caller-supplied class implementing `IWorkflowInstance<TData>` that is not `WorkflowInstance<TData>`
- **When** `FireAsync` is awaited with it
- **Then** an `ArgumentException` with message "Instance must be created via WorkflowInstance<TData>.Create()." and `ParamName` `"instance"` is thrown

### Requirement: Completed and faulted instances reject all triggers

The system SHALL throw `WorkflowCompletedException` when `instance.Status` is
`WorkflowStatus.Completed` and `WorkflowFaultedException` when it is `WorkflowStatus.Faulted`, both
carrying the definition's `Name` and the instance's `InstanceId`, and SHALL accept triggers for both
`Active` and `NotStarted`.

#### Scenario: Trigger on a completed instance

- **Given** an instance whose `Status` is `Completed`
- **When** `FireAsync(definition, instance, "reopen")` is awaited
- **Then** a `WorkflowCompletedException` is thrown with message "Workflow '{name}' instance '{id}' is already completed and cannot accept new triggers." and `WorkflowName`/`InstanceId` populated

#### Scenario: Trigger on a faulted instance

- **Given** an instance whose `Status` is `Faulted`
- **When** `FireAsync` is awaited with any trigger
- **Then** a `WorkflowFaultedException` is thrown with message "Workflow '{name}' instance '{id}' is faulted and cannot accept new triggers."

#### Scenario: Trigger on a NotStarted instance

- **Given** an instance restored with `Status == WorkflowStatus.NotStarted`
- **When** a valid trigger is fired and succeeds
- **Then** the transition is applied, history is appended, and `Status` remains `NotStarted` — the engine only ever assigns `Completed` or `Faulted`

### Requirement: An unmatched trigger is a result, not an exception

The system SHALL return `TransitionResult.NotFound(instance.CurrentState, trigger)` when no
transition in the definition has both `FromState == instance.CurrentState` and
`Trigger == trigger`, leaving the instance untouched.

#### Scenario: Trigger not defined for the current state

- **Given** an active instance in state `Draft` and a definition where `"approve"` only leaves `Submitted`
- **When** `FireAsync(definition, instance, "approve")` is awaited
- **Then** the result has `IsNotFound == true`, `IsSuccess == false`, `FromState == "Draft"`, `ToState == null`, `Trigger == "approve"`, `DenialReasons` empty, and the instance's state, status and history are unchanged

### Requirement: Only the first matching transition per trigger is ever considered

The system SHALL select the transition via `FirstOrDefault` over `Transitions` and SHALL NOT fall
through to a later transition with the same `FromState` and `Trigger` when the selected one's guards
deny; guard-based branching between alternative destinations is therefore not supported.

#### Scenario: Second alternative is unreachable

- **Given** `Transition("submit", "Draft", "Fast").Guard(i => i.Data.Amount < 100)` declared before `Transition("submit", "Draft", "Slow")` with no guard
- **When** `"submit"` is fired on an instance whose `Amount` is 500
- **Then** the result is `Denied` with the first transition's guard reason — the unguarded `Slow` transition is never attempted

#### Scenario: Engine-level permitted triggers agree with that selection

- **Given** the same definition and instance
- **When** `GetPermittedTriggers(definition, instance)` is called
- **Then** `"submit"` is absent, because the grouping evaluates only `g.First()`'s guards

### Requirement: All failing guards are reported and nothing is mutated

The system SHALL evaluate every guard of the selected transition (no short-circuit), and when one or
more predicates return false SHALL return `TransitionResult.Denied` carrying every failing guard's
reason in declaration order, without running any exit, transition or entry action and without
changing state, status or history.

#### Scenario: Two guards fail

- **Given** a transition with guards `(p1,"needs approver")` and `(p2,"needs budget")`, both returning false
- **When** the trigger is fired
- **Then** the result has `IsDenied == true`, `DenialReasons == ["needs approver", "needs budget"]`, `ToState == null`, and no action delegate was invoked

#### Scenario: All guards pass

- **Given** a transition whose every guard predicate returns true
- **When** the trigger is fired
- **Then** the transition proceeds to its action phases

### Requirement: A successful transition runs actions in a fixed order and records history last

The system SHALL, on a permitted transition, await the source state's `OnExitActions`, then the
transition's `Actions`, then assign `CurrentState = transition.ToState`, then await the destination
state's `OnEntryActions`, then set `Status = Completed` if the destination is `IsFinal`, then append
a `StateChangeRecord(fromState, toState, trigger, DateTime.UtcNow)` to `History`, then invoke the
optional state-changed callback, and finally return `TransitionResult.Success`.

#### Scenario: Ordinary transition

- **Given** an active instance in `Draft`, a `"submit"` transition to `Submitted`, an exit action on `Draft`, a transition action, and an entry action on `Submitted`
- **When** the trigger is fired
- **Then** the three actions ran in the order exit → transition → entry, `CurrentState == "Submitted"`, `Status` is still `Active`, `History` has one record `("Draft","Submitted","submit", <UTC now>)`, and the result is `Success` with `FromState`/`ToState`/`Trigger` populated

#### Scenario: Entry actions observe the new state

- **Given** an `OnEntry` action on the destination state that reads `instance.CurrentState`
- **When** the transition runs
- **Then** the action observes the destination state name, because `CurrentState` is assigned before the entry actions are awaited

#### Scenario: Reaching a final state completes the instance

- **Given** a transition whose destination state was declared `IsFinal()`
- **When** the trigger is fired successfully
- **Then** `Status == WorkflowStatus.Completed`, the history record is still appended, and any subsequent `FireAsync` throws `WorkflowCompletedException`

#### Scenario: Missing state definitions are tolerated

- **Given** a custom `IWorkflowDefinition<TData>` whose `States` does not contain the transition's `FromState` or `ToState`
- **When** the trigger is fired
- **Then** the corresponding exit/entry action loops are skipped (`FirstOrDefault` returned null), the state change still applies, and no exception is raised for the missing definitions

#### Scenario: Cancellation token is propagated to every action

- **Given** exit, transition and entry actions that accept the token
- **When** `FireAsync` is awaited with a token
- **Then** the same `CancellationToken` instance is passed to each action invocation

### Requirement: An action failure faults the instance and is wrapped

The system SHALL catch any exception thrown by an exit, transition or entry action that is not a
`WorkflowException`, reset `CurrentState` to the source state, set `Status` to
`WorkflowStatus.Faulted`, and throw `WorkflowActionException` carrying the workflow name, instance
id, the phase state (source state for exit/transition actions, destination state once entry actions
are running), the trigger and the original exception as `InnerException`; no history record SHALL be
appended.

#### Scenario: Transition action throws

- **Given** an active instance in `Draft` and a `"submit"` transition to `Submitted` whose action throws `InvalidOperationException`
- **When** the trigger is fired
- **Then** a `WorkflowActionException` is thrown with `State == "Draft"`, `Trigger == "submit"`, message "Action failed during transition 'submit' in state 'Draft' of workflow '{name}'.", `InnerException` the original exception; `CurrentState == "Draft"`, `Status == Faulted`, `History` empty

#### Scenario: Entry action throws — state is rolled back

- **Given** the destination state's `OnEntry` action throws
- **When** the trigger is fired
- **Then** `WorkflowActionException.State` is the destination state name, but `instance.CurrentState` is rolled back to the source state and `Status` is `Faulted`, so a faulted instance never reports a state its history does not contain

#### Scenario: Cancellation during an action is converted into a fault

- **Given** an action that observes the token and throws `OperationCanceledException`
- **When** the trigger is fired
- **Then** the exception is caught by the same handler: the instance is marked `Faulted` and a `WorkflowActionException` wrapping the `OperationCanceledException` is thrown instead of the cancellation propagating

#### Scenario: A WorkflowException from an action escapes unhandled

- **Given** an `OnEntry` action that throws a `WorkflowException` (or a subclass)
- **When** the trigger is fired
- **Then** the exception filter `when (ex is not WorkflowException)` does not match, so it propagates as-is with `CurrentState` left advanced to the destination state, `Status` still `Active` and no history record appended

### Requirement: The state-changed callback is invoked synchronously inside the transition

The system SHALL invoke the optional `Action<StateChangeRecord, string, Guid>` supplied to the
`WorkflowEngine` constructor once per successful transition, after the history record is appended,
passing the record, the definition's `Name` and the instance's `InstanceId`; the invocation happens
inside the engine's try block, so an exception from the callback is treated as an action failure.

#### Scenario: Callback receives the record

- **Given** a `WorkflowEngine` constructed with a callback
- **When** a transition from `Draft` to `Submitted` via `"submit"` succeeds
- **Then** the callback is invoked once with that `StateChangeRecord`, the workflow name and the instance id

#### Scenario: No callback configured

- **Given** a `WorkflowEngine()` constructed with no callback
- **When** a transition succeeds
- **Then** no notification occurs and the transition result is unaffected

#### Scenario: Callback throws after a completed transition

- **Given** a callback that throws `InvalidOperationException`
- **When** a transition succeeds and the callback is invoked
- **Then** the engine's catch handler runs: `CurrentState` is rolled back to the source state, `Status` becomes `Faulted`, and `WorkflowActionException` is thrown — even though the transition itself completed and its history record has already been appended, leaving history and `CurrentState` inconsistent

### Requirement: Engine-level permitted triggers are instance- and guard-aware

The system SHALL return an empty list from `WorkflowEngine.GetPermittedTriggers` when the instance's
`Status` is `Completed` or `Faulted`; otherwise it SHALL group the transitions leaving
`instance.CurrentState` by trigger and return the trigger of each group whose first transition's
guards all pass for that instance.

#### Scenario: Terminal instance

- **Given** an instance whose `Status` is `Completed` or `Faulted`
- **When** `GetPermittedTriggers(definition, instance)` is called
- **Then** an empty list is returned regardless of the definition's transitions

#### Scenario: Guard-denied trigger is hidden

- **Given** an active instance in `Submitted` and an `"approve"` transition whose guard fails for it
- **When** `GetPermittedTriggers(definition, instance)` is called
- **Then** `"approve"` is not in the result, while `WorkflowDefinition.GetPermittedTriggers("Submitted")` still lists it

#### Scenario: NotStarted instance is not treated as terminal

- **Given** an instance with `Status == NotStarted` in the initial state
- **When** `GetPermittedTriggers(definition, instance)` is called
- **Then** the guard-passing triggers of the initial state are returned

### Requirement: TransitionResult exposes exactly one outcome flag and never a null reason list

The system SHALL construct results only through `Success`, `Denied` and `NotFound`, such that
`Success` sets `IsSuccess` with both `FromState` and `ToState`, `Denied` sets `IsDenied` with the
supplied reasons and a null `ToState`, `NotFound` sets `IsNotFound` with a null `ToState` and no
reasons, and `DenialReasons` is an empty array rather than null whenever no reasons were supplied.

#### Scenario: Success shape

- **Given** `TransitionResult.Success("Draft", "Submitted", "submit")`
- **When** the result is inspected
- **Then** `IsSuccess` is true, `IsDenied` and `IsNotFound` are false, `ToState == "Submitted"` and `DenialReasons` is empty

#### Scenario: NotFound shape

- **Given** `TransitionResult.NotFound("Draft", "approve")`
- **When** the result is inspected
- **Then** `IsNotFound` is true, `ToState` is null and `DenialReasons.Count == 0`

### Requirement: Workflow exceptions carry the workflow name and instance id

The system SHALL expose `WorkflowName` and `InstanceId` on `WorkflowException` and on all three
subclasses (`WorkflowCompletedException`, `WorkflowFaultedException`, `WorkflowActionException`),
with `WorkflowActionException` additionally exposing `State` and `Trigger`.

#### Scenario: Diagnosing a faulted transition

- **Given** a `WorkflowActionException` caught by the caller
- **When** its properties are read
- **Then** `WorkflowName`, `InstanceId`, `State`, `Trigger` and `InnerException` identify the workflow, the instance, the phase and the root cause

### Requirement: DI registration provides a singleton engine and the Mermaid generator

The system SHALL register, from `AddWorkflowEngine()`, a singleton `IWorkflowEngine` instance with no
state-changed callback and `IWorkflowDiagramGenerator` implemented by `MermaidDiagramGenerator`; and
from `AddWorkflowEngine(configure)` with `WorkflowEngineOptions.PublishStateChanges == true`, a
singleton engine whose callback resolves all registered
`Action<StateChangeRecord, string, Guid>` services and invokes each of them per state change.
`DotDiagramGenerator` SHALL NOT be registered by either overload.

#### Scenario: Default registration

- **Given** an empty `IServiceCollection`
- **When** `AddWorkflowEngine()` is called and the provider is built
- **Then** `IWorkflowEngine` resolves to a singleton `WorkflowEngine` with no callback and `IWorkflowDiagramGenerator` resolves to `MermaidDiagramGenerator`

#### Scenario: Publishing state changes with registered callbacks

- **Given** two `Action<StateChangeRecord, string, Guid>` delegates registered in the container and `AddWorkflowEngine(o => o.PublishStateChanges = true)`
- **When** a transition succeeds on the resolved engine
- **Then** both delegates are invoked with the record, workflow name and instance id

#### Scenario: Publishing enabled with no callbacks registered

- **Given** `AddWorkflowEngine(o => o.PublishStateChanges = true)` and no `Action<StateChangeRecord, string, Guid>` registrations
- **When** a transition succeeds
- **Then** `GetServices` yields nothing, the loop body never runs, and the transition completes normally

#### Scenario: Options overload with publishing disabled

- **Given** `AddWorkflowEngine(o => { })`
- **When** the provider is built
- **Then** the registered engine is a plain `WorkflowEngine()` with no callback

### Requirement: Mermaid rendering emits a state diagram with space-substituted identifiers

The system SHALL emit, from `MermaidDiagramGenerator.Generate`, a `stateDiagram-v2` header, an
`[*] --> {InitialState}` line, one `{State} : {Description}` line per state that has a description,
one `{From} --> {To} : {Trigger}` line per transition in declaration order, and one
`{State} --> [*]` line per final state, with the result trailing-trimmed; every state name and
trigger SHALL have spaces replaced by underscores, and a description SHALL have CR/LF collapsed to
spaces and be trimmed while keeping its own spaces.

#### Scenario: Definition with a described state and a final state

- **Given** a definition `Order` with initial state `Draft`, `State("In Review").Description("Waiting for approver")`, `Transition("submit", "Draft", "In Review")` and a final `Closed`
- **When** `Generate(definition)` is called
- **Then** the output contains `[*] --> Draft`, `In_Review : Waiting for approver`, `Draft --> In_Review : submit` and `Closed --> [*]`

#### Scenario: Multi-line description

- **Given** a state description containing `"line one\r\nline two"`
- **When** the diagram is generated
- **Then** the description is rendered on one line as `line one line two`, keeping the diagram statement valid

#### Scenario: Names differing only by a space collide

- **Given** two states named `"In Review"` and `"In_Review"`
- **When** the diagram is generated
- **Then** both render as the identifier `In_Review`, so the diagram shows a single node for two distinct states

### Requirement: DOT rendering emits a left-to-right digraph with quoted, escaped labels

The system SHALL emit, from `DotDiagramGenerator.Generate`, a `digraph "{Name}"` block with
`rankdir=LR`, rounded rectangle node defaults, a `__start__` point node with an edge to the initial
state, one node line per state whose label is `Name` (or `Name\nDescription` when a description
exists) and which uses `shape=doublecircle` for final states, and one labelled edge per transition;
double-quote characters in emitted values SHALL be escaped as `\"`.

#### Scenario: Final state and described state

- **Given** a definition with `State("Closed").IsFinal()` and `State("Draft").Description("Editable")`
- **When** `Generate(definition)` is called
- **Then** the output contains `__start__ -> "Draft";`, `"Draft" [label="Draft\nEditable"];`, `"Closed" [label="Closed", shape=doublecircle];` and one `"From" -> "To" [label="trigger"];` line per transition

#### Scenario: Quoted state name

- **Given** a state named `He said "go"`
- **When** the diagram is generated
- **Then** the emitted identifier and label have their quotes escaped as `\"` so the DOT source stays parseable

### Requirement: Definitions are never persisted; only instances are

The system SHALL define persistence solely over instances via
`IWorkflowInstanceStore<TData>` — `SaveAsync(workflowName, instance)`, `LoadAsync(instanceId)`,
`DeleteAsync(instanceId)`, `FindByStateAsync(state, limit)`, `FindByStatusAsync(status, limit)` and
`FindByWorkflowNameAsync(workflowName, limit)` — with the workflow name carried per save rather than
as a stored definition, because definitions hold `Func` delegates.

#### Scenario: Round-tripping an instance

- **Given** any of the seven backends and an instance advanced to `Submitted` with one history record
- **When** `SaveAsync("OrderApproval", instance)` then `LoadAsync(instance.InstanceId)` are awaited
- **Then** the loaded instance has the same `InstanceId`, `CurrentState`, `Status`, deserialized `Data` and one history record

#### Scenario: Rebuilding the definition is the caller's job

- **Given** a loaded instance
- **When** the caller wants to fire a trigger
- **Then** it must supply an `IWorkflowDefinition<TData>` built in code, since no definition was persisted

### Requirement: Save is a non-atomic read-then-write upsert keyed on the instance id

The system SHALL, in every backend's `SaveAsync`, first read the persisted record whose `Guid`
equals `instance.InstanceId`; when found it SHALL apply `UpdateFromInstance(instance)`, overwrite
`WorkflowName` with the supplied name, call the underlying store's update and return the instance id;
when not found it SHALL build a new model via `FromInstance(workflowName, instance)` and return the
underlying store's create result. The read and the write are separate operations with no transaction
or optimistic concurrency, so concurrent saves of the same instance are not safe.

#### Scenario: First save inserts

- **Given** an instance never saved before
- **When** `SaveAsync("OrderApproval", instance)` is awaited
- **Then** a new record is created with `Guid == instance.InstanceId`, `CreatedAt` and `UpdatedAt` set to `DateTime.UtcNow`, and the create result is returned

#### Scenario: Second save updates and refreshes the workflow name

- **Given** an already-persisted instance and a save under a different `workflowName`
- **When** `SaveAsync("OrderApprovalV2", instance)` is awaited
- **Then** `CurrentState`, `Status`, the serialized data/history and `UpdatedAt` are refreshed, `CreatedAt` is left untouched, `WorkflowName` becomes `"OrderApprovalV2"`, and `instance.InstanceId` is returned

#### Scenario: Concurrent first save — SQL

- **Given** two concurrent `SaveAsync` calls for the same brand-new instance against `SqlWorkflowInstanceStore`
- **When** both observe no existing row and both create
- **Then** because `Guid` is the `[PrimaryField]` of `__WorkflowInstances`, the second insert fails with a primary-key violation, and an overlapping save can lose the first writer's changes

#### Scenario: Concurrent first save — ElasticSearch

- **Given** two concurrent `SaveAsync` calls for the same brand-new instance against `ElasticSearchWorkflowInstanceStore`
- **When** both observe no existing document and both create
- **Then** two documents are indexed silently, because `CreateAsync` mints its own `_id` rather than keying on `Guid`

#### Scenario: Concurrent first save — RavenDB

- **Given** two concurrent `SaveAsync` calls for the same brand-new instance against `RavenDBWorkflowInstanceStore`
- **When** both observe no existing document and both create
- **Then** documents are keyed by `Guid`, so the outcome is a last-writer-wins lost update rather than a duplicate

#### Scenario: Concurrent save — JSON/XML file backends

- **Given** two concurrent saves against `JsonWorkflowInstanceStore` or `XmlWorkflowInstanceStore`
- **When** the underlying `AsyncJsonStore`/`AsyncXmlStore` rewrites the whole file per save
- **Then** the writes can interleave and lose data; these backends are limited to single-process/development use

#### Scenario: Cosmos returns the stored id on the update path

- **Given** an existing Cosmos document for the instance
- **When** `SaveAsync` takes the update branch
- **Then** it returns `existing.Guid ?? instance.InstanceId`, whereas the other six backends return `instance.InstanceId` directly

### Requirement: Load returns null and Delete is a no-op for an unknown instance

The system SHALL return `null` from `LoadAsync` when no record matches the id, and SHALL, in
`DeleteAsync`, read the record first and only call the underlying store's delete when one was found —
deleting a non-existent instance therefore completes silently.

#### Scenario: Loading a missing instance

- **Given** an id that was never saved
- **When** `LoadAsync(id)` is awaited on any backend
- **Then** `null` is returned and no exception is thrown

#### Scenario: Deleting a missing instance

- **Given** an id that was never saved
- **When** `DeleteAsync(id)` is awaited
- **Then** the call completes without error and without touching the underlying store's delete path

#### Scenario: Deleting an existing instance

- **Given** a persisted instance
- **When** `DeleteAsync(instance.InstanceId)` is awaited and `LoadAsync` is retried
- **Then** the record is gone and `LoadAsync` returns `null`

### Requirement: Find queries order by UpdatedAt descending and cap at 100 by default

The system SHALL, in every backend's `FindByStateAsync`, `FindByStatusAsync` and
`FindByWorkflowNameAsync`, filter on `CurrentState`, on `Status` compared as the integer value of the
`WorkflowStatus` enum, and on `WorkflowName` respectively, order results by `UpdatedAt` descending,
and apply the `limit` argument whose default is `100`.

#### Scenario: Most recently updated first

- **Given** three persisted instances in state `Submitted` saved at different times
- **When** `FindByStateAsync("Submitted")` is awaited
- **Then** at most 100 instances are returned, ordered newest `UpdatedAt` first

#### Scenario: Status is matched as an integer

- **Given** instances with `Status` `Active` (1) and `Completed` (2)
- **When** `FindByStatusAsync(WorkflowStatus.Completed)` is awaited
- **Then** the filter compares the persisted `Status` column/field against `2` and returns only the completed instances

#### Scenario: Explicit limit

- **Given** 500 persisted instances of one workflow
- **When** `FindByWorkflowNameAsync("OrderApproval", limit: 10)` is awaited
- **Then** at most 10 instances are returned

#### Scenario: Ordering mechanism differs on Cosmos

- **Given** the CosmosDB backend
- **When** any find query runs
- **Then** the ordering is expressed as `OrderBy<CosmosWorkflowInstanceModel>.ByName(nameof(UpdatedAt), descending: true)`, whereas the other six backends use the expression form `OrderBy<T>.ByDescending(m => m.UpdatedAt)`

### Requirement: State and status queries are not scoped by workflow name except on CosmosDB

The system SHALL, on the SQL, JSON, XML, ElasticSearch, MongoDB and RavenDB backends, filter
`FindByStateAsync` and `FindByStatusAsync` on state/status alone — returning instances of every
workflow stored in the same table/collection — while the CosmosDB backend SHALL additionally require
`WorkflowName == ` the name supplied to its constructor. `FindByWorkflowNameAsync` on CosmosDB SHALL
use only its `workflowName` argument and ignore the constructor's name.

#### Scenario: Cross-workflow rows on the shared backends

- **Given** two workflows `OrderApproval` and `InvoiceApproval` persisting to the same store, both with a state named `Submitted`
- **When** `FindByStateAsync("Submitted")` is awaited on the SQL backend typed as `SqlWorkflowInstanceStore<DB, OrderData>`
- **Then** rows belonging to `InvoiceApproval` are also returned, and `ToInstance<OrderData>()` attempts to deserialize their `DataJson` into `OrderData`

#### Scenario: Cosmos scopes to its constructor name

- **Given** `new CosmosDBWorkflowInstanceStore<OrderData>("OrderApproval", settings)`
- **When** `FindByStateAsync("Submitted")` is awaited
- **Then** only documents whose `WorkflowName` is `"OrderApproval"` are returned

#### Scenario: Cosmos instance saved under a different name becomes invisible to state queries

- **Given** the same store used to call `SaveAsync("OrderApprovalV2", instance)`
- **When** `FindByStateAsync` or `FindByStatusAsync` is awaited
- **Then** that instance is excluded, because the document's `WorkflowName` no longer equals the constructor's `_workflowName`

#### Scenario: Cosmos find-by-name is unscoped

- **Given** the same `"OrderApproval"`-constructed Cosmos store
- **When** `FindByWorkflowNameAsync("InvoiceApproval")` is awaited
- **Then** `InvoiceApproval` documents are returned, unlike the state/status queries

### Requirement: A record missing its identity or payload is rejected on restore

The system SHALL throw `InvalidOperationException` from every backend's `ToInstance<TData>()` when
the persisted `Guid` is null, when the serialized payload (`DataJson`, or `DataXml` on the XML
backend) is null, empty or whitespace, or when deserializing that payload yields null — rather than
minting a replacement id or forcing a null payload into `Restore`.

#### Scenario: Record with no Guid

- **Given** a persisted record whose `Guid` is null
- **When** `ToInstance<OrderData>()` is called
- **Then** an `InvalidOperationException` is thrown reading "Workflow instance document has no Guid and cannot be restored (workflow '{name}')." — the SQL backend's wording is "Workflow instance row has no Guid …"

#### Scenario: Record with an empty payload

- **Given** a persisted record whose `DataJson` is `string.Empty` (the property default)
- **When** `ToInstance<OrderData>()` is called
- **Then** an `InvalidOperationException` "Workflow instance '{guid}' has empty DataJson and cannot be restored (workflow '{name}')." is thrown instead of an opaque deserializer error

#### Scenario: Payload deserializes to null

- **Given** a persisted record whose `DataJson` is the literal `null`
- **When** `ToInstance<OrderData>()` is called
- **Then** an `InvalidOperationException` "… DataJson deserialized to null and cannot be restored …" is thrown

#### Scenario: XML backend uses the same guards on DataXml

- **Given** an XML record whose `DataXml` is empty or deserializes to null
- **When** `ToInstance<OrderData>()` is called
- **Then** the equivalent `InvalidOperationException` naming `DataXml` is thrown

### Requirement: History deserializing to null degrades to an empty history

The system SHALL substitute an empty history list when the persisted history payload deserializes to
null, so a record with a corrupt or absent history still restores; only the payload and the identity
are fatal.

#### Scenario: Null history JSON

- **Given** a persisted record whose `HistoryJson` is the literal `null` and whose `DataJson` is valid
- **When** `ToInstance<OrderData>()` is called
- **Then** the instance restores with an empty `History` and no exception

#### Scenario: Default history value

- **Given** a record that was never written by `FromInstance`, so `HistoryJson` is its default `"[]"`
- **When** the record is restored
- **Then** `History` is empty

#### Scenario: XML empty-array root default

- **Given** an XML record whose `HistoryXml` is its default `"<ArrayOfXmlStateChangeRecord />"` or is empty/whitespace
- **When** `ToInstance<OrderData>()` is called
- **Then** the empty-list fallback is used and no "was not expected" XML root error occurs

### Requirement: Status is persisted as the raw enum integer and cast back unchecked

The system SHALL store `Status` as `(int)instance.Status` and restore it as `(WorkflowStatus)Status`
with no range validation, so an out-of-range persisted integer restores as an undefined
`WorkflowStatus` value.

#### Scenario: Round-trip of a known status

- **Given** an instance whose `Status` is `WorkflowStatus.Completed`
- **When** it is saved and loaded
- **Then** the persisted integer is `2` and the restored `Status` is `WorkflowStatus.Completed`

#### Scenario: Out-of-range persisted status

- **Given** a record whose `Status` column holds `99`
- **When** `ToInstance<OrderData>()` is called
- **Then** the restore succeeds with `Status` equal to the undefined enum value `(WorkflowStatus)99`, and the engine treats it as neither `Completed` nor `Faulted`, so triggers are accepted

### Requirement: Payload and history serialization goes through an injectable serializer seam

The system SHALL route every backend model's payload/history (de)serialization through
`Birko.Serialization.ISerializer`, accepting an optional serializer on `ToInstance`, `FromInstance`
and `UpdateFromInstance` and defaulting to a shared static instance —
`SystemJsonSerializer` (camelCase) for the SQL, JSON, ElasticSearch, MongoDB, RavenDB and CosmosDB
models, and `Birko.Serialization.Xml.SystemXmlSerializer` for the XML model.

#### Scenario: Default JSON wire format

- **Given** an instance with a payload property `OrderNumber`
- **When** it is saved via any of the six JSON-backed models
- **Then** the stored payload uses the camelCase default of `SystemJsonSerializer`

#### Scenario: Custom serializer supplied

- **Given** a caller-supplied `ISerializer`
- **When** `FromInstance(workflowName, instance, serializer)` and later `ToInstance<TData>(serializer)` are used
- **Then** that serializer performs both directions instead of the static default

#### Scenario: XML history is mapped through a serializer-friendly DTO

- **Given** an instance with two history records
- **When** the XML model serializes them
- **Then** each `StateChangeRecord` is mapped to a `XmlWorkflowInstanceModel.XmlStateChangeRecord` POCO and a concrete `List<>` is serialized — required because `StateChangeRecord` is a positional record with no parameterless constructor and `History` is exposed as the `IReadOnlyList<T>` interface, neither of which `XmlSerializer` can handle

#### Scenario: XML history is mapped back on restore

- **Given** a persisted `HistoryXml` document of `XmlStateChangeRecord` elements
- **When** `ToInstance<OrderData>()` is called
- **Then** each DTO is mapped back to a `StateChangeRecord(FromState, ToState, Trigger, OccurredAt)` and passed to `Restore`

### Requirement: Backend-specific storage mapping is fixed per provider

The system SHALL persist instances to a provider-specific shape: the SQL model maps to table
`__WorkflowInstances` with `Guid` as `[PrimaryField]` named `Id` and named columns
`WorkflowName`, `CurrentState`, `Status`, `DataJson`, `HistoryJson`, `CreatedAt`, `UpdatedAt`; the
JSON model uses camelCase `[JsonPropertyName]`s; the XML model uses `[XmlRoot("WorkflowInstance")]`
with PascalCase `[XmlElement]`s and a `DataXml`/`HistoryXml` pair; the ElasticSearch model maps
`workflowName`/`currentState` as `Keyword`, `status` as an integer `Number`, `dataJson`/`historyJson`
as non-indexed `Text`, `createdAt`/`updatedAt` as `Date`, and declares
`IndexName = "workflow-instances"`; the MongoDB model uses camelCase `[BsonElement]`s; the RavenDB
and CosmosDB models use plain unannotated properties.

#### Scenario: SQL column naming

- **Given** the SQL backend
- **When** the schema is created for `WorkflowInstanceModel`
- **Then** the table is `__WorkflowInstances` and the instance id column is `Id`, declared as the primary field

#### Scenario: ElasticSearch payload fields are not searchable

- **Given** the ElasticSearch backend
- **When** the mapping is applied
- **Then** `dataJson` and `historyJson` are `Text` fields with `Index = false`, so payloads cannot be queried, while the discriminators `workflowName` and `currentState` are `Keyword` fields the find queries can filter on

### Requirement: Schema helpers provision and drop through the underlying store

The system SHALL expose a static `EnsureCreatedAsync` / `DropAsync` pair per backend that constructs
the underlying async store for the instance model, applies the supplied settings and delegates to the
store's `InitAsync` / `DestroyAsync`.

#### Scenario: SQL provisioning

- **Given** `SqlSettings` for a SQLite/PostgreSQL/MSSql/MySQL target
- **When** `SqlWorkflowInstanceSchema.EnsureCreatedAsync<DB>(settings)` is awaited
- **Then** `AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>.InitAsync` runs and the `__WorkflowInstances` table exists

#### Scenario: Dropping

- **Given** any backend's settings
- **When** `DropAsync(settings)` is awaited
- **Then** the underlying store's `DestroyAsync` removes the table/collection/index/file

#### Scenario: MongoDB provisioning provisions nothing

- **Given** MongoDB settings
- **When** `MongoDBWorkflowInstanceSchema.EnsureCreatedAsync(settings)` is awaited
- **Then** the call is idempotent and validates the store/client, but creates no collection and no indexes — MongoDB creates collections lazily on first write and this model declares no indexes, so `InitCoreAsync` is a deliberate no-op

### Requirement: Stores can be constructed from settings or from a pre-built store

The system SHALL offer, per backend, a settings constructor that builds and configures the
underlying async store, and a constructor accepting an existing store which throws
`ArgumentNullException` when it is null; the underlying store SHALL be exposed via a public `Store`
property. The CosmosDB store additionally SHALL require a non-null `workflowName` as its first
constructor argument, throwing `ArgumentNullException` otherwise.

#### Scenario: Injecting a pre-built store

- **Given** an already-configured `AsyncMongoDBStore<MongoWorkflowInstanceModel>`
- **When** `new MongoDBWorkflowInstanceStore<OrderData>(store)` is constructed
- **Then** the instance store uses that store, exposed through `Store`, so transaction contexts can be reached

#### Scenario: Null store argument

- **Given** a null underlying store
- **When** any backend's store-taking constructor is invoked
- **Then** an `ArgumentNullException` naming `store` is thrown

#### Scenario: Cosmos requires a workflow name

- **Given** a null workflow name
- **When** `new CosmosDBWorkflowInstanceStore<OrderData>(null, settings)` is constructed
- **Then** an `ArgumentNullException` naming `workflowName` is thrown

### Requirement: Instance persistence is generic over a reference-type payload

The system SHALL constrain every `IWorkflowInstanceStore<TData>` implementation to `TData : class`
even though the interface itself declares no constraint, so a value-type payload cannot be persisted
by any shipped backend.

#### Scenario: Reference-type payload

- **Given** a payload class `OrderData`
- **When** `SqlWorkflowInstanceStore<SqLiteConnector, OrderData>` is instantiated
- **Then** the store compiles and operates

#### Scenario: Value-type payload

- **Given** a payload `struct OrderData`
- **When** a shipped backend is instantiated with it
- **Then** the `where TData : class` constraint rejects it at compile time, although `IWorkflowInstanceStore<TData>` itself would allow it
