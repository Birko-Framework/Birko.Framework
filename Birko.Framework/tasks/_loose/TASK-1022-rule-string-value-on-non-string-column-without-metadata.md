---
id: TASK-1022
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
related: [TASK-1008, TASK-895, TASK-882]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A string rule value on a non-string column may break on a path with no table metadata

## Context

Found by [[TASK-1008]]'s pass 4 (rules) on 2026-11-28, unverified; the user asked for it to be filed.

`MarkRuleOrdinalText` (`Birko.Data.SQL/SQL/DataBase_RuleField.cs`, about line 410) marks a rule's `Equal` / `NotEqual` /
`In` / `NotIn` exact ordinal when every value is a string and no column type is known. The doc comment says so on
purpose ("taken as one when nothing says otherwise"). The type-less overloads have no entity type, and a `[View]` type
probably has no table `TryLoadTable` finds. On that path `Equal "5"` on an `int` column renders the exact text form; on
SQL Server that is `(col + N'|') COLLATE …`, and `int + N'|'` should fail with a conversion error, where a plain `=`
converted implicitly. Not run.

## Acceptance criteria

- [ ] Measure first, live on the four SQL providers: a string-valued `Equal` / `In` rule on an integer and on a Guid column,
  through the type-less overload and through a view type. Record each result against the compiled rule.
- [ ] Nothing wrong → close with the measurement, pinned as a test.
- [ ] Something wrong → red first, then fix (resolve the column type where it is knowable, refuse where it is not), and the
  rule contracts pass.

## Human test plan

N/A — fully covered by automated tests.
