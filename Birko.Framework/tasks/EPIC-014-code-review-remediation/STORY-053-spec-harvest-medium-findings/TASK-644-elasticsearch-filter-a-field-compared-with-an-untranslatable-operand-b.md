---
id: TASK-644
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

# ElasticSearch filter: a field compared with an untranslatable operand becomes an exists test

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `filter-expression-translation`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

In `ParseComparison`'s null branch (`Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:316`), when one operand is
a field and the other cannot be translated (`x.Name == x.Sub.Name`, `x.Code == x.Name.Substring(1)`), the
field-vs-field refusal does not fire: the value is null, and the comparison becomes `MustNot(exists field)` or `exists`.
A silent wrong answer that also bypasses the nested-member refusal (rules 42, 44). The spec records it as current
behaviour.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Such a comparison is refused with NotSupportedException
- [x] Red on the current code; the spec `docs/specs/filter-expression-translation.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced, 5 of 5 red.** `x.Text == x.Text.Trim()`, its `!=` form, the mirrored `x.Text.Trim() == x.Text`, and
the nested `x.Owner.Name == x.Text` / `x.Text == x.Owner.Name` all translated without an error. Each became
`MustNot(exists text)` (`exists` for `!=`).

**Cause:** in `ParseComparison`, `ParseConstant` answers `null` for a null literal, and every untranslatable
operand also answers `null`. So "the value is null" and "the operand did not translate" reached the null branch
the same way.

**Fix:** the null branch now runs only when the non-field operand contains no reference to the parameter (a
literal, or a closure or expression that evaluates to null). An operand that still refers to the parameter cannot
be the null constant, so it is refused with `null`, which `ParseFilterQuery` turns into `NotSupportedException`.
`== null`, `null ==`, a closure holding null, and `!= null` still give the existence tests (pinned).

**Tests:** `Task644UntranslatableOperandTests` (5 refusals and 1 pin). `Birko.Data.ElasticSearch.Tests` is 282 of
282, live. The 8 suites that import `Birko.Data.ElasticSearch` (386 tests) are green.

**Docs:** the spec `filter-expression-translation` (the comparison requirement; the scenario that recorded the old
behaviour is now its inverse), and Recent Updates (the oldest entry rolled).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-644` — leave empty until then._
