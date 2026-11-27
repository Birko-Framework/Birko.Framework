---
id: TASK-1002
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-861, TASK-865, TASK-308, TASK-1011, TASK-1012]
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

- [x] One shared corpus of predicates, each labelled always-true, always-false or row-dependent. It covers at least:
  - empty and non-empty `Contains`;
  - a negated always-false group under AND and under OR;
  - an ordering against a null captured value, both ways round and negated;
  - `x => true` / `_ => false`;
  - nested negations;
  - a null collection (not claimed empty).
- [x] A test runs every entry through `PredicateScope` and through `ParseConditionExpression` + `WouldTargetEveryRow`.
  It asserts both agree with the label for refusal: always-true is refused, and the rest are not, except the explicit
  `x => true` door.
- [x] Each entry either passes on both reductions or is a recorded, deliberate exception, with the reason in the test.
- [x] Adding a form to one reduction without the other fails the test. Shown by removing one known case from one side
  and watching it go red.

## Evidence 2026-11-27

**The corpus.** `tests/Birko.Data.SQL.Tests/Task1002ScopeReductionCorpusTests.cs` holds 40 labelled shapes: every row, no
row, depends on the row. Three theories ask each shape of `PredicateScope.ReducesToAllRows`, of
`ParseConditionExpression` + `WouldTargetEveryRow`, and whether the two agree.

**On first run it found two production bugs**, both filed and fixed in this run (the user's choice):
- **[[TASK-1011]] (P1), the expression reduction.** A null array under C# 14's span-bound `Contains` is an empty
  span, so `!none.Contains(A)` is every row. `PredicateScope` assumed `null.Contains` throws, and InMemory's
  `Delete` removed 6 of 6 rows with no exception.
- **[[TASK-1012]] (P2), the SQL reduction.** The parser dropped a NOT around an always-true group, so
  `!(A > 3 || true)`, which matches no row, read every row on SQLite.

**Red, then green.** On the code before the fix, 8 checks failed:
- the SQL side and the agreement check, for three negated always-true shapes;
- the expression side and the agreement check, for the null array.

That is the last criterion shown twice over: one reduction lacking a form the other has fails here. After the fix:
121/121.

**One recorded difference.** A null `List<T>`: its instance `Contains` throws in C#, so a portable store writes
nothing, while the SQL parser reads it as empty and the guard refuses the write. Neither side writes every row. It is
pinned in its own test, with that reason.

**Also:** `DataBase.RequireBoundedFilter`'s remarks still said the expression guard does not know an ordering against
null. That stopped being true at TASK-865, and the remarks now say so and point here. The merge of the two reductions
stays out of scope, as filed.

## Out of scope

- Merging the two reductions into one. TASK-861 decided SQL judges its own tree, so changing that needs a decision.
- Expressions the SQL parser refuses (`NotSupportedException`, [[TASK-308]]). The corpus may hold them, but only to
  assert that refusal.

## Human test plan

N/A — fully covered by automated tests.
