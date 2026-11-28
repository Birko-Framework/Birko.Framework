---
id: TASK-1018
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1008, TASK-873]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A string comparison inside a SQL CASE WHEN follows the collation

## Context

Found by [[TASK-1008]]'s pass 2 (method calls, casts and string matching) on 2026-11-28; the user asked for it to be filed.

`RenderBoolFragment` (`Birko.Data.SQL/SQL/DataBase.cs`, about line 1959) renders a string comparison in a CASE WHEN
test (a conditional in a value fragment or SET) as a plain `=`, which follows the column collation. Everywhere else a
string `==` is exact since TASK-873, so `x.Code == "abc" ? 1 : 0` can pick the branch C# would not (on SQL Server
`ABC` and `abc ` match).

## Acceptance criteria

- [ ] Red first, live: a conditional whose test compares strings answers what C# answers on the four providers.
- [ ] The test goes through the provider's exact text equality, as a WHERE equality does.

## Human test plan

N/A — fully covered by automated tests.
