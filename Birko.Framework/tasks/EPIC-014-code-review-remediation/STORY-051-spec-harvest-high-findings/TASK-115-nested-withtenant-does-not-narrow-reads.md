---
id: TASK-115
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: 595d99b (Birko.Data.Tenant) / 1a2a689 (Birko.Data.Tenant.Tests) / 59a3301 (Birko.EventBus.Tenant.Tests)
github-issue: null
jira-key: null
findings: [SH-H054]
picked-by: fix-next
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

- [x] `WithAllTenants(() => WithTenant(t, () => store.Read(…)))` returns **only tenant *t*'s rows**
- [x] Exiting the nested `WithTenant` restores the all-tenants scope — the next iteration is not left
      narrowed
- [x] The per-tenant admin loop over *n* tenants reads each tenant's rows exactly once
- [x] `WithTenant(t, () => WithAllTenants(() => …))` — the reverse nesting — has a defined, tested meaning
- [x] Reads and item writes resolve **nested-scope** precedence identically — `WithTenant` innermost
      narrows both — asserted in one test so the two lines cannot silently diverge again

**This criterion was rescoped at step 3.** As written ("reads and item writes resolve tenant precedence
identically") it could not be satisfied without changing `TenantFilter`'s `IsAllTenantsScope`-first
ordering — which would settle [[TASK-127]], an open decision, as a side effect of a bug fix. The two cases
are separable and only one is a defect:

- **`WithTenant` innermost** (`WithAllTenants(() => WithTenant(t, ...))`) — reads did not narrow, writes
  did. Nobody chose that; it is the defect, and it is fixed here. Both now narrow.
- **`WithAllTenants` innermost** (ambient tenant, or `WithTenant(t, () => WithAllTenants(...))`) — reads
  widen, item writes still do not. That disagreement is untouched by this fix and is [[TASK-127]]'s
  decision. A test pins the current behaviour so the decision has a baseline to move from, and is named
  in the test as a baseline rather than as desired behaviour.
- [x] A plain `WithAllTenants` with no nesting still reads every tenant (the documented purpose is intact)
- [x] `TenantEventScopeAccessor`'s system-event path is covered, since it produces this exact shape
- [x] Regression tests in `Birko.Data.Tenant.Tests` with a multi-tenant fixture
- [x] `/specs regen` for `tenant-isolation`, spec diff reviewed

## Out of scope

- `SH-H047` ([[TASK-114]]) and the `TenantSyncProvider` cluster ([[TASK-113]]). If all three land together,
  do this one **first** — a correct fetch predicate in TASK-113 can still be widened by a stale all-tenants
  flag, so fixing that ordering first makes the other two testable.
- `SH-H049` — downgraded in [[STORY-051]], not tasked.
- `SH-H053` (`AddEventTenantScope()` binding an unregistered `Tenant.Current`) — unverified, separate.

## Human test plan

N/A — covered by automated tests.

## Progress log

- step 2 — picked; ranked above TASK-113 because TASK-113's own Out of scope names this a prerequisite ("a correct fetch predicate can still be widened by a stale all-tenants flag"), and above TASK-126 because it unblocks the highest-blast-radius item in the pool. Flagged at pick time: the last acceptance criterion, read literally, would settle [[TASK-127]]'s open decision as a side effect — to be rescoped at step 3, not honoured as written.
- step 3 — verified: HELD exactly. All four `WithTenant` overloads save/restore only `_currentTenantGuid`/`_currentTenantName`; all four `WithAllTenants` overloads correctly save/restore `_allTenantsScope`. `TenantContext` is the only implementation of the scope-bearing interface (`Birko.Security.AspNetCore.TenantContextAdapter` implements a narrower ASP.NET `ITenantContext` with no scope methods). Rescoped the last acceptance criterion — see the note under Acceptance criteria.
- step 4 — fix in Birko.Data.Tenant/Models/{TenantContext.cs (all 4 WithTenant overloads), ITenantContext.cs (contract doc)}; tests in Birko.Data.Tenant.Tests/NestedTenantScopeTests.cs; suite 59/59 green
- step 5 — reverted fix: 5/59 failed. Fix-dependent = A_nested_WithTenant_narrows_reads_inside_an_all_tenants_scope, A_nested_WithTenantAsync_narrows_reads_inside_an_all_tenants_scope, The_per_tenant_admin_loop_reads_each_tenants_rows_exactly_once, All_four_WithTenant_overloads_suspend_the_all_tenants_scope, Reads_and_item_writes_agree_when_WithTenant_is_the_innermost_scope. The other 4 new tests pass under revert, in two distinct categories, and neither is evidence: (a) Exiting_a_nested_WithTenant_restores_the_all_tenants_scope and The_scope_is_restored_even_when_the_body_throws are **regression guards on the fix's own failure mode** — under the old code the flag was never cleared, so "restored" is indistinguishable from "untouched"; they fail against a fix that clears without restoring, which is the mistake worth guarding; (b) the two BASELINE_* tests pin TASK-127's unchanged behaviour on purpose. All 50 pre-existing tests pass in both states.
- step 5b — consumer suites: Composition 21, Sync.Tenant 17, EventBus.Tenant 8, Security.AspNetCore 77 green. Birko.Data.SQL.Tests reported 2/325 failing on the first run after the source edit and has been 325/325 on 7 subsequent runs including the identical 5-suite loop. Attributed to build contention on the first rebuild, **inferred not observed** — the failing test names were not captured. Supporting evidence: no file in that suite references WithTenant / WithAllTenants / TenantContext / IsAllTenantsScope, so none of its tests can reach the changed code path.
- step 6 — respecced `tenant-isolation`; requirements changed: "Scoped tenant execution restores the previous tenant" (SHALL now covers the all-tenants flag; 4 scenarios added — nested narrowing, the admin loop, restore-on-throw, and read/write agreement on nested precedence) and "Explicit all-tenants (admin) scope" (a scenario added recording the innermost-`WithAllTenants` disagreement as an open decision rather than a contract)
- step 7 — committed 595d99b (Birko.Data.Tenant) / 1a2a689 (Birko.Data.Tenant.Tests) / 59a3301 (Birko.EventBus.Tenant.Tests) / aggregator below

## Outcome

**What was wrong.** All four `WithTenant` / `WithTenantAsync` overloads saved and restored the tenant guid
and name but never `_allTenantsScope`, while `TenantFilter` resolves the read predicate as
`IsAllTenantsScope ? null : CurrentTenantGuid` — testing the flag first. So a nested `WithTenant` did not
narrow reads at all, and the per-tenant admin loop these scopes exist for returned **every** tenant's rows
on **every** iteration: an *n*-times-over data-processing bug that presents as a performance mystery long
before anyone suspects correctness. Item writes test `HasTenant` first, so writes narrowed while reads did
not — the asymmetry is what made this a defect rather than a policy.

**The fix.** Each `WithTenant` overload clears the flag for its duration and restores the captured value in
the same `finally` as the guid and name. The rationale lives on the `_allTenantsScope` field rather than
being copy-pasted into four methods, because the next scope method added has to make the same decision.

**Step-5 split.** Reverting only `TenantContext.cs`: **5 of 59 failed** in `Birko.Data.Tenant.Tests`, plus
**1 of 10** in `Birko.EventBus.Tenant.Tests`.

- Fix-dependent (6): `A_nested_WithTenant_narrows_reads_inside_an_all_tenants_scope`,
  `A_nested_WithTenantAsync_narrows_reads_inside_an_all_tenants_scope`,
  `The_per_tenant_admin_loop_reads_each_tenants_rows_exactly_once`,
  `All_four_WithTenant_overloads_suspend_the_all_tenants_scope`,
  `Reads_and_item_writes_agree_when_WithTenant_is_the_innermost_scope`,
  `Dispatch_inside_an_all_tenants_drain_still_narrows_to_the_events_tenant`.
- **Regression guards on the fix's own failure mode, not evidence** (3):
  `Exiting_a_nested_WithTenant_restores_the_all_tenants_scope`, `The_scope_is_restored_even_when_the_body_throws`,
  `An_all_tenants_drain_is_restored_between_dispatches`. Under the old code the flag was never cleared, so
  "restored" is indistinguishable from "untouched" and these cannot fail. They **do** fail against a fix
  that clears without restoring — which would leave every loop iteration after the first narrowed — so they
  are worth keeping, but they prove nothing about the defect.
- **Deliberate baselines for [[TASK-127]]** (2): `BASELINE_with_WithAllTenants_innermost_reads_widen_while_item_writes_do_not`,
  `BASELINE_an_ambient_tenant_plus_WithAllTenants_still_widens_reads`. Named and commented so a future
  reader does not take them as endorsing the asymmetry.
- All 50 pre-existing `Birko.Data.Tenant.Tests` pass in both states, including TASK-114's `WithAllTenants`
  cases. Consumer suites green: Composition 21, Sync.Tenant 17, EventBus.Tenant 10, Security.AspNetCore 77.

**Judgement calls.**

1. **The scope question was deliberately only half-settled.** The last acceptance criterion, as written
   ("reads and item writes resolve tenant precedence identically"), could not be met without changing
   `TenantFilter`'s `IsAllTenantsScope`-first ordering — which would have settled [[TASK-127]] as a side
   effect of a bug fix, the thing `CLAUDE.md` explicitly warns against. The two cases are separable and only
   one is a defect: `WithTenant` innermost (nobody chose the behaviour — fixed) versus `WithAllTenants`
   innermost (a real design question — left, with a pinned baseline). The criterion was rescoped at step 3,
   before any code was written, and the reasoning is recorded under Acceptance criteria.
2. **`WithTenant` clears rather than the filter re-ordering.** Clearing keeps the change inside the scope
   methods, where "save and restore the scope" already is the contract; re-ordering `TenantFilter` would
   have changed the meaning of every existing `WithAllTenants` call site at once.
3. **The `TenantEventScopeAccessor` row was covered rather than treated as proxied.** The accessor is a thin
   delegation to `WithTenantAsync`, so the TenantContext-level test arguably covers it — but the acceptance
   row names the path, and a delegation can be changed without that test noticing. One fix-dependent test
   added in a fourth repo.

**Flagged, not fixed.**

- **[[TASK-127]] is now more urgent, not less.** This fix makes nested `WithTenant` narrow, which sharpens
  the contrast with the still-widening innermost-`WithAllTenants` case. Two adjacent behaviours now differ
  for a reason that is written down but not decided.
- **A 2/325 failure in `Birko.Data.SQL.Tests` on the first run after the source edit** was not reproduced in
  7 subsequent runs, including the identical 5-suite loop. Attributed to build contention during the
  first rebuild — **inferred, not observed**, since the failing test names were not captured. Supporting
  evidence: no file in that suite references `WithTenant` / `WithAllTenants` / `TenantContext` /
  `IsAllTenantsScope`, so none of its tests can reach the changed code path. If it recurs, it is not this
  change.
- **[[TASK-113]] is now unblocked** — it named this task a prerequisite ("a correct fetch predicate can
  still be widened by a stale all-tenants flag").
