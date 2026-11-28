---
id: TASK-1014
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
related: [TASK-1008, TASK-1012, TASK-1002]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL parser's parse-time constant comparison drops an enclosing NOT

## Context

Found by [[TASK-1008]]'s pass 1 (NULL semantics) on 2026-11-28; the user asked for it to be filed.

`DataBase.ParseConditionExpression` (`Birko.Data.SQL/SQL/DataBase.cs`, about line 790) evaluates a binary comparison
neither side of which references the lambda parameter. A `true` result returns `Array.Empty` (no condition, every row)
whatever the `parent` carries, so it is not routed through `AlwaysTrue(parent)`, the TASK-1012 fix for the same shape.
At top level, `!(<parameter-free comparison>)` would read every row; inside AND/OR, `IsConstantBoolCondition` now
catches it.

It looks unreachable: `ExpressionNormalizer` funcletizes every parameter-free subtree at the lambda boundary before the
parser runs. But a second copy of a rule is the drift rule 53 describes, and the TASK-1002 corpus would not see it if
the normalizer ever stopped folding a shape.

## Acceptance criteria

- [ ] Show whether the branch is reachable: a test that reaches it, or a measurement that nothing does.
- [ ] Reachable → red first, then route the `true` result through `AlwaysTrue(parent)`. Unreachable → delete the
  branch (rule 53), with the corpus still green.
- [ ] The TASK-1002 corpus and `AlwaysFalseGroupContract` pass.

## Human test plan

N/A — fully covered by automated tests.
