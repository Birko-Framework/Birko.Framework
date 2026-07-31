---
id: TASK-115
parent: STORY-051
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H054]
---

# A nested `WithTenant` does not narrow reads inside an all-tenants scope

## Context

`../Birko.Data.Tenant/Models/TenantContext.cs:79` — **CONFIRMED**.

`WithTenant` / `WithTenantAsync` save and restore only `_currentTenantGuid` / `_currentTenantName`. They
**never touch `_allTenantsScope`**. Meanwhile `TenantStoreWrapper.cs:152` (async `:150`) computes the filter
as:

```csharp
effectiveTenant = IsAllTenantsScope ? null : CurrentTenantGuid;
```

The all-tenants flag is tested **first**. So the maintenance pattern the admin scope exists for —

```csharp
WithAllTenants(() => { foreach (var t in tenants) ctx.WithTenant(t, null, () => store.Read(…)); });
```

— returns **every tenant's rows on every iteration**. It is also the shape `TenantEventScopeAccessor`
produces for system events.

**The asymmetry is the tell.** Item writes test `HasTenant` first (`:184`), so writes *do* narrow while
reads do not. Two adjacent lines in the same class resolve the same question in opposite orders, which is
what makes this a defect rather than a policy: nobody chose it.

Rated P1 rather than P0: it over-reads rather than leaking across a trust boundary the caller has not already
crossed (the caller is inside an explicit all-tenants scope), and it does not corrupt data. But an admin
loop silently processing every tenant per iteration is an *n*-times-over data-processing bug, and it will
present as a mysterious performance problem long before anyone suspects correctness.

## Approach

Make the innermost explicit scope win. A nested `WithTenant` inside an all-tenants scope should narrow to
that tenant, and restore the all-tenants flag on exit — i.e. `WithTenant` saves and restores
`_allTenantsScope` along with the tenant, exactly as it already does for the Guid and name.

Then make the read filter and the write guard agree on precedence, and assert that agreement, so the two
lines cannot drift apart again. Read the write path's `HasTenant`-first ordering as the intended semantics
unless there is a reason to prefer the other.

Check `WithAllTenants` nested inside `WithTenant` too — the reverse nesting has the same question and the
answer should be symmetric.

## Acceptance criteria

- [ ] `WithAllTenants(() => WithTenant(t, () => store.Read(…)))` returns **only tenant *t*'s rows**
- [ ] Exiting the nested `WithTenant` restores the all-tenants scope — the next iteration is not left
      narrowed
- [ ] The per-tenant admin loop over *n* tenants reads each tenant's rows exactly once
- [ ] `WithTenant(t, () => WithAllTenants(() => …))` — the reverse nesting — has a defined, tested meaning
- [ ] Reads and item writes resolve tenant precedence identically, asserted in one test so the two lines
      cannot silently diverge again
- [ ] A plain `WithAllTenants` with no nesting still reads every tenant (the documented purpose is intact)
- [ ] `TenantEventScopeAccessor`'s system-event path is covered, since it produces this exact shape
- [ ] Regression tests in `Birko.Data.Tenant.Tests` with a multi-tenant fixture
- [ ] `/specs regen` for `tenant-isolation`, spec diff reviewed

## Out of scope

- `SH-H047` ([[TASK-114]]) and the `TenantSyncProvider` cluster ([[TASK-113]]). If all three land together,
  do this one **first** — a correct fetch predicate in TASK-113 can still be widened by a stale all-tenants
  flag, so fixing that ordering first makes the other two testable.
- `SH-H049` — downgraded in [[STORY-051]], not tasked.
- `SH-H053` (`AddEventTenantScope()` binding an unregistered `Tenant.Current`) — unverified, separate.

## Human test plan

N/A — covered by automated tests.
