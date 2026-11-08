---
id: TASK-860
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-857]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser silently drops method calls and casts it does not translate

## Context

Found by TASK-857 (2026-11-07). TASK-857 made every `Math.*` call either translate exactly or refuse. The same silent
dropping applies to other calls and to casts, and it was measured on SQLite, PostgreSQL, MySQL and SQL Server:
- `x.Code.Substring(1) == …` and `Convert.ToInt32(x.Amount) == …` drop the call and compare the bare column: wrong
  rows on every provider.
- `(decimal)x.Hits / 4m` drops the cast, so PostgreSQL and SQL Server do integer division: wrong rows.
- Update SET, from code reading only: `ParseExpression` returns null for a unary minus.
- Also found by the TASK-859 review, on SQLite:
  - `x.Score.GetValueOrDefault() != 5` drops the call.
  - `string.Compare(x.Name, "a") > 0` renders `Name > 'a'`, which loses C#'s culture ordering.

A translator that cannot express a node must refuse it (rules 41–44). Today the parser's default for an unknown call
or conversion is to keep the operand and lose the operation, which turns a filter into a different filter with no
error.

## Acceptance criteria

- [x] Inventory every expression node kind the SQL filter parser (`DataBase.ParseConditionExpression` and the value
      fragment path) can meet: method calls, conversions, unary and binary operators, conditionals, member access,
      invocations. For each, record "translated exactly", "refused" or "silently dropped", per provider, measured
      against the C# oracle for a representative shape.
      → Measured 2026-11-08 with a 125-case contract on SQLite and on PostgreSQL, MySQL and SQL Server live.
        Silently wrong rows, or an unrelated error:
        - String calls on a column: Substring, Trim*, IndexOf, Replace, PadLeft, Length, `Code[0]`, concatenation,
          IsNullOrWhiteSpace. Comparisons: `string.Compare` / `CompareOrdinal` / `CompareTo`, `Equals` with a
          comparison.
        - Conversions: `Convert.ToInt32`, narrowing casts, `x.Hits / 4.0` and `(decimal)x.Hits / 4m` dividing as
          integers. MySQL's integer `/` divided fractions.
        - DateTime members on a column, integer bitwise operators, `GetValueOrDefault`.
        - SET values: unary minus, `~` and `!` gave syntax errors, and `&` and `|` rendered as AND and OR.
        Already exact, and kept: LIKE, ToLower/ToUpper, IsNullOrEmpty, `.Date ==`, `.Value`/`.HasValue`, `??`,
        TASK-857's Math, enum and widening comparisons.
- [x] Every "silently dropped" node becomes "translated exactly" (only where the translation is exact on every
      provider; numeric casts, for example, made explicit per provider) or "refused by name" with
      `NotSupportedException` before the statement runs. The parser's default for an unknown node is refusal, not
      pass-through.
      → `DataBase_CallTranslation.cs` rewrites `GetValueOrDefault` to `??`, `Equals` to `==` (enum and nullable
        included), a bool XOR to `!=`, and char patterns to the one-character string form. It also holds the refusal
        guards.
      → `SqlConversion` classes each conversion as transparent, widening or lossy. Integer → decimal / double / float
        is widening, with an explicit cast where it changes the result: `SqlValueFragmentDialect.Conversion`, plus a
        new `MySqlValueFragmentDialect` (DIV, DOUBLE) and SQLite REAL.
      → Every other call, conversion and node is refused by name, before any statement runs: checked on reads,
        `Update(filter, Action)`, and a chunked delete.
- [x] The same for update SET values (`ParseExpression`), including unary minus.
      → The SET parser was rewritten. Negation goes through the dialect, and any other unknown node is refused. Only a
        decimal quotient is refused, because SQL Server sizes it to six decimals and SQLite divides as double.
      → Narrowing integer SETs (`Sh = (short)(Sh + 1)`), measured: PostgreSQL, SQL Server and MySQL (strict mode, the
        8.x default) fail on overflow and write nothing, so they are allowed. SQLite stored 32768 silently and every
        later read of that row threw, so SQLite refuses (`StoresIntegerOverflowLoudly`), including in-range values.
- [x] A shared contract (like TASK-857's `MathFilterContract`) runs the inventory on all four providers, red before the
      fix. Every SQL importer suite stays green, and any consumer-visible refusal is listed as breaking.
      → New `tests/Shared/StoreContract/CallCastFilterContract.cs`, 147 cases, in the four SQL provider suites.
        - Red proof: on the old code, 86 failed on SQLite and 85 on each server.
        - The independent review's regression shapes pass both on HEAD and on the new code.
      → The independent review compared 100+ shapes against HEAD and found no blocker. It found 4 shapes that had
        worked before and now refused: compiler-inserted long → double, SET `Hits / 2.0`, narrowing SET, and enum
        `Equals`. All four were fixed before the commit. It also found `Contains('b')` silently wrong, which was
        broken before TASK-860; fixed too.
      → Suites green live: SQL.Tests 810, SQLite 1419, MySQL 869, SQL Server 900, PostgreSQL 846 of 847. The one
        failure is `DeadlockRetryLiveTests.With_retry_off`, identical on HEAD. All 35 importer suites ran green in
        the first pass; TimescaleDB has no container.
      → Updated on purpose: `DecimalValueFragmentComparisonTests` (the refusal message), and
        `DestructiveFilterGuardTests` (`Widget` gets a `[Table]`).
      → **Breaking:** an untranslatable call or cast now throws `NotSupportedException`. A scan of Symbio's ~1,440
        filter lambdas found none that refuse.
      → **Found, filed:**
        - TASK-869: LIKE ignores `StringComparison` and follows collation; needs a decision.
        - TASK-870: SQLite ASCII-only case folding, decimal division precision, SET `.Date` on SQL Server, Replace
          collation, and SQLite silent integer overflow in `SET Hits = Hits + 1`.

## Out of scope

- `Math.*` (TASK-857, done).
- NULL semantics (TASK-859).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
