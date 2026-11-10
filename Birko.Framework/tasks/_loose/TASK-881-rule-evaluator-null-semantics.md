---
id: TASK-881
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduce (rule 54) with TASK-864's shapes in memory, against C#.
      → Measured 2026-11-10: TASK-864's 39 shapes plus 8 more × plain / `IsNegated` / negated AND group × both contexts,
        against the rule compiled by `RuleExpressionConverter` (itself checked against hand-written lambdas). TASK-864's
        shapes differed on 21 of 117, as TASK-864 recorded: `LessThan(OrEqual) 5` matched NULL rows; `In [null, 5]` and
        `Name In [abc, null]` missed NULL; `NotIn [null, …]` matched NULL. The extra shapes also showed
        `GreaterThan null` matching every non-null row and `Between null..5` matching rows ≤ 5.
- [x] `RuleEvaluator` follows C# on NULL (TASK-859's decision: C# semantics, always), or a deviation is recorded with
      its reason. Check Symbio's IoT rules for any change.
      → `ComparisonHelper.CompareValues` returns `int?`, null when either side is null, so every ordering and both
        `Between` bounds are false on NULL through C#'s own lifted comparison; negation then gives C#'s true. `IsIn` no
        longer returns early on a null value: each candidate goes through `AreEqual` (null == null). The converter's
        logic is not shared: it relies on typed member expressions, the evaluator on boxed values; the test keeps them
        in agreement.
      → Symbio's IoT rules (`AlarmService`, `AutomationService`): every context value and rule value is a non-null
        decimal and only ordering / Equal / NotEqual are used, so no result changes. Inside the framework,
        `RuleBasedValidator` and `RuleFilterBehavior` now answer NULL as C# does (e.g. "Age LessThan 18" no longer flags a
        null Age), intended under TASK-859.
- [x] Tests, red before the fix.
      → `Task881RuleEvaluatorNullTests` (47 shapes × 3 forms × 2 contexts, plus the reference check): 72 of 474 red with
        the `ComparisonHelper` hunk reverse-applied, 474/474 green. All 98 importers of `Birko.Rules.projitems` green
        apart from other tasks' in-progress tests at the time.
      → Noted for TASK-895 and TASK-882: in-memory string `Equal` is still case-insensitive (SH-M295), while SQL rules are
        to become exact; a non-nullable member against a null rule value now gives C#'s lifted answer here and in SQL,
        and only `RuleExpressionConverter` degrades it to "matches nothing".

## Out of scope

- SQL rule conversion (TASK-864, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
