---
id: TASK-1012
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-1002, TASK-861, TASK-308]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL parser drops a NOT around an always-true group, so a read returns every row

## Context

Found by [[TASK-1002]]'s corpus on 2026-11-27. The user asked for it to be filed and fixed in the same run.

`DataBase.ParseConditionExpression` returns `Array.Empty` (no condition, every row) for an AND/OR group that reduces to
always-true. It did so whatever the `parent` carried, and the enclosing `Not` had put its `IsNot` on that parent. So
`!(x.A > 3 || true)`, which matches no row, parsed to "no condition".

The always-false branches already passed `parent` to `MakeFalseCondition`, which is why their negation survived
([[TASK-861]]). `IsConstantBoolCondition` had the same blind spot: an empty condition counted as constant `true` even
under a `NOT`.

**Measured on SQLite before the fix, against C#:**

| Operation | SQLite | C# |
|---|---|---|
| Read | 6 of 6 rows | 0 |
| Count | 6 | 0 |
| Delete, Update | refused (`WholeTableWriteException`) | — |

No data was changed, but every read was wrong. In a composite, `A > 4 || !(A > 3 || on)` read every row instead of rows
5 and 6, and its writes were refused.

## Acceptance criteria

- [x] Red first: the corpus entries `!(A > 3 || true)`, `!(A > 3 || on)` and `A > 0 || !(A > 3 || true)` fail on the SQL
  reduction. `A > 4 || !(A > 3 || on)` in `AlwaysFalseGroupContract` fails live: 7 checks on SQLite, read and writes.
- [x] A new `AlwaysTrue(parent)` returns no condition, or, under a negated parent, the always-false leaf with the NOT
  consumed. It is used by every always-true return of the AND/OR branch and by the lambda-level `x => true`.
  `IsConstantBoolCondition` honours `IsNot` on an empty condition.
- [x] The corpus, and `AlwaysFalseGroupContract` live on all four SQL providers, pass.

## Evidence 2026-11-27

| Test | Before the fix | After the fix |
|---|---|---|
| Corpus | 6 checks fail for these shapes | pass |
| `AlwaysFalseGroupContract` | SQLite 168/175, PostgreSQL the same (7 failures, all the composite) | 175/175 on SQLite, MySQL, PostgreSQL and SQL Server |

## Human test plan

N/A — fully covered by automated tests.
