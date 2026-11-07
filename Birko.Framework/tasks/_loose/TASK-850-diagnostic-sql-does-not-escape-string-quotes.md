---
id: TASK-850
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

# The diagnostic SQL does not escape a quote inside a string value

## Context

Flagged by the filter-expression-translation respec (2026-11-07). The old implementation behaved the same. The user
chose to file it.
- `DataBase.GetGeneratedQuery` (Birko.Data.SQL) writes each parameter's value in place of its name for error messages
  (`InitException` command text). It wraps string values in `'…'` without doubling a `'` inside.
- A value like `O'Brien` therefore renders `'O'Brien'`, which reads as malformed SQL.
- The text is never executed, so this is not an injection. But a reader may copy it into a SQL console, and it
  misrepresents the statement. Rule 20 names one producer of escaping for the whole framework.

## Acceptance criteria

- [ ] String values are escaped by the framework's one escaping producer (rule 20) when rendered into the diagnostic
      text: `O'Brien` → `'O''Brien'`.
- [ ] A test pins it, red before the fix. `GetGeneratedQuery` stays a single linear pass (TASK-832).

## Out of scope

- The executed SQL, which binds parameters.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
