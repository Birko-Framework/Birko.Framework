---
id: TASK-874
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

# SQL Server: a LIKE term past 4000 characters fails with Msg 8152

## Context

Found by TASK-869 (2026-11-08), measured on SQL Server 2022: a LIKE pattern longer than 4000 characters fails with
Msg 8152 ("String or binary data would be truncated"), also when bound as `nvarchar(max)`. TASK-869 refuses the
`Ordinal` / `OrdinalIgnoreCase` forms by name past the limit (`MSSqlValueFragmentDialect.MaxLikePatternLength`); the
no-argument `Contains` / `StartsWith` / `EndsWith` still fail with the server error at about 3998 characters. Loud,
not wrong rows.

## Acceptance criteria

- [ ] Reproduce (rule 54) on the no-argument forms.
- [ ] Refuse by name before anything runs, through the same limit, or translate another way if one is exact.
- [ ] Tests, red before the fix.

## Out of scope

- Other providers (MySQL and PostgreSQL handled 10,000-class patterns, measured).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
