---
id: TASK-855
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `PropertyUpdate` on a column renamed with fluent `HasColumnName` writes the C# name

## Context

Found by TASK-849 (2026-11-07), measured on SQLite. `DataBase.GetFieldFromLambda` ignores the fluent column name, so
`PropertyUpdate.Set(x => x.Points, …)` on a property mapped `HasColumnName("Pts")` renders `SET Points = …` and fails
with "no such column". It fails loudly. It is the "two producers for one name" shape that rule 48 describes: the
fluent mapping and the lambda resolver each define the column's identity.

## Acceptance criteria

- [ ] Reproduced on SQLite in-process: Set, Increment and Compute on a renamed column fail today. Record the error.
- [ ] The update path resolves the column through the same producer the table build uses, so renamed columns work for
      every `PropertyUpdate` kind (rules 16 and 48).
- [ ] Regression tests red before the fix; the Birko.Data.SQL importer suites stay green.

## Out of scope

- Nested members (TASK-845).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
