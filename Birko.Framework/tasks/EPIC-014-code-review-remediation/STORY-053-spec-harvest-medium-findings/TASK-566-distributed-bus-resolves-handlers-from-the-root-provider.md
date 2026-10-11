---
id: TASK-566
parent: STORY-053
feature: FEATURE-014
status: todo
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

- [ ] Measured first: a `ValidateScopes = true` test fails today for both sites. A second test shows one shared instance
      across two messages with validation off.
- [ ] `DistributedEventBus` resolves DI handlers from one async scope per incoming message, disposed after dispatch,
      the same contract as `InProcessEventBus` after TASK-565.
- [ ] `AutoSubscriber` discovers handled event types without instantiating handlers from the root. It can use a
      short-lived scope, or inspect `IServiceCollection` registrations instead. Decide which and say why.
- [ ] Red-verified: pointing the resolve back at the root fails the new tests. Report the split.
- [ ] The `event-bus-and-messaging` spec requirements for the distributed path are updated to match.

## Out of scope

- `InProcessEventBus` — fixed by TASK-565.
- `DistributedEventBus`'s missing `IEventScopeAccessor` (tenant) restoration on consume, which the spec records as a
  known gap. It is a different property.

## Human test plan

N/A — a library contract with no UI surface; fully covered by automated tests.
