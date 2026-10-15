---
id: TASK-144
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P3
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: [SH-M295, SH-M297, SH-M299]
pr: null
github-issue: null
jira-key: null
---

# `RuleSpecification` and `RuleExpressionConverter` are two translators of one rule model

## Context

Filed from [[TASK-116]]'s Outcome, where unifying them was out of scope for a correctness fix.

`Birko.Rules`' `RuleExpressionConverter` and `Birko.Data.Patterns`' `RuleSpecification` both turn an
`IRule` tree into a LINQ `Expression`. They are independent implementations of the same mapping, and
TASK-116 made the maintenance cost concrete rather than theoretical:

**`Like`, `In` and `NotIn` were missing entirely from `RuleSpecification`** — falling through to
`Expression.Constant(true)`, i.e. match-all on a filter that also drives bulk `Update` and `Delete` —
while `RuleExpressionConverter` had implemented all three correctly the whole time. The fix was to **port
the arms across by hand**.

So one translator silently lacked three *declared* operators for as long as they had existed, and the
only reason the fix was cheap is that the other one was right. Nothing stops the next operator landing in
one and not the other.

TASK-116 also closed a *behavioural* divergence between `RuleSpecification.ToExpression()` and the
in-memory `RuleEvaluator`, pinned afterwards by a parameterised both-engines test. That test is the model
for how this task should end — except the two engines here are both *expression* translators, so they can
be compared structurally and not only by results.

### Added by TASK-171 (2026-10-15): three medium findings this task owns

- **SH-M295**: string `Equal` is case-insensitive in memory and ordinal at the store. No single case-insensitive
  equality is translated by every store (SQLite / PostgreSQL compare case-sensitively; not every translator accepts
  `string.Equals(…, comparison)` or `ToLower`).
- **SH-M297**: an unresolvable field with `IsNull` matches every entity in memory (the evaluator reads a missing
  field as null, right for dictionary contexts) and none at the store (the three-engine agreement tests pin
  "unresolved matches nothing, even negated"). Refusing the field breaks those pins.
- **SH-M299**: this task's subject.
- Also recorded there: string ORDERING is culture-sensitive in memory and has no store form (SH-M291, now degraded on
  the expression side), and the evaluator does not parse a date given as text (SH-M293).

## Acceptance criteria

- [x] Establish whether the two can become one — including whether `Birko.Data.Patterns` may depend on
      `Birko.Rules` for it (it already references it), and what `RuleSpecification` adds beyond
      translation (the `ISpecification<T>` surface, `IsSatisfiedBy` routed through the evaluator)
- [x] If unified: every behaviour TASK-116 fixed still holds, proven by re-running its suite **unchanged**
      — that suite is this refactor's regression contract and must not be edited to fit the result
- [x] (n/a, unified; an expression-equality theory guards a re-split) If not unified: a test asserts the two translators agree operator-by-operator over a shared table,
      so a future divergence fails loudly instead of waiting to be found by a match-all defect
- [x] Either way, the decision and its reasoning live in the surviving translator's own docs
- [x] The degradation invariant TASK-116 introduced (`Leaf.Unevaluable`, never negated into match-all) is
      preserved or deliberately re-derived — it is the property that makes the translator safe, not an
      implementation detail to drop in a merge

## Out of scope

- The four match-all findings themselves ([[TASK-116]], closed).
- `RuleConditionConverter` — the SQL-**text** rule path, a third and genuinely different translator with
  its own outstanding defect ([[TASK-111]]).

## Human test plan

N/A — fully covered by automated tests. The acceptance is either a behaviour-preserving refactor or a
divergence test; both are mechanically checkable.

## Resolution (2026-10-15)

**Unified.** `RuleExpressionConverter` (Birko.Rules) is the one translator; `RuleSpecification.ToExpression()` calls it,
and the specification is down to its `ISpecification<T>` surface (the constructors and `IsSatisfiedBy` through the
evaluator). `Birko.Data.Patterns` already referenced `Birko.Rules`, so no edge was added. Neither translator had a
caller outside its own tests (measured across the framework and every consumer), so the merge broke nothing.

The two had drifted both ways. The converter took the specification's TASK-116 degradation invariant (`Leaf.Unevaluable`,
never negated, preserved as is) and its TASK-171 fixes: a null needle no longer matches every row, an interior `%` no
longer matches the literal, a fractional value against an int is not rounded, string ordering degrades instead of
throwing, an unconvertible value degrades, and an operator with no arm throws. The specification took the converter's
nested fields with null guards and its text-to-DateTime conversion. A scalar `In` value is one candidate, as the
evaluator reads it; a null one stays unevaluable (TASK-116 pins it).

**SH-M295, decided:** string equality is plain `==`, the store-translatable form. The evaluator stays case-insensitive;
the divergence is documented (converter docs, `docs/rules.md`) and its pin inverted. **SH-M297, fixed:**
`IRuleContext.HasFixedFields` (default false, so no implementer breaks); `ObjectRuleContext` is fixed and resolves nested
paths, and the evaluator answers "no match, never negated" for a field it cannot reach, as the translator does.
Dictionary contexts keep "missing is null".

**Regression contract held:** TASK-116's suite (`RuleSpecificationMatchAllTests`, `RuleGroupNegationTests`) passes
**unchanged**, as do the three-engine agreement tests and TASK-171's triage tests. One pin inverted
(`Equal_StringField_CaseInsensitive`, the M295 decision). **Red:** `OneRuleTranslatorTests` 10 of 17 on the old code
(pins: Guid text and scalar `In`, which the converter already had; the dictionary context; four expression-equality
rows that already matched).

**Regression:** the 92 suites importing `Birko.Rules` (5,033 tests), green, live against PostgreSQL, MySQL, SQL Server,
TimescaleDB, ElasticSearch, MongoDB and RavenDB.

**Spec regen pending**, batched: `validation-and-rules`, `specifications-and-paging`.
