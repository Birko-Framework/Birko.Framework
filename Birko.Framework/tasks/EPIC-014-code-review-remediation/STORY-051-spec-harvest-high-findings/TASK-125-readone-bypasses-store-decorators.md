---
id: TASK-125
parent: STORY-051
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-114]
pr: null
github-issue: null
jira-key: null
findings: [SH-H036]
---

# `ReadOne` queries the connector directly, bypassing every store decorator

## Context

`../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs:20` — **CONFIRMED by hand (2026-07-31)**.

```csharp
foreach (TModel item in repository.Connector.Select<TModel, object>(typeof(TModel), filter?.Filter(), …))
```

`Connector` resolves through `GetUnwrappedStore` (`Birko.Data.Stores/StoreExtensions.cs:16`), which walks
`IStoreWrapper.GetInnerStore()` in a loop down to the **innermost** store. So every decorator is skipped —
`TenantStoreWrapper.Read` is what injects `ModelByTenant`, and going straight to the connector applies no
tenant predicate at all. `ReadOne` therefore returns the first matching row from **any** tenant. Soft-delete,
localization and audit wrappers are dropped by the same call.

This is the **read-side sibling of [[TASK-114]]**, which fixed the write side. Same root shape: a path that
looks tenant-safe from the outside and silently isn't.

## The finding's scope is wrong — correct it before fixing

`SH-H036` reads as though the bypass is one method. Checking `GetUnwrappedStore` first says otherwise:
**70 call sites across 21 data projects**. That is *not* a 70-site fix, and treating it as one would balloon
this task into a redesign:

- **69 of the 70 are the intentional escape hatch** — `XStore => Store?.GetUnwrappedStore<…>()` properties
  (`CosmosStore`, `ElasticSearchStore`, `MongoStore`, `RavenStore`, …) that exist so callers can reach
  backend-native features (bulk APIs, native queries) a portable store cannot express. Exposing them is a
  documented capability, not a defect.
- **`ReadOne` is the only general-purpose *read helper* that silently takes that path.** It is the sole
  `Extensions/` file of its kind across the ViewModel projects. That is the defect: an API whose signature
  promises a normal filtered read and whose implementation opts out of the decorator chain.

So the fix is one method. The escape-hatch properties are a **separate, smaller documentation question**
(see Out of scope).

## Reachability — state it honestly in the fix

Inside the framework, `ReadOne` has **only test callers**
(`Birko.Data.SQL.ViewModel.Tests/SyncDataBaseRepositoryTests.cs:135,148`). It is a **public extension method
shipped to consumers**, so consumer reachability is unknown and it reads as safe from the outside. That
lowers the urgency, not the correctness: do not downgrade this to "unused".

## Approach

`ReadOne` should go through the repository's **store** (and therefore its decorator chain) rather than its
connector. The decision to settle first: whether the extension can be expressed on the portable store API at
all, or whether it exists *because* it needed the connector's `Select` projection.

- If it can use the store — route it through the wrapped store and delete the connector path.
- If the connector projection is genuinely needed — the tenant (and other decorator) predicates must be
  composed onto the filter before the `Select`, which means the extension can no longer be
  decorator-agnostic. Prefer the first option; a helper that has to re-implement every decorator will drift.

**Do not "fix" this by removing `GetUnwrappedStore`.** It is load-bearing for 69 legitimate call sites.

## Acceptance criteria

- [ ] `ReadOne` under an ambient tenant *t* does **not** return a row belonging to tenant *u* — asserted with
      a two-tenant fixture over a tenant-wrapped repository
- [ ] The same read still returns the caller's own row unchanged (no over-filtering)
- [ ] Soft-delete / localization / audit decorators are likewise not skipped — or, if only tenant is
      addressed, the remaining gaps are named explicitly in the Outcome rather than left implied
- [ ] Existing `SyncDataBaseRepositoryTests` cases still pass, or their change is justified
- [ ] The 69 escape-hatch `GetUnwrappedStore` properties are left alone and that decision is recorded
- [ ] Regression tests in `Birko.Data.SQL.ViewModel.Tests`
- [ ] `/specs regen` for `repository-contract` (and `tenant-isolation` if its scenarios move), spec diff reviewed

## Out of scope

- **Documenting the escape hatch.** The `XStore` / `Connector` properties strip every decorator and nothing
  says so at the call site. Worth a doc note on `GetUnwrappedStore` and the CLAUDE.md files, but it is a
  documentation task, not this defect — file separately if it is not picked up here.
- `SH-H019` ([[TASK-126]]) — different subsystem, same family.
- The `WithAllTenants` + ambient-tenant contradiction ([[TASK-127]]) — a decision, not a defect.

## Human test plan

N/A — covered by automated tests. A two-tenant fixture asserts the cross-tenant read is not returned.
