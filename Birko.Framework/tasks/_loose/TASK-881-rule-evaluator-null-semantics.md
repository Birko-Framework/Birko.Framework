---
id: TASK-881
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-864]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `RuleEvaluator` answers NULL differently from C#

## Context

Found by TASK-864 (2026-11-09), measured: `RuleEvaluator` (`ComparisonHelper`, Birko.Rules) differs from C# and from
`RuleExpressionConverter` on 21 of TASK-864's 117 NULL shapes. It orders NULL below every value (`Score LessThan 5`
selects NULL rows), never matches NULL from an `In` list, and treats `GreaterThan null` as true for every non-null row.
Since TASK-864, SQL rules follow C#, so the same rule now answers differently in memory than through a store. Symbio's
IoT module runs `RuleEvaluator` in memory, on non-null telemetry.

## Acceptance criteria

- [ ] Reproduce (rule 54) with TASK-864's shapes in memory, against C#.
- [ ] `RuleEvaluator` follows C# on NULL (TASK-859's decision: C# semantics, always), or a deviation is recorded with
      its reason. Check Symbio's IoT rules for any change.
- [ ] Tests, red before the fix.

## Out of scope

- SQL rule conversion (TASK-864, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
