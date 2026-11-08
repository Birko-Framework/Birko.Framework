---
id: TASK-873
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-869]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL string `==` follows the column collation, not C#'s ordinal equality

## Context

Found by TASK-869 (2026-11-08). `x.Name == "abc"` renders `Name = @p` and follows the collation: case-insensitive on
MySQL and SQL Server, accent-insensitive on MySQL, while C# `==` is ordinal. It is the same gap TASK-869 left on the
no-argument LIKE forms by the user's decision (option A). Since TASK-869, `string.Equals(x.Name, v,
StringComparison.Ordinal)` gives C#'s answer on every provider.

## Acceptance criteria

- [ ] Measure per provider (live): `==`, `!=`, `IN` lists of strings, on mixed-case and accented data, against C#.
- [ ] **Decide with the user:** keep `==` collation-following and document it (as TASK-869's option A), or make it
      ordinal. Record the decision.
- [ ] Tests per provider, red before any fix.

## Out of scope

- LIKE forms and explicit comparisons (TASK-869, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
