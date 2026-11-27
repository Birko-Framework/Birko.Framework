---
id: TASK-1002
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-861, TASK-865, TASK-308]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The whole-table scope rule has two reductions; pin them to one test corpus

## Context

Found at the 2026-11-27 `/specs regen` of `bulk-filter-operations`; the user asked for it to be filed.

Since [[TASK-861]], two reductions answer "would this destructive filter reach every row?":

- **`PredicateScope`** (`Birko.Data.Core/Expressions/PredicateScope.cs`, behind `BoundedFilterGuard`) reduces the LINQ
  expression. The portable bulk stores (InMemory, JSON, XML, MongoDB, RavenDB, Cosmos DB, Elasticsearch, InfluxDB)
  use it.
- **`WouldTargetEveryRow`** (`AbstractConnectorBase.ConstantTruth`) reduces the parsed SQL condition tree. The SQL
  connector funnels and, through `DataBase.RequireBoundedFilter`, the SQL bulk stores' action path use it.

TASK-861 chose this split on purpose: SQL decides on the tree it will run. Today both know the always-false forms:
- an empty `Contains`;
- `_ => false`;
- an ordering against null ([[TASK-865]] for `PredicateScope`, TASK-861 for the tree).

But rule 53 applies: two implementations drift. The next always-false or always-true form has to be taught to both,
and a form taught to only one gives a whole-table write on the other family, with no exception. That's how TASK-861
happened: `Update(x => !(x.Score > nul), action)` rewrote 6 of 6 rows on SQL while `Delete` refused it.

## Acceptance criteria

- [ ] One shared corpus of predicates, each labelled always-true, always-false or row-dependent. It covers at least:
  - empty and non-empty `Contains`;
  - a negated always-false group under AND and under OR;
  - an ordering against a null captured value, both ways round and negated;
  - `x => true` / `_ => false`;
  - nested negations;
  - a null collection (not claimed empty).
- [ ] A test runs every entry through `PredicateScope` and through `ParseConditionExpression` + `WouldTargetEveryRow`.
  It asserts both agree with the label for refusal: always-true is refused, and the rest are not, except the explicit
  `x => true` door.
- [ ] Each entry either passes on both reductions or is a recorded, deliberate exception, with the reason in the test.
- [ ] Adding a form to one reduction without the other fails the test. Shown by removing one known case from one side
  and watching it go red.

## Out of scope

- Merging the two reductions into one. TASK-861 decided SQL judges its own tree, so changing that needs a decision.
- Expressions the SQL parser refuses (`NotSupportedException`, [[TASK-308]]). The corpus may hold them, but only to
  assert that refusal.

## Human test plan

N/A — fully covered by automated tests.
