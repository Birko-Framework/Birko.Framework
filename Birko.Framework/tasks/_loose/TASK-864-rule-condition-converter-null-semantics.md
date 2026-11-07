---
id: TASK-864
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-859]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A rule's NotEqual / NotIn misses NULL rows in SQL but matches them in memory

## Context

Found by the TASK-859 review (2026-11-07), by reading the code; not yet reproduced.
- TASK-859 gave SQL filters C#'s NULL semantics, but only on the expression parser's path.
- `RuleConditionConverter` builds conditions directly, without `NullableOperands`, so a rule's `NotEqual` / `NotIn`
  on a NULL column renders `col <> v`, which misses the NULL rows.
- The same rule evaluated in memory (`ComparisonHelper`) matches them.

One rule, two answers, depending on where it is evaluated. That contradicts TASK-859's "always" decision.

## Acceptance criteria

- [ ] Reproduce first (rule 54): the same rule, in memory and against SQLite (plus the live providers), on rows with
      NULL values. Record each operator.
- [ ] Rule conditions get the same NULL-aware rendering as TASK-859's filters, from one producer.
- [ ] Tests per operator, red before the fix.

## Out of scope

- The expression parser (TASK-859, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
