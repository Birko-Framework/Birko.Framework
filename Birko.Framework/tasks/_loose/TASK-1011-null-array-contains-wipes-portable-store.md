---
id: TASK-1011
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-1002, TASK-215, TASK-218, TASK-865]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A negated Contains over a null array wipes every row on a portable store

## Context

Found by [[TASK-1002]]'s corpus on 2026-11-27. The user asked for it to be filed and fixed in the same run.

C# 14 binds an array's `Contains` to `MemoryExtensions.Contains`. The array arrives through an implicit conversion to
`ReadOnlySpan<T>`, and a **null array becomes an empty span**. So `x => !none.Contains(x.Amount)` with a null `none` is
true of every row in C#, and a compiled delegate evaluates it that way.

`PredicateScope.IsEmptySetContains` returned false for any null collection. Its comment assumed `null.Contains(x)`
throws, which holds for `Enumerable.Contains` and an instance `List<T>.Contains`, not for the span binding. So the
whole-table guard ([[TASK-215]]) let the filter through on every portable store.

**Measured on InMemory before the fix:**
- `Delete(x => !none.Contains(x.A))` left **0 of 6** rows, with no exception;
- `Update(…, Action)` marked 6 of 6.

The SQL stores were not affected: the parser reads a null list as an empty `IN`, and the guard refuses it.

## Acceptance criteria

- [x] Red first: the corpus entry `!none.Contains(A) (null array)` fails on the expression reduction.
  `PortableUnboundedFilterGuardTests.UnboundedFilter_AsANullArray_IsRefusedToo` fails too.
- [x] `IsEmptySetContains` treats a null collection as empty when the `Contains` is span-bound (`MemoryExtensions`), and
  as before (not empty) otherwise.
- [x] Delete and Update with that filter are refused on InMemory, sync and async. A read still returns every row, as C#
  does. The shared `AlwaysFalseGroupContract` holds the shape too, so all four SQL providers are checked live.

## Evidence 2026-11-27

| Test | Before the fix | After the fix |
|---|---|---|
| Corpus (`Task1002ScopeReductionCorpusTests`) | the expression side and the agreement check fail | pass |
| InMemory guard tests | 16/17 | 17/17 |
| `AlwaysFalseGroupContract`, SQL providers | already passing (SQL had it right) | 175/175 on each of the four, live |

The `List<T>` case is pinned separately (`A_null_list_throws_in_csharp_and_is_refused_by_sql`). It throws in C#, so a
portable store writes nothing, and the SQL guard refuses it.

## Human test plan

N/A — fully covered by automated tests.
