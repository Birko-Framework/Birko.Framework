---
id: TASK-1021
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
related: [TASK-1008, TASK-863, TASK-860]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A boolean compared with a boolean in a SQL SET value may not answer what C# answers

## Context

Found by [[TASK-1008]]'s pass 3 (booleans and parameters) on 2026-11-28, unverified; the user asked for it to be filed.

TASK-863 rewrites a boolean compared with a boolean (`e == false` → `!e`, `x.Opt.Value` → `x.Opt == true`) before
the condition parser sees a filter (`RewriteBooleanComparisons`, `Birko.Data.SQL/SQL/DataBase.cs` about line 617). The
SET value path does not run it: `SetValueOperator` (about line 502) renders a bool `==` / `!=` as a plain `=` / `<>`.
So `Compute(x => x.Flag, x => x.Score.HasValue == false)`, or a nullable bool compared with a constant, may store a
value C# would not, or fail on a provider that cannot compare a predicate with `=` (SQL Server). Not measured.

## Acceptance criteria

- [ ] Measure first, live on the four SQL providers: the TASK-863 shapes (`tests/Shared/StoreContract/BoolComparisonContract.cs`)
  used as a SET value, each against C#. Record the result.
- [ ] Nothing wrong → close with the measurement and pin it as a test (rule 54: a hypothesis falsified, not adopted).
- [ ] Something wrong → red first, then fix (the same rewrite in SET position, or a refusal), and the contract passes.

## Human test plan

N/A — fully covered by automated tests.
