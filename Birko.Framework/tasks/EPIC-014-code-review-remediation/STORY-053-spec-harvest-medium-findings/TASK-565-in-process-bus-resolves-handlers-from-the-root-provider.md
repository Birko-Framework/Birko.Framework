---
id: TASK-565
parent: STORY-053
feature: FEATURE-014
status: in-progress
priority: P1
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M120]
pr: null
github-issue: null
jira-key: null
---

# The in-process bus resolves event handlers from the root provider — scoped handler dependencies throw or are shared forever

## Context

Found from the consumer side by Symbio TASK-905 (2026-10-11). On Symbio's **Development** API almost every `__Outbox`
row is `Failed` with:
> Cannot resolve 'IEnumerable`1[IEventHandler`1[...OrderPlaced]]' from root provider because it requires scoped service
> '...InvoiceService'.

This is **SH-M120** from the 2026-07-30 spec harvest, triaged under [[TASK-155]] and not yet worked. It is pulled out
here because it is a live P1 in a consumer, and TASK-155's other 23 findings are not.

**Mechanism, read from the code:**
- `AddEventBus` (`Birko.EventBus/Extensions/EventBusServiceCollectionExtensions.cs:28`) registers `IEventBus` as a
  **singleton** built with `sp`, the **root** provider.
- `InProcessEventBus.GetHandlers` (`Birko.EventBus/Local/InProcessEventBus.cs:101`) calls
  `_serviceProvider.GetService(IEnumerable<IEventHandler<T>>)` on that provider. **No `IServiceScope` exists anywhere on
  the publish path.**
- The outbox makes it unconditional. `OutboxProcessor` publishes from a hosted-service loop through the inner bus, and
  `IEventScopeAccessor` (Birko.EventBus.Tenant) restores the ambient **tenant** without creating a DI scope.
- With `ValidateScopes` on (ASP.NET Development) the resolve throws, so every handler with a scoped dependency fails.
  With it off (every other environment) scoped services are resolved **from the root**: one instance shared by every
  dispatch for the life of the process and never disposed. That is a captive dependency.

Consumers on `AddEventBus`: Symbio (with the outbox), DraCode.KoboldLair.Server.

## Acceptance criteria

- [ ] Measured, not asserted: a test shows that before the fix two publishes with `ValidateScopes = false` see the
      **same** scoped instance, and that with `ValidateScopes = true` the publish throws.
- [ ] `InProcessEventBus` resolves DI handlers from a **fresh async scope per `PublishAsync`**, keeps that scope for the
      whole pipeline + dispatch, and disposes it afterwards (async disposal, so `IAsyncDisposable` handlers/deps work).
- [ ] `AddEventBus` composition works under `ValidateScopes = true` + `ValidateOnBuild = true` with a handler that
      depends on a scoped service, both through direct publish and through `AddOutbox` + `OutboxProcessor`.
- [ ] Each publish gets a distinct scoped instance, and it is disposed after that publish.
- [ ] Manual-only buses (`new InProcessEventBus()`, no provider) behave exactly as before; existing EventBus tests
      stay green.
- [ ] The new tests are red-verified: resolving from the root again fails them. Report the split as numbers.
- [ ] `SPEC-HARVEST-FINDINGS-2026-07-30.md` § SH-M120 carries a `Verdict:` line, and [[TASK-155]] notes that SH-M120
      moved here.
- [ ] `/specs regen event-bus-and-messaging` (or a hand edit of the requirement if regen is not run), with the spec
      diff reviewed. The spec currently documents the root-provider resolve as shipped behaviour.

## Out of scope

- TASK-155's other 23 event-bus findings.
- Per-**handler** scopes under `MaxConcurrency > 1`. Parallel handlers of one publish share the publish's scope. That
  is a documented choice, recorded in the bus's XML doc, not a fix this task makes.
- `DistributedEventBus`'s consumer side. It has its own handler resolve path; checking it for the same defect is a
  separate read.

## Human test plan

N/A — a library contract with no UI surface; fully covered by automated tests. The consumer-visible check (a Symbio
guest order on the Development API producing its e-mail) belongs to Symbio TASK-905's plan.

## Implementation plan

1. Characterisation test first (criterion 1) in `tests/Birko.EventBus.Tests`: root built with and without
   `ValidateScopes`, a scoped probe dependency, two publishes.
2. `InProcessEventBus`: in `PublishAsync`, when a provider is present, open
   `_serviceProvider.GetService<IServiceScopeFactory>()?.CreateAsyncScope()` and resolve `GetHandlers` from
   `scope.ServiceProvider`, running enrichment, the pipeline and dispatch inside it with `await using`. A provider with no
   scope factory (a hand-rolled `IServiceProvider` in a test) falls back to resolving from it directly, as today.
3. Tests for criteria 2–5, including an outbox round trip with `AddOutbox` (in-memory store) + `OutboxProcessor`.
4. Mutation: revert step 2 and confirm the new tests go red, then restore.
5. Verdict line, TASK-155 note, spec update. Then commit and push. Symbio TASK-905 consumes it.
