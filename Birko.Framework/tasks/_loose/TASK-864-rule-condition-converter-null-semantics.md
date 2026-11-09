---
id: TASK-864
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduce first (rule 54): the same rule, in memory and against SQLite (plus the live providers), on rows with
      NULL values. Record each operator.
      → Measured 2026-11-09: 39 rule shapes × plain, `IsNegated` and inside a negated AND group = 117 reads, on rows with
        NULL `int?` and `string?` columns, against the compiled rule (`RuleExpressionConverter`, C#) and `RuleEvaluator`.
        Byte-identical on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022: **53 of 117 differed from C#.**
        - NotEqual, NotIn, negated orderings, negated Between and every negated string operator lost the NULL rows.
        - `In [null, 5]` missed NULL; `NotIn [null, 5]` bound NULL into `NOT IN` and returned nothing.
        - **`NotIn` / negated `In` with a null set matched every row**; C# matches nothing.
      → `RuleEvaluator` is not C# on NULL either (21 of 117): it orders NULL below every value and never matches NULL
        from an `In` list. Where it disagrees with C#, TASK-859's "C# semantics, always" was followed. Filed TASK-881.
- [x] Rule conditions get the same NULL-aware rendering as TASK-859's filters, from one producer.
      → `ConvertLeaf` marks a leaf over a column that can hold NULL (new `DataBase.RuleFieldCanBeNull`, the lambda
        parser's rule: nullable CLR type, not `[RequiredField]`, not primary key; nullable on the type-less path).
        `Convert` / `ConvertSet` then run the tree through TASK-859's `ApplyCSharpNullSemantics` (now internal), so the
        IS NULL legs have one producer. In / NotIn use `MarkNullableIn`'s expression-free half, split out of the
        parser's; comparisons against a null value use `ComparedWithNull`, replacing the converter's SH-M130 special
        case. Negated Between is `NOT >= lo OR NOT <= hi`. A null `In` set or string needle matches nothing.
      → TASK-861's guard: a negated ordering against null, alone or in a group, reduces to always-true, so Delete and
        Update on such a rule throw `WholeTableWriteException` on all four providers.
- [x] Tests per operator, red before the fix.
      → Shared `RuleNullContract` (39 shapes × 3 forms, read and count, delete, plus the null-ordering guard; 231 cases
        per provider), `Task864RuleNullConditionTests` (shapes, non-nullable / `[RequiredField]` / key columns unchanged,
        `ConstantTruth` verdicts). Four `RuleConditionConverterTests` that pinned the old shape updated on purpose.
      → Red (own hunks reverse-applied): contract 108 of 231 on each provider, unit tests 25 of 50. Green: 231 / 50.
      → All 35 importers of `Birko.Data.SQL.projitems` live, `BIRKO_REQUIRE_LIVE=1`, run alone: green (SQLite 2081,
        PostgreSQL 1501, MySQL 1523, SQL Server 1557, SQL.Tests 884, Views 338, TimescaleDB 117/89/7).
      → Consumers: no `RuleConditionConverter` user; Symbio runs `RuleEvaluator` in memory only.
      → **Found, filed:** TASK-881 (`RuleEvaluator` is not C# on NULL), TASK-882 (rule converters' remaining
        disagreements: non-nullable columns against null, interior `%` in Like, values not converted with
        `ToStoredValue`), TASK-883 (live SQL contracts share fixed table names, so concurrent runs collide).

## Out of scope

- The expression parser (TASK-859, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
