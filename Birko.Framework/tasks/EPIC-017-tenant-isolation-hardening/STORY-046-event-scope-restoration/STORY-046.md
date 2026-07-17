---
id: STORY-046
parent: EPIC-017
status: in-progress
created: 2026-07-17
theme: event-scope-restoration
affects: [Birko.EventBus, Birko.EventBus.Outbox, Birko.EventBus.MessageQueue]
origin: Symbio Strict-adoption blocker — async event handlers throw under TenantIsolationMode.Strict
---

# Restore ambient (tenant) scope for background event dispatch

## Problem

Under `TenantIsolationMode.Strict` (STORY-044), any async event handler that touches a tenant-scoped
repository throws "no tenant in scope", because handler dispatch happens **outside** the publishing
request's async flow and nothing restores the ambient `ITenantContext` (AsyncLocal) the event was
enriched under.

Two boundaries lose the scope:

1. **Outbox** — `OutboxEventBus.PublishAsync` persists `entry.TenantGuid`, but `OutboxProcessor`
   (background loop) re-publishes via the inner bus **without** reading it. The inner
   `InProcessEventBus.PublishAsync` builds a fresh `EventContext` and **re-runs enrichers** against an
   unset ambient → `context.TenantGuid` comes back **null** and handlers run tenant-less. (Latent even
   pre-Strict: a distributed inner bus writes `EventEnvelope.TenantGuid = null`, silently dropping the
   tenant across the outbox hop.)
2. **Distributed consumer** — `DistributedEventBus.SubscribeToTransportAsync` rebuilds `context` from
   `envelope.TenantGuid` (populated) but dispatches in the MQ consumer's tenant-less flow without
   restoring ambient.

In-memory (non-outbox) publish is **fine** — `InProcessEventBus` dispatches inline in the publisher's
flow, so ambient is preserved.

Key insight: a pipeline behavior reading `context.TenantGuid` fixes the distributed-consumer path (data
is populated there) but **not** the outbox→in-process path, because `context.TenantGuid` is already null
by dispatch time. The outbox must restore scope from the persisted `entry.TenantGuid` at re-publish.

## Design

A single transport-agnostic hook, defined in `Birko.EventBus` core so the outbox/MQ layers restore scope
**without** depending on `Birko.Data.Tenant` (layering):

```csharp
public interface IEventScopeAccessor
{
    Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken ct = default);
}
```

- Default `NullEventScopeAccessor` (no-op) — behaviour unchanged until a bridge is registered.
- A **tenant bridge** (small package or consumer) implements it: `context.TenantGuid` set →
  `WithTenantAsync`, null → `WithAllTenants` (mirrors the STORY-044 jobs fix).
- Applied at two points, both via the one accessor:
  - **`OutboxProcessor`** wraps re-publish with a context carrying `entry.TenantGuid` (this is the part
    that unblocks Symbio, whose inner bus is in-process).
  - A built-in **`ScopeRestorationBehavior : IEventPipelineBehavior`** for the distributed-consumer path
    (idempotent no-op for in-memory / outbox-in-process). *(Behavior deferred — see Status.)*

## Status (2026-07-17) — outbox path prototyped (DONE); pipeline behavior TODO

Landed (framework, uncommitted):
- `Birko.EventBus/Core/IEventScopeAccessor.cs` — `IEventScopeAccessor` + `NullEventScopeAccessor`
  (registered in `.projitems`).
- `OutboxProcessor` — optional `IEventScopeAccessor scopeAccessor` ctor param (defaults to the no-op);
  `ProcessBatchAsync` re-establishes scope from `entry.TenantGuid` (+ correlation/headers) before
  re-publishing.
- `OutboxServiceCollectionExtensions.AddOutbox` — resolves `IEventScopeAccessor` from DI (optional) and
  passes it to the processor. Register a bridge → automatic; absent → no-op.
- Tests: `Birko.EventBus.Outbox.Tests/OutboxScopeRestorationTests.cs` (suite 9/9). Red→green verified by
  temporarily reverting the wrap: the restore test failed (tenant lost), passed once restored. The tests
  use an AsyncLocal stand-in for the ambient tenant — **no dependency on Birko.Data.Tenant**, proving the
  layering.

Also landed — the **tenant bridge** (new sibling `Birko.EventBus.Tenant`, scaffolded via
`new-birko-subproject`):
- `TenantEventScopeAccessor : IEventScopeAccessor` — maps `EventContext.TenantGuid` →
  `WithTenantAsync` (set) / `WithAllTenantsAsync` (null/`Guid.Empty` = system event).
- `AddEventTenantScope()` DI extension (over `Tenant.Current`; overload takes an explicit context).
- Registered in `.slnx` + `.code-workspace`; `Birko.EventBus.Tenant.Tests` 4/4 green (specific-tenant,
  null→all-tenants, empty→system, DI wiring). This is the only project depending on both
  `Birko.EventBus` and `Birko.Data.Tenant`, keeping the two cores independent.

Not done yet (framework follow-up):
- **`ScopeRestorationBehavior : IEventPipelineBehavior`** for the distributed-consumer path (only needed
  once a distributed/MQTT transport dispatches these handlers; Symbio uses in-memory outbox today).

Consumer adoption is **out of scope for this framework story** — calling `AddEventTenantScope()` and
flipping `TenantIsolationMode.Strict` is per-consumer work tracked in each consumer's own `tasks/`
(for Symbio: `Symbio/tasks/.../TASK-156`), per the polyrepo split in the root `CLAUDE.md`.

## Decisions

- **Opt-in registration, automatic effect** — no-op default keeps `Birko.EventBus` tenant-agnostic;
  registering the bridge activates it across all transports. Tie the registration to Strict adoption.
- **null-tenant → `WithAllTenants`** — system/global events work; the trade-off is a mis-enriched event
  runs cross-tenant instead of throwing (same fail-open-on-null as the jobs). Enrichment correctness is
  load-bearing. Revisit to "null → throw unless marked global" only if mis-enriched events show up.

## Acceptance

- [x] Outbox processor restores scope from `entry.TenantGuid` before re-publish (red→green test).
- [x] Default (no bridge) behaviour unchanged (no-op accessor; existing outbox tests green).
- [x] Abstraction is tenant-agnostic (test proves it without Birko.Data.Tenant).
- [x] Tenant bridge (`Birko.EventBus.Tenant`) + `AddEventTenantScope()` (4/4 tests).
- [ ] Distributed-consumer pipeline behavior.

(Consumer adoption — wiring `AddEventTenantScope()` + the Strict flip + end-to-end verification — is
tracked in the consumer repo, not here; e.g. Symbio's TASK-156.)
