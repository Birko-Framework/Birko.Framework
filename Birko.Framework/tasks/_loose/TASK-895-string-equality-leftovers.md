---
id: TASK-895
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-873]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# String equality: leftovers after TASK-873

## Context

Found by TASK-873 (2026-11-09):
- **TASK-833's planner still refuses a string IN list past the parameter limit**, because string comparison followed
  the collation. Since TASK-873 `==` and IN are exact, so splitting would be safe. That changes TASK-833's recorded
  decision: **decide with the user.**
- **Rules (`RuleConditionConverter`) and hand-built `Condition`s keep `=` and the collation.** Should rules be exact
  too? **Decide with the user.**
- `string.Equals(x.Col, v, Ordinal)` (TASK-869's TextEqual) renders a BIN2 LIKE: no index seek, and SQL Server's 4000-
  character LIKE limit. It could go through `OrdinalTextEquality`.
- `string.Equals(x.A, x.B, Ordinal)` over two columns is refused, while `x.A == x.B` is now exact.
- View joins built from view attributes (`ViewSelectSqlBuilder.BuildViewJoinConditionSql`) render plain `=` on string
  keys and follow the collation.
- `utf8mb4_0900_bin` does not exist on MySQL 5.7 or MariaDB (string equality would fail there loudly).
- PostgreSQL `CHAR(n)` (bpchar): `::text` strips padding while C# reads it padded; the framework never creates them.

## Acceptance criteria

- [ ] Get the user's decisions on the two items above, and record them.
- [ ] Implement the decided items and the consistency items (TextEqual through `OrdinalTextEquality`, two-column
      `Equals(Ordinal)`, view join keys), or record why not; tests red before each fix.

## Out of scope

- `==` / IN in lambda filters (TASK-873, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
