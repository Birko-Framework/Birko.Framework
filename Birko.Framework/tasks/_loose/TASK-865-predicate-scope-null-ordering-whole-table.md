---
id: TASK-865
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduced on InMemory and on every other `BoundedFilterGuard` user that can run locally (live where needed):
      Delete, Update(PropertyUpdate), Update(Action), with `!(x.N > nul)`, `!(x.N > nul && x.A > 3)`, the
      nested form and `x.A > 3 || !(x.N < nul && …)`. Record rows affected.
- [ ] `PredicateScope` treats an ordering against a null value as always false (lifted-operator semantics), so a
      destructive filter that reduces to always-true is refused. `==` / `!=` null are NOT constant.
- [ ] A shared contract (like TASK-861's `AlwaysFalseGroupContract`) runs on the non-SQL providers, red before the fix.

## Out of scope

- SQL providers (TASK-861, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
