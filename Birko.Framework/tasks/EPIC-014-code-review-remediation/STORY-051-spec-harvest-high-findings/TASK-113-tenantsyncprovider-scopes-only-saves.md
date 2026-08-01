---
id: TASK-113
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H050, SH-H051, SH-H052]
---

# `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant

## Context

Three CONFIRMED findings in `../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs`, and **one root
cause**. `ApplyTenantFiltering` wraps only `CanSaveToLocal` / `CanSaveToRemote`; its own XML doc (line ~137)
says so outright — it *"only modifies save filters, not fetch predicates"*. Everything below follows from
that sentence being true.

**SH-H052 — reads, compares and previews cross tenants (`:298`).** `GetAllItemsAsync` is called with only
`LocalFetchPredicate` / `RemoteFetchPredicate`, which tenant filtering explicitly leaves untouched. Every
tenant's rows enter `localDict` / `remoteDict`; `AnalyzeItem` never consults `BelongsToTenant`. So
`PreviewAsync` under tenant *t* enumerates and version-hashes **other tenants' entities** and reports them as
ToCreate / ToUpdate / ToDelete — and those foreign Guids also reach the knowledge store.

**SH-H051 — deletes with no tenant check at all (`:475`).** `case SyncAction.Delete:` calls
`_localStore.DeleteAsync` / `_remoteStore.DeleteAsync` **without consulting any predicate**. Because the
fetches are unscoped, an item belonging to tenant *u* that resolves to Delete under ambient tenant *t* is
deleted. `Create`, `Update` and `ApplyConflictResolutionAsync` all *do* consult the predicates — so this
reads as an omission, not a design.

**SH-H050 — knowledge and saves keyed by different tenants (`:147`).** The knowledge / last-sync calls and
`CreateKnowledgeItem` use `GetTenantGuid(options)` (lines 280, 363, 813), while the save filters apply only
`if (_tenantContext.HasTenant …)` and filter on `CurrentTenantGuid`. So
`SyncAsync(new TenantSyncOptions { TenantGuid = u })` with **no ambient tenant** — the documented way to sync
one tenant from a background job — keys knowledge to *u* while **no save predicate exists at all**, writing
every tenant's items to both stores. With ambient *t* plus options *u*, knowledge is keyed *u* while writes
are filtered to *t*.

That last one is the worst of the three, because the configuration that triggers it is the one the docs
recommend.

## Approach

The tenant term has to move from the save predicates to the **fetch** predicates, so that no foreign entity
ever enters `localDict` / `remoteDict`. That makes the read, compare, preview, delete and knowledge paths
correct by construction instead of each needing its own guard — and it is the only version of this fix that
does not leave a fourth path to be found later.

Resolve the ambient-vs-options tenant precedence **once**, in one place, and use it everywhere. Today two
different answers (`GetTenantGuid(options)` and `_tenantContext.CurrentTenantGuid`) are live in the same run;
whichever wins, the mismatch is the bug. A run with neither an ambient tenant nor `options.TenantGuid` should
**fail loudly** rather than sync everything.

Keep the delete arm's predicate consultation even after the fetches are scoped — defence in depth on a
destructive path, and it is what `Create`/`Update` already do.

## Acceptance criteria

- [ ] Fetch predicates carry the tenant term; `localDict` / `remoteDict` contain **only** the target
      tenant's items, asserted directly
- [ ] `PreviewAsync` under tenant *t* reports zero actions for another tenant's rows
- [ ] An item belonging to tenant *u* is **never deleted** by a run scoped to tenant *t*, from either store
- [ ] The `SyncAction.Delete` arm consults the save predicate as `Create`/`Update` do
- [ ] Tenant resolution is single-sourced; `SyncAsync(options.TenantGuid = u)` with no ambient tenant scopes
      **writes** to *u*, not just knowledge — the documented background-job shape works correctly
- [ ] Ambient *t* + options *u* is either rejected or has one defined meaning, documented in the XML doc
- [ ] A run with no tenant from either source throws rather than syncing every tenant
- [ ] Knowledge items are keyed by the same tenant the writes were scoped to — asserted on the same run
- [ ] `ApplyTenantFiltering`'s XML doc no longer says fetch predicates are untouched (it will be false)
- [ ] Regression tests in `Birko.Data.Sync.Tenant.Tests` covering a two-tenant fixture for each row above
- [ ] `/specs regen` for `tenant-isolation` and `data-sync`, spec diffs reviewed

## Out of scope

- `SH-H047` (item-level write guard trusting the caller's `TenantGuid`) — [[TASK-114]].
- `SH-H054` (nested `WithTenant` not narrowing reads) — [[TASK-115]]. Related, and worth fixing **before**
  this one if both land in the same sweep: a correct fetch predicate can still be widened by a stale
  all-tenants flag.
- `SH-H053` (`AddEventTenantScope()` binding a `Tenant.Current` that `AddTenantContext*` never registers) —
  unverified, `Birko.EventBus.Tenant`, separate task.
- The seven unverified `data-sync` highs (`SH-H008`–`SH-H014`) — same file family, different defects.

## Human test plan

N/A — covered by automated tests. A two-tenant in-memory fixture reproduces every case; the cross-tenant
consequences are assertable without a live backend.
