---
id: TASK-865
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-861]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A non-SQL whole-table guard misses an ordering against null

## Context

Found by TASK-861 (2026-11-08), measured on InMemory.
- `int? nul = null; DeleteAsync(x => !(x.Score > nul))` and `!(x.Score > nul && x.A > 3)` deleted 6 of 6 rows, with
  no exception.
- In C#, `x.Score > null` is always false, so the negation is always true: the filter targets every row.
- `PredicateScope` (behind `BoundedFilterGuard`) already reduces the empty-`Contains` group shapes, but it does not
  know that an ordering (`<`, `<=`, `>`, `>=`) against a null constant or captured value is always false.
- Likely every store and wrapper using `BoundedFilterGuard` is affected: InMemory, JSON, XML, MongoDB, RavenDB,
  Cosmos DB, Elasticsearch, InfluxDB, and the decorators. TASK-861 fixed the SQL side.

## Acceptance criteria

- [x] Reproduced on InMemory and on every other `BoundedFilterGuard` user that can run locally (live where needed):
      Delete, Update(PropertyUpdate), Update(Action), with `!(x.N > nul)`, `!(x.N > nul && x.A > 3)`, the
      nested form and `x.A > 3 || !(x.N < nul && …)`. Record rows affected.
      → Measured 2026-11-08 with a shared contract: 9 every-row shapes × 6 operations (Delete, Update(PropertyUpdate),
        Update(Action), sync and async). No provider threw the guard's exception:
        - InMemory, JSON, XML: all 54 wrote 6 of 6.
        - MongoDB: 42 wrote 6 of 6, the rest 3 or 4.
        - RavenDB: mostly 3 of 6, some 0, 4 or 5.
        - Cosmos DB: between 0 and 5 of 6.
        - Elasticsearch: all 54 refused with `NotSupportedException` and wrote nothing.
        - InfluxDB: no live fixture (token auth only); it inherits the guard from the base bulk stores.
- [x] `PredicateScope` treats an ordering against a null value as always false (lifted-operator semantics), so a
      destructive filter that reduces to always-true is refused. `==` / `!=` null are NOT constant.
      → A new arm in `PredicateScope.IsAlwaysFalse` (`IsNullOrdering`) matches a lifted, bool-typed ordering whose
        operand is a null constant after normalization. That covers a captured value, `(int?)null`, either side,
        and lifted decimal and DateTime.
      → `==` / `!=` are not affected, and neither is a user-defined operator over nullable operands. Reads unchanged.
- [x] A shared contract (like TASK-861's `AlwaysFalseGroupContract`) runs on the non-SQL providers, red before the fix.
      → New `tests/Shared/StoreContract/NullOrderingScopeContract.cs`, 84 cases per provider: 54 must be refused, 30
        controls must touch exactly the rows C# matches. It runs in InMemory, JSON, XML, MongoDB, RavenDB, Cosmos DB
        and Elasticsearch, plus 12 unit tests in `PredicateScopeNullOrderingTests`.
      → Red proof: with `PredicateScope.cs` at HEAD, 54 of 84 failed per provider, and 6 of 12 unit tests.
      → 23 importer suites green, live where applicable, including Data.Core, every non-SQL store, Patterns, Tenant,
        Sync, Repositories, Data.Tests, Aggregates, SqLite and MySQL. About 100 more suites import Birko.Data.Core;
        they were not run, and CI will cover them.
      → **Found, filed:** TASK-867 (P1). RavenDB and Cosmos DB translate a null ordering differently from C#, on reads
        and on writes the guard allows. Elasticsearch refuses it. These are pinned as `KnownTranslationGap` entries
        in the contract; each is an inverted test (rule 56).

## Out of scope

- SQL providers (TASK-861, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
