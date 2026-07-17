---
id: EPIC-017
status: in-progress
created: 2026-07-17
owner: ai
affects: [Birko.Data.Tenant, Birko.Data.Composition, Birko.EventBus, Birko.EventBus.Outbox, Birko.EventBus.MessageQueue]
source: security-review (Symbio consumer) 2026-07-17
---

# Tenant isolation hardening

## Area of concern

A security review of Symbio (a multi-tenant consumer) found that data-layer tenant
isolation in `Birko.Data.Tenant` is **fail-open**, and a code read while triaging it
surfaced a **second, independent** cross-tenant defect in the decorator ordering owned by
`Birko.Data.Composition`. Both are latent for *every* multi-tenant consumer of the
framework (~16 consumers), not just Symbio.

The root design flaw behind the fail-open behaviour: **"no tenant set" is overloaded to mean
two opposite things** — (a) "tenancy isn't wired / this is a bug" and (b) "I intend
cross-tenant admin access." The current code treats both as (b), so a mis-wired context
silently grants cross-tenant access instead of failing closed.

### Finding 1 — fail-open when no tenant is in scope (`Birko.Data.Tenant`)

- `Filters/ModelByTenant.cs` — `Filter()` returns **only** the caller's `BaseFilter` (no
  tenant predicate) when `TenantGuid` is null or `Guid.Empty`. A tenant-scoped read issued
  with no tenant set returns **every tenant's rows**.
- `Stores/AsyncTenantStoreWrapper.cs` — `BelongsToCurrentTenant` returns `true` when
  `!HasTenant` (deliberate "admin mode", CR-L229); `SetTenantGuidIfNeeded` stamps
  `TenantGuid = CurrentTenantGuid ?? Guid.Empty`, creating orphan `Guid.Empty` rows.
- The fail-open surface is **wider than reads**: the filter-based bulk writes in
  `AsyncTenantBulkStoreWrapper` — `UpdateAsync(filter, …)`, `UpdateAsync(filter, PropertyUpdate)`,
  `DeleteAsync(filter)` — build `new ModelByTenant<T>(…)` directly too, so with no tenant a
  filter-based delete **deletes across all tenants**.
- The static `Models.Tenant.Current` singleton fallback in the wrapper ctor (CR-M175) is the
  real footgun: a store constructed without a scoped `ITenantContext` silently degrades to
  non-tenant (unfiltered) mode.

### Finding 2 — decorator order defeats per-tenant uniqueness (`Birko.Data.Composition`)

`StoreWrapperBuilder.Build` places the **Tenant** wrapper **outermost**
(`Tenant → Default → Sluggable → SoftDelete → … → raw`). But `AsyncDefaultStoreWrapper` and
`AsyncSluggableBulkStoreWrapper` issue their **own probe/corrective queries** against their
*inner* store — which, with Tenant outermost, is **below** Tenant and therefore **not
tenant-scoped**:

- `AsyncDefaultStoreWrapper.UnsetOtherDefaultsAsync` reads `_innerStore.ReadAsync(e => e.IsDefault)`
  across **all tenants** and bulk-updates them → creating a default in tenant A silently
  clears the default flag on tenants B, C, … (cross-tenant data corruption).
- `AsyncSluggableBulkStoreWrapper` resolves slug collisions against all tenants → slugs become
  globally unique instead of per-tenant (functionality bug + cross-tenant existence leak).
- Same defect on `AsyncDefaultStoreWrapper.UpdateAsync(filter, Action<T>)` (reads + writes below
  the tenant boundary).

The framework **already established the correct principle** for this exact class of bug —
"Sluggable positioned after SoftDelete so uniqueness checks only consider non-deleted records"
(`StoreWrapperBuilder` line 66) — but never applied it to Tenant. This is an oversight, not a
designed choice: global cross-tenant uniqueness of slugs/defaults is not sensible behaviour for
a multi-tenant framework.

## Constraints

- The framework is shared by ~16 consumers and the fail-open is documented as intentional +
  test-pinned. **Do not flip the default.** Behaviour changes are opt-in; the permissive default
  is preserved for backward compatibility.
- Admin / non-tenant mode is a legitimate feature (back-office, migrations, seeding) and must
  survive — but it should become an **explicit, typed scope**, not the consequence of an unset
  context.

## Stories

- **STORY-044** (**done** 2026-07-17) — Opt-in strict (fail-closed) tenancy mode. `TenantIsolationMode`
  { Permissive (default), Strict }; virtual `TenantFilter` seam across both async **and** sync
  wrappers (reads/count/filter-writes); mode-aware `BelongsToCurrentTenant` / `SetTenantGuidIfNeeded`;
  `tenantMode` + `tenantWrapperFactory` on `StoreWrapperBuilder.Build`, `mode` on `AsTenantAware` and
  the DI repository extensions; explicit `WithAllTenants` admin scope on `ITenantContext` (non-breaking
  DIMs). 9 strict/admin/parity tests. Permissive default unchanged.
- **STORY-045** (**done** 2026-07-17) — Fix decorator ordering so per-tenant uniqueness probes are
  tenant-scoped. Relocated the Tenant wrapper to sit **inside** Default/Sluggable/SoftDelete but
  **outside** Audit/Timestamp/EventSourcing. 4 regression tests (Default + Sluggable per-tenant
  uniqueness both proven red→green; filter-update scoping; guard-before-event invariant).
- **STORY-046** (in-progress) — Restore ambient (tenant) scope for background event dispatch. Async
  handlers throw under Strict because the outbox processor / MQ consumer dispatch outside the request's
  async flow. New transport-agnostic `IEventScopeAccessor` (no-op default) in `Birko.EventBus`;
  `OutboxProcessor` restores scope from `entry.TenantGuid` before re-publish (red→green, 9/9); new
  `Birko.EventBus.Tenant` bridge (`AddEventTenantScope`, 4/4). Framework follow-up: distributed-consumer
  pipeline behavior. (Consumer adoption is tracked per-consumer, e.g. Symbio TASK-156 — not this EPIC.)

## Success criteria

- A multi-tenant consumer can opt into fail-closed isolation with a **one-line** switch
  (`tenantMode: Strict`) — no per-consumer subclassing of security-critical logic.
- In Strict mode: reads/count with no tenant **throw** (not return empty); filter-based writes
  throw; item writes with no tenant throw rather than stamp `Guid.Empty`.
- Default/Sluggable uniqueness is enforced **per tenant** for entities that are both `ITenant`
  and `IDefault`/`ISluggable`; a regression test proves tenant A's write cannot mutate tenant B's
  rows.
- Permissive mode is byte-for-byte the existing behaviour; all currently-passing tenant tests
  stay green.

## Design notes / open questions

- **Where does the mode live?** Prefer a `TenantIsolationMode` value threaded via
  `StoreWrapperBuilder.Build` → wrapper ctor. If it goes on `ITenantContext`, use a C# default
  interface method (`=> Permissive`) so no consumer implementation breaks.
- **Throw vs empty on Strict reads:** throw — a silent empty result masks the wiring bug; an
  exception surfaces it loudly in dev/test. The cost (a legit tenant-less background job throws)
  is exactly what the explicit admin scope is for.
- **`Guid.Empty` sentinel** is itself smelly (a valid-looking tenant id). Strict mode never
  produces it; if a real "system/global" partition is ever wanted, give it a named non-empty id.
- **Ordering vs strict mode are complementary**, not alternatives — 044 closes the no-tenant
  fail-open; 045 closes cross-tenant leakage between sibling wrappers. Both land in this EPIC.
