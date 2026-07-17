---
id: STORY-045
parent: EPIC-017
status: done
created: 2026-07-17
completed: 2026-07-17
theme: decorator-order-per-tenant-uniqueness
affects: [Birko.Data.Composition, Birko.Data.Tenant]
---

# Fix decorator ordering so per-tenant uniqueness probes are tenant-scoped

## Outcome (2026-07-17) — DONE

Relocated the `ITenant` wrapper in `StoreWrapperBuilder.Build` from **outermost** to between the
Audit and SoftDelete blocks. New chain (outermost → innermost):

```
Default → Sluggable → SoftDelete → Tenant → Audit → Timestamp → EventSourcing → raw
```

Pure positional change (plus the chain-order doc comment) — no wrapper-internal logic touched.
`Default`/`Sluggable` probe + corrective queries now pass through the tenant filter (per-tenant
uniqueness); `Tenant` stays outside `EventSourcing`/`Timestamp`/`Audit` (guard still rejects before
an event is recorded; `TenantGuid` stamped in time for the audit/event payload).

Tests added to `Birko.Data.Composition.Tests` (suite 12/12 green; `Birko.Data.Tenant.Tests` 23/23
unaffected). Red→green verified by stashing the reorder:
- `Default_uniqueness_is_scoped_per_tenant_not_global` — proven RED on old order (tenant B cleared
  tenant A's default).
- `Slug_uniqueness_is_scoped_per_tenant_not_global` — proven RED on old order (tenant B forced to
  `electronics-2`).
- `Default_filter_update_only_touches_current_tenant_rows` — `UpdateAsync(filter, Action<T>)`
  scoping (same Default inner-store mechanism).
- `Tenant_guard_rejects_cross_tenant_write_before_any_event_is_recorded` — invariant guard
  (correct by construction both orders; pins that Tenant stays outside EventSourcing).

Commits — Birko.Data.Composition (source), Birko.Data.Composition.Tests (tests),
Birko.Framework (this story).

---


## Goal

For entities that are both `ITenant` and `IDefault`/`ISluggable`, uniqueness must be enforced
**per tenant**, and a uniqueness wrapper's corrective writes must never cross the tenant boundary.

## Problem

`StoreWrapperBuilder.Build` applies decorators innermost-first, yielding
(outermost → innermost):

```
Tenant → Default → Sluggable → SoftDelete → Audit → Timestamp → EventSourcing → raw
```

`AsyncDefaultStoreWrapper` and `AsyncSluggableBulkStoreWrapper` issue their **own** queries against
their *inner* store. With Tenant **outermost**, that inner store is **below** Tenant, so those
queries are **not tenant-scoped**:

- `AsyncDefaultStoreWrapper.UnsetOtherDefaultsAsync` →
  `_innerStore.ReadAsync(e => e.IsDefault)` reads **every tenant's** defaults and bulk-updates
  them. Creating/updating a default in tenant A silently clears the default flag on tenants B,
  C, … — **cross-tenant data corruption**.
- `AsyncSluggableBulkStoreWrapper.ResolveSlugAsync` checks slug collisions against **all tenants**
  → slugs are globally unique instead of per-tenant (**functionality bug** + cross-tenant
  existence leak).
- `AsyncDefaultStoreWrapper.UpdateAsync(filter, Action<T>)` reads then writes below the tenant
  boundary — same defect.

The framework already handles this exact class of bug correctly for SoftDelete (`StoreWrapperBuilder`
comment, line 66: "Sluggable positioned after SoftDelete so uniqueness checks only consider
non-deleted records") — it simply was never applied to Tenant.

## Fix

Relocate the Tenant wrapper so it sits **inside** the probe-issuing / delete-converting wrappers
but **outside** the mechanical enrichers. New order (outermost → innermost):

```
Default → Sluggable → SoftDelete → Tenant → Audit → Timestamp → EventSourcing → raw
```

Rationale:
- **Inside Default & Sluggable** → their probe reads and corrective writes now pass through Tenant
  → scoped to the current tenant (and guarded).
- **Inside SoftDelete** → uniqueness probes remain filtered to non-deleted rows (unchanged) *and*
  become tenant-scoped.
- **Outside EventSourcing / Timestamp / Audit** → the tenant guard still rejects a cross-tenant
  write **before** an event is recorded (no orphan events), and `TenantGuid` is still stamped in
  time for the audit/event payload.

Reads stay tenant-filtered: the caller's read descends Default → Sluggable → SoftDelete →
**Tenant** (adds the tenant predicate) → raw. Isolation of the primary read path is preserved;
only the *sibling wrappers' internal* queries change (they gain scoping).

In `StoreWrapperBuilder.Build`, this means applying the Tenant wrapper **before** SoftDelete /
Sluggable / Default in the innermost-first construction (i.e. move the `ITenant` block from the
current outermost position to just after the Audit block), and updating the chain-order doc comment.

## Verify against intent

Confirm no consumer deliberately relies on **global** cross-tenant uniqueness of slugs/defaults
(extremely unlikely — tenant A's slug blocking tenant B is not sensible). The SoftDelete precedent
is strong evidence this is an oversight; treat global uniqueness as the bug, per-tenant as correct.

## Acceptance / tests

- New regression test (`Birko.Data.Composition` tests): an entity type that is `ITenant + IDefault`
  — set default in tenant A, then set default in tenant B; assert tenant A's default is **still
  set** (was silently cleared before the fix).
- Analogous test for `ITenant + ISluggable`: the same slug can be used in tenant A and tenant B;
  no cross-tenant collision; the collision suffix is computed per tenant.
- `AsyncDefaultStoreWrapper.UpdateAsync(filter, Action<T>)` under a tenant only reads/writes that
  tenant's rows.
- Existing Default / Sluggable / SoftDelete tests (non-tenant entities) stay green — reorder must
  not regress the single-tenant / no-tenant paths.
- Confirm the EventSourcing "no orphan event on tenant-guard rejection" invariant still holds after
  the move (tenant guard fires before EventSourcing records).

## Notes

- Shares fixtures with STORY-044; sequence 045 alongside/after 044 since both edit the tenant
  wrappers + builder.
- Pure ordering change in `Build` + doc-comment update; no wrapper-internal logic change required
  for the reorder itself (the wrappers are already correct once positioned right).
