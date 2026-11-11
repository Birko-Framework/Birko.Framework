---
id: TASK-882
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
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

# Rule converters: remaining disagreements after TASK-864

## Context

Found by TASK-864 (2026-11-09), not measured in its contract:
- **Non-nullable column against a null value:** `RuleExpressionConverter` degrades to "matches nothing"; the SQL
  converter gives IS NOT NULL (every row) for `NotEqual null` and every row for a negated ordering against null (C#'s
  lifted answer). `Amount In [null, 5]` matches nothing in memory and matches 5 in SQL.
- **Like with an interior `%`** (`a%b`) is unevaluable in memory but runs in SQL (related to SH-M362).
- **Rule values reach SQL without `ToStoredValue`:** an enum or Guid sent as text is not converted to the stored form,
  unlike the lambda path.

**Decided by the user (2026-11-11):** rule string operators (`Like`, `Contains`, `StartsWith`, `EndsWith`) ignore case
exactly (`OrdinalIgnoreCase`) on every runner, SQL included — one answer wherever a rule runs; `Equal` stays exact
(TASK-895). A `Like` with an interior `%` is refused by name everywhere for now; supporting it is TASK-915.

## Acceptance criteria

- [x] Reproduce each (rule 54) against C#, in memory and in SQL.
      → Measured 2026-11-11: 77 shapes (`tests/Shared/StoreContract/RuleLeftoverShapes.cs`, each with a hand-written C#
        lambda), plain / negated / in a negated group = 231 cases per runner. Failing before (items 1 / 2 / 3 of
        69 / 69 / 93): compiled rule in memory 43 / 21 / 9, through the store 43 / 9 / 9; `RuleEvaluator` 0 / 12 / 30; SQL rule
        SQLite 6 / 33 / 50, PostgreSQL 6 / 57 / 78, MySQL 6 / 27 / 41, SQL Server 6 / 24 / 42. Contract per provider 227 / 331 /
        200 / 193 of 693 failing.
- [x] Each one that differs is made consistent (one answer per rule, wherever it runs) or refused by name.
      → (1) The compiled rule lifts a non-nullable member as C# does (`NotEqual null` every row, `Equal`/orderings
        nothing, negations every row, `In [null, 5]` matches 5); SQL `Between` with a null bound no longer throws
        (`ComparedWithNull`); an enum ordering compiles by underlying value. (2) One parser `RuleLikePattern` for all
        runners; interior `%` refused by name (message names the field, not the pattern); SQL string operators carry
        `OrdinalIgnoreCase` and render through TASK-869's `SqlStringMatch` (TASK-895's collation pin inverted, the
        user's decision); `a%%` is a prefix; a string operator on a non-string column matches nothing in SQL. (3) One
        producer `RuleValueConverter`; SQL binds through `DataBase.TryRuleStoredValue` (convert, then
        `AbstractField.ToStoredValue`, rule 16); a value with no form for the member matches nothing in every runner.
- [x] Tests, red before the fix.
      → `Task882RuleLeftoversTests`, `RuleLeftoversContract` in the 4 SQL providers, `Task882RuleValueAndPatternTests`,
        `Task882RuleConditionTests` (20 of 25 red with the SQL hunks reverted): 0 failing of 693 per provider after. Old
        pins inverted (rule 56): `OneRuleTranslatorTests`, `ValidationAndRulesTriageTests`, `RuleSpecificationMatchAllTests`,
        `Task895StringEqualityLeftoversTests`, `FilterTranslationTriageTests`; `SpecificationPagingTriageTests` re-premised
        on SH-M291.
      → Live: 97 importer suites of Birko.Rules / Birko.Data.SQL green on the working tree (Redis suites excluded; one Redis test in EventBus.MessageQueue needs a host); staged HEAD + patch snapshot: Rules 1244, Patterns 166, SQL.Tests 1148, SQLite 3173, PostgreSQL 2593, MySQL 2615, SQL Server 2649 - all green.
      → Consumers: Symbio's IoT rules use `RuleEvaluator` with decimals and dictionary contexts — unchanged.
      → **Found, filed:** TASK-915 (interior `%` support), TASK-916 (evaluator leftovers: unconvertible value on a NULL
        member, culture string ordering SH-M291, `Name Equal 5` vs `"05"`; SQL Server Guid ordering).

## Out of scope

- NULL on nullable columns (TASK-864, done); `RuleEvaluator` NULL ordering (TASK-881).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
