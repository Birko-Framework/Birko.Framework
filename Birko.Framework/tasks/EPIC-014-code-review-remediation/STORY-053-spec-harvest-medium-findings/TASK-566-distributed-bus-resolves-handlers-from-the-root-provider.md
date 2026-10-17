---
id: TASK-566
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M120]
pr: null
github-issue: null
jira-key: null
---

# The distributed bus resolves event handlers from the root provider too — the same defect as TASK-565 on the message-queue path

## Context

Found while fixing [[TASK-565]] (SH-M120), which gave `InProcessEventBus` one DI scope per publish. The message-queue
transport has the same shape and was not part of that finding:
- `Birko.EventBus.MessageQueue/DistributedEventBus.cs:221` — `GetHandlers` calls `_serviceProvider.GetService(IEnumerable<IEventHandler<T>>)`
  on the provider the singleton bus was built with (the root). Messages arrive from a transport subscription, where no
  request scope exists. So a handler with a scoped dependency throws under `ValidateScopes`, and everywhere else it
  shares one root-owned instance across every message.
- `Birko.EventBus.MessageQueue/AutoSubscriber.cs:88` — at startup it resolves `IEnumerable<IEventHandler<T>>` from the
  root to learn which event types have handlers. Under `ValidateScopes` that throws for any handler with a scoped
  dependency, before a single message is consumed.

No known consumer runs the distributed bus today: Symbio and DraCode use `AddEventBus`. That is why this is P2.

## Acceptance criteria

- [x] Measured first: a `ValidateScopes = true` test fails today for both sites. A second test shows one shared instance
      across two messages with validation off.
- [x] `DistributedEventBus` resolves DI handlers from one async scope per incoming message, disposed after dispatch,
      the same contract as `InProcessEventBus` after TASK-565.
- [x] `AutoSubscriber` discovers handled event types without instantiating handlers from the root. It can use a
      short-lived scope, or inspect `IServiceCollection` registrations instead. Decide which and say why.
- [x] Red-verified: pointing the resolve back at the root fails the new tests. Report the split.
- [x] The `event-bus-and-messaging` spec requirements for the distributed path are updated to match.

## Out of scope

- `InProcessEventBus` — fixed by TASK-565.
- `DistributedEventBus`'s missing `IEventScopeAccessor` (tenant) restoration on consume, which the spec records as a
  known gap. It is a different property.

## Human test plan

N/A — a library contract with no UI surface; fully covered by automated tests.

## Resolution (2026-10-17)

Measured first (`tests/Birko.EventBus.MessageQueue.Tests/Task566HandlerScopeTests.cs`). On the old code, 4 of 4 failed:
- The bus site failed 2: under `ValidateScopes` delivery threw "Cannot resolve IEnumerable<IEventHandler<..>> from root
  provider because it requires scoped service". With validation off, two messages shared one root-owned dependency that
  was never disposed.
- The AutoSubscriber site failed 2: the same exception at `SubscribeAllAsync`, before any message, through both the
  `IsService` path and the scope fallback.

- `DistributedEventBus` opens one async scope per delivered message, the way `InProcessEventBus.PublishAsync` does
  (TASK-565). Handlers are resolved from it and it is disposed after the pipeline. A provider without
  `IServiceScopeFactory` is used directly, as on the in-process path.
- `AutoSubscriber` asks `IServiceProviderIsService.IsService(IEventHandler<T>)`. **Why this over the alternatives:**
  - `IServiceCollection` is not available at runtime: the hosted service only has the provider.
  - A scope would still build every handler at startup, with each one's dependencies (DB contexts, clients), only to
    learn that it exists.
  - `IsService` answers from the registrations and builds nothing. The test pins 0 constructions.

  A container without `IsService` is asked inside a short-lived scope. That branch is reachable for third-party
  containers, so it has its own test (rule 53).
- Spec: a new requirement, "Distributed delivery resolves DI handlers from one scope per message". The AutoSubscriber
  requirement now says "has a registration", asked without resolving from the root.
- Regression: both importers of `Birko.EventBus.MessageQueue.projitems` pass: MessageQueue 7/7, EventBus 113/113.
