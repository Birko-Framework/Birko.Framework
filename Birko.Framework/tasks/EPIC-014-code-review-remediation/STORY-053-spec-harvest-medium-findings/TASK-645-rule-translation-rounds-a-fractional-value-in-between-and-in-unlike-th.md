---
id: TASK-645
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Rule translation rounds a fractional value in Between and In, unlike the in-memory evaluator

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `specifications-and-paging`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

TASK-171's fractional-against-integral fix (SH-M294) covers the six comparison operators only. `Between` and `In`
still convert with `ChangeType`, which rounds (`Birko.Rules/Expressions/RuleExpressionConverter.cs:286-289,337`), so
`Qty Between 10.5..20` matches 10 and `Qty In [1.5]` matches 2. The in-memory evaluator compares exactly
(`ComparisonHelper.cs:28,204`), so the two engines disagree, which [[TASK-144]] set out to end.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Between and In agree with the evaluator for fractional values against integral members
- [x] Red on the current code; the spec `docs/specs/specifications-and-paging.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced, red first.** On TASK-171's sample (`Qty` 11, 10, 3), `Between(Qty, 10.5, 20)` through `ToExpression()`
selected {11, 10}, while `IsSatisfiedBy` selected {11}: `Convert.ChangeType(10.5, int)` rounds to 10.

**Fix: one producer for the comparison.** `BuildBetween` builds each bound through `BuildComparison`
(`GreaterThanOrEqual` and `LessThanOrEqual`), and `BuildIn` builds each candidate through it with `Equal`. That is
the method in which SH-M294 compares a fractional value against an integral member in `double`. A degraded bound or
candidate (one that cannot be converted, or a type with no ordering, SH-M291) still makes the whole leaf
unevaluable, as before.

**Tests:** `SpecificationPagingTriageTests.Between_and_In_do_not_round_a_fractional_value_against_an_integral_member`
covers both engines (`Agree`): fractional lower and upper bounds, `In` with a fractional candidate alone and mixed,
and `NotIn`. `Birko.Data.Patterns.Tests` (132) and `Birko.Rules.Tests` (188) are green.

**Docs:** the specs `specifications-and-paging` (the requirement and a new scenario) and `validation-and-rules`
(Between and In), and Recent Updates.

**Regression:** the 93 suites that import `Birko.Rules` (5,339 tests) are green, live on local Docker (no Redis,
so its gated tests returned early).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-645` — leave empty until then._
