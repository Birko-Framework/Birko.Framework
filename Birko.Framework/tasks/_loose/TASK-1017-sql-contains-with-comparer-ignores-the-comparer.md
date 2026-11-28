---
id: TASK-1017
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1008, TASK-873, TASK-869]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL Contains with a StringComparer ignores the comparer and matches case-sensitively

## Context

Found by [[TASK-1008]]'s pass 2 (method calls, casts and string matching) on 2026-11-28; the user asked for it to be filed.

`names.Contains(x.Name, StringComparer.OrdinalIgnoreCase)` in a SQL filter: `IsNonOperandArgument`
(`Birko.Data.SQL/SQL/DataBase.cs`, about line 1317) skips the comparer, then `MarkOrdinalText` (about line 2259) marks
the string `IN` exact ordinal (TASK-873). So the list matches case-sensitively where C# does not: `"ALPHA"` is not
matched by `"alpha"`. Reads return too few rows, and a filtered Update or Delete touches too few, with no exception.
The comment on `IsNonOperandArgument` still says the comparer's semantics are delegated to the column collation,
which stopped being true at TASK-873. The only test (`tests/Birko.Data.SQL.Tests/Connectors/EnumParameterBindingTests.cs`,
`ContainsWithExplicitComparer_StillTranslatesToInOverTheColumn`) checks that an `IN` is produced, not which rows match.

## Acceptance criteria

- [ ] Red first, live on the four SQL providers: `Contains(x.Name, StringComparer.OrdinalIgnoreCase)` returns the rows
  C# returns (measure the miss before the fix).
- [ ] `StringComparer.Ordinal` is the exact `IN`; `OrdinalIgnoreCase` is translated exactly (as TASK-869's
  `OrdinalIgnoreCase` string calls are) or refused; any other comparer (culture, custom) is refused with
  `NotSupportedException` naming it.
- [ ] The `IsNonOperandArgument` comment and the existing test say what the code does.

## Human test plan

N/A — fully covered by automated tests.
