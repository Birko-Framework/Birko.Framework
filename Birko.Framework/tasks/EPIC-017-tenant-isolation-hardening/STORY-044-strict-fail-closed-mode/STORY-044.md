---
id: STORY-044
parent: EPIC-017
status: done
created: 2026-07-17
completed: 2026-07-17
theme: strict-fail-closed-tenancy
affects: [Birko.Data.Tenant, Birko.Data.Composition]
---

# Opt-in strict (fail-closed) tenancy mode

## Outcome (2026-07-17) — DONE

Added `TenantIsolationMode { Permissive (default), Strict }` and made the tenant layer fail-closed
on opt-in, with the permissive default byte-for-byte unchanged (backward compatible for all ~16
consumers).

- **Seam:** `TenantFilter(filter)` is now the single overridable choke point through which every
  read/count and every filter-based write composes the tenant predicate (replaced all inline
  `new ModelByTenant<T>(…)` sites in **both** async and sync wrappers). In Strict with no tenant it
  throws instead of returning the caller's unscoped filter.
- **Guards:** `BelongsToCurrentTenant` denies (→ `UnauthorizedAccessException`) and
  `SetTenantGuidIfNeeded` throws rather than stamping `Guid.Empty` when Strict + no tenant.
- **Both wrapper families:** async (`AsyncTenant[Bulk]StoreWrapper`) **and** sync
  (`Tenant[Bulk]StoreWrapper`) are mode-aware (sync parity closed).
- **Explicit admin scope:** `ITenantContext.IsAllTenantsScope` + `WithAllTenants` /
  `WithAllTenantsAsync` (added as non-breaking **default interface methods**; `TenantContext` backs
  them with `AsyncLocal<bool>`). In Strict, an active all-tenants scope allows deliberate
  cross-tenant work and preserves the caller's per-item `TenantGuid` on create. This replaces
  "no tenant = accidental admin" with "admin = a scope you typed."
- **Opt-in surfaces:** `StoreWrapperBuilder.Build` gained `tenantMode` + a `tenantWrapperFactory`
  escape hatch; `AsTenantAware` (both overloads) and all six `AddTenant[Async]Repository[Scoped]`
  DI methods take `mode`. One-line opt-in — no consumer subclassing.

Tests: `Birko.Data.Composition.Tests` 21/21 (strict read/create/filter-delete throw; permissive
default still fails open; strict-with-tenant scopes; admin-scope read + create-preserve;
`AsTenantAware` strict; sync-wrapper strict). `Birko.Data.Tenant.Tests` 23/23 unchanged.

Known non-gap: a custom `ITenantContext` that doesn't override the new DIMs gets safe fail-closed
behavior (no admin scope) — documented in the interface XML.

Commits — Birko.Data.Tenant (source), Birko.Data.Composition (Build hook),
Birko.Data.Composition.Tests (tests), Birko.Framework (this story).

---

## Goal

Let a multi-tenant consumer opt into fail-closed isolation with a one-line switch, without
forking the framework's security-critical tenant logic and without changing behaviour for the
~15 consumers that rely on today's permissive default.

## Problem recap

`Birko.Data.Tenant` fails **open** when no tenant is in scope:

- `ModelByTenant<T>.Filter()` drops the tenant predicate when `TenantGuid` is null/`Guid.Empty`
  → reads/count return all tenants' rows.
- The read/count path and the **filter-based** bulk writes
  (`UpdateAsync(filter, …)`, `UpdateAsync(filter, PropertyUpdate)`, `DeleteAsync(filter)`) build
  `new ModelByTenant<T>(…)` inline, so all of them fail open together.
- `BelongsToCurrentTenant` returns `true` when `!HasTenant` (item writes fail open in "admin mode").
- `SetTenantGuidIfNeeded` stamps `Guid.Empty` when no tenant → invisible orphan rows.

There is currently **no seam** on the read path (the inline `new ModelByTenant<T>` can't be
overridden) and `StoreWrapperBuilder.Build` hardcodes `AsyncTenantBulkStoreWrapper<,>`, so a
consumer can't inject fail-closed behaviour through the normal build path.

## Scope / plan

1. **`TenantIsolationMode` enum** `{ Permissive, Strict }` in `Birko.Data.Tenant.Models`.
   Default everywhere = `Permissive` (byte-for-byte current behaviour).

2. **Single filter-factory seam** on `AsyncTenantStoreWrapper` (inherited by the bulk wrapper):
   ```csharp
   protected virtual IFilter<T> TenantFilter(Expression<Func<T, bool>>? filter)
       => new ModelByTenant<T>(_tenantContext.CurrentTenantGuid, filter);
   ```
   Replace **every** inline `new Filters.ModelByTenant<T>(…)` — `AsyncTenantStoreWrapper` reads
   (lines 49, 54) and `AsyncTenantBulkStoreWrapper` read + filter-based writes (lines 53, 71, 76,
   81) — with `TenantFilter(filter).Filter()`. One override now closes reads, count, **and** all
   three filter-based write paths.

3. **Mode-aware enforcement** (in the wrapper, consulting the mode):
   - Strict + no tenant on a read/count/filter-write → throw
     (`InvalidOperationException("no tenant in scope")`), not fail-open.
   - `BelongsToCurrentTenant`: Strict + `!HasTenant` → `false` (call sites already throw
     `UnauthorizedAccessException`).
   - `SetTenantGuidIfNeeded`: Strict + no tenant → **throw** instead of stamping `Guid.Empty`.

4. **`StoreWrapperBuilder.Build` hook** — add:
   ```csharp
   TenantIsolationMode tenantMode = TenantIsolationMode.Permissive,
   Func<IAsyncBulkStore<T>, ITenantContext, IAsyncBulkStore<T>>? tenantWrapperFactory = null
   ```
   Use `tenantWrapperFactory` at the tenant layer if supplied; otherwise construct the built-in
   wrapper with `tenantMode`. (The factory is the escape hatch for exotic needs; plain
   fail-closed is just `tenantMode: Strict`.)

5. **Optional (recommended) — explicit admin scope.** Add `WithAllTenants` / a system scope so
   cross-tenant admin access is *typed* rather than the accident of an unset context. If added,
   Strict mode requires this to be active for any cross-tenant operation.

## Design decisions (from EPIC design notes)

- Mode is threaded via `Build` → wrapper ctor. If it ever moves onto `ITenantContext`, use a C#
  **default interface method** (`=> Permissive`) so no consumer implementation breaks.
- Strict reads **throw**, they do not return empty (silent empty masks the wiring bug).

## Acceptance / tests

- Permissive mode: all existing tenant tests stay green (behaviour unchanged).
- Strict mode, no tenant in scope: `ReadAsync` / `CountAsync` / `UpdateAsync(filter,…)` /
  `DeleteAsync(filter)` all **throw**; `CreateAsync` / item `UpdateAsync` throw rather than stamp
  `Guid.Empty`.
- Strict mode, tenant in scope: reads/writes behave exactly as permissive-with-tenant.
- `Build(tenantMode: Strict)` produces a wrapper exhibiting the above with **no consumer subclass**.
- `tenantWrapperFactory` override is honoured when supplied.
- New tests live in the `Birko.Data.Tenant` + `Birko.Data.Composition` test projects (xUnit +
  FluentAssertions).

## Notes

- Coordinate the ordering fix (STORY-045) before finalizing tests — the two touch the same
  wrappers/builder and their tests share fixtures.
- CR-M175 (static `Tenant.Current` fallback) is the practical trigger for accidental fail-open;
  Strict mode should throw regardless of *which* context instance is in play when no tenant is set.
