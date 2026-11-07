---
id: TASK-853
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-850]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The diagnostic SQL renders NULL, numbers, dates, bools and bytes misleadingly

## Context

Found by TASK-850 (2026-11-07). This is a group of value kinds that `DataBase.GetGeneratedQuery` (Birko.Data.SQL)
renders misleadingly in the diagnostic text it builds for error messages. All of it is display-only; none of it is
executed. Grouped because one renderer fixes all of them.
- A null or `DBNull` with a string `DbType` renders `''`, which reads as an empty string. With any other `DbType` it
  renders nothing at all (`A = `).
- `DateTime`, `decimal` and `double` render unquoted in the current culture. On sk-SK: `1,5`, `7. 11. 2026 …`.
- A bool renders `True`, and a `byte[]` renders `System.Byte[]`.
- `SqliteParameter` defaults to `DbType.String`, so on SQLite every value is quoted, numbers included
  (`LIMIT '10'`).

## Acceptance criteria

- [ ] Rendering is by the value's CLR type, not by `DbType`. NULL is `NULL`; numbers and dates are rendered in the
      invariant culture (dates as ISO 8601, quoted); bools as `1` / `0` (or the provider's literal); `byte[]` as a hex
      literal, truncated with its length noted.
- [ ] Strings keep TASK-850's escaping, through `SqlLiteral.EscapeLiteral`.
- [ ] Tests for each kind, run under a non-invariant culture (e.g. sk-SK) to pin the culture fix, red before the fix.
      `GetGeneratedQuery` stays one linear pass.

## Out of scope

- String quote escaping (TASK-850, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
