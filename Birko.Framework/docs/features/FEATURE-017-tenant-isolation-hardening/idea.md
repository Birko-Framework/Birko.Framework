---
id: FEATURE-017
created: 2026-07-17
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Tenant isolation hardening

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-017](../../../tasks/EPIC-017-tenant-isolation-hardening/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

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

## Proposed shape

- A multi-tenant consumer can opt into fail-closed isolation with a **one-line** switch
  (`tenantMode: Strict`) — no per-consumer subclassing of security-critical logic.
- In Strict mode: reads/count with no tenant **throw** (not return empty); filter-based writes
  throw; item writes with no tenant throw rather than stamp `Guid.Empty`.
- Default/Sluggable uniqueness is enforced **per tenant** for entities that are both `ITenant`
  and `IDefault`/`ISluggable`; a regression test proves tenant A's write cannot mutate tenant B's
  rows.
- Permissive mode is byte-for-byte the existing behaviour; all currently-passing tenant tests
  stay green.

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Data.Tenant, Birko.Data.Composition, Birko.EventBus, Birko.EventBus.Outbox, Birko.EventBus.MessageQueue]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
