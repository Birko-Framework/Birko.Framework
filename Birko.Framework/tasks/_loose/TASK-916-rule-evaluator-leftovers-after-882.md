---
id: TASK-916
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-882]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Rules: remaining two-answer shapes after TASK-882

## Context

Found by TASK-882 (2026-11-11), not fixed there:
- On a NULL member, `RuleEvaluator` has no declared type, so it cannot tell a rule value is unconvertible:
  `OptState NotEqual "Bogus"` is true there, while the compiled rule matches nothing.
- The evaluator orders strings culturally, the compiled rule matches nothing (SH-M291).
- `Name Equal 5` against `"05"`: the evaluator compares numerically, the compiled rule compares the text `"5"`.
- Guid ordering on SQL Server (`uniqueidentifier` byte order) vs C# not measured.

## Acceptance criteria

- [ ] Measure each against the C# lambda (rule 54).
- [ ] One answer per rule wherever it runs, or refused by name; tests red before.

## Out of scope

- Interior `%` (TASK-915).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
