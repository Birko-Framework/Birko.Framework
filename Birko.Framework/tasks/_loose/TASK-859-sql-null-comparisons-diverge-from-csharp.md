---
id: TASK-859
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL filter on a nullable column drops the NULL rows that C# matches

## Context

Found by the TASK-856 review (2026-11-07), measured on SQLite; expected on every SQL provider, because the cause is
the parser.
- `x.Optional != 8m` and `new decimal?[] { null, 10.1m }.Contains(x.Optional)` miss the rows where `Optional` is NULL,
  which C# matches: in C#, `null != 8` is true and `null` is in the list.
- SQL's three-valued logic makes `NULL <> 8` UNKNOWN, and `NULL IN (NULL, …)` is never true.
- The value-fragment forms (`x.Optional + 1m != 8m`) behave the same.
- TASK-213's tests already record this divergence (a NULL `Score` row excluded by SQL and counted by C#), but nothing
  ever scheduled a fix.

The answer is silently different from the C# expression it was translated from (rules 26 and 42).

## Decision (the user, 2026-11-07)

**Fix always, as a breaking change.** Filters behave like the C# expression they are written as:
- `!=` and `!Contains` include NULL rows;
- `Contains` with `null` in the list matches NULL.

There is no opt-in flag. The change is marked **breaking** in Recent Updates; Symbio is still in development.

## Acceptance criteria

- [x] Measured per SQL provider (live where available), against the C# oracle: `!=` against a constant and against a
      nullable column, `Contains` with a list holding null, `!Contains`, and `==` null. Record each.
      → Measured 2026-11-07, 61 shapes, the same on SQLite, PostgreSQL, MySQL and SQL Server (live).
        - 34 of 61 differed from C#.
        - `!=`, `!Contains` and negated comparisons missed the NULL rows (2 of 4 returned).
        - `Contains` with null in the list missed them too.
        - `!{null}.Contains` returned 6 of 4.
- [x] Translated to C# semantics: `col <> v OR col IS NULL`; for a list with null, `col IN (…non-null) OR col IS NULL`;
      `NOT IN` likewise NULL-aware; column-vs-column with `IS [NOT] DISTINCT FROM` or the provider's equivalent. Or
      refused by name where a provider cannot express it. Decide with the user whether this is a breaking change
      worth a flag, since some callers may rely on SQL semantics.
      → Decided: always, breaking, no flag (§ Decision).
      → The parser marks which operands can be NULL, and a final pass adds IS NULL legs only where SQL and C# would
        answer differently. Inside a NOT, the guard takes the form `(col = v AND col IS NOT NULL)`.
      → A `{null}` list becomes `IS [NOT] NULL`. Column-vs-column keeps TASK-574's null-safe form.
      → An ordering against a null closure value is always false (or always true when negated). The bare destructive
        form is refused by the whole-table guard.
      → Non-nullable columns (by CLR type, `[Required]`, primary key) render unchanged.
      → Chunking (TASK-833): `IN(L) OR IS NULL` carries its IS NULL leg in the first chunk only, so the chunks stay
        disjoint: a NULL row is read, counted and incremented once. NOT IN with a null leg is never split.
- [x] Regression tests per shape and provider, red before the fix. The TASK-213 tests that pin the old divergence are
      updated deliberately, not deleted.
      → New shared `tests/Shared/StoreContract/NullFilterContract.cs` (61 shapes), run in the SQLite, PostgreSQL,
        MySQL and SQL Server suites, plus `Task859NullComparisonTests` (rendering, chunking, guard).
      → `SqlExpressionParityTests` gains 7 nullable cases, and the TASK-213 divergence note in
        `ComputedContainsOperandTests` is updated to the new answer.
      → Red proof: with the fix reverted, 83 of 185 SQLite tests failed, and the contract was 34 of 61 red per provider.
        All 35 SQL importer suites green live.
      → Independent review: 75 extra read shapes and 10 delete shapes, 70 matched C#, and the other 5 also fail on
        HEAD (filed below). No blockers.
      → **Found, filed:**
        - The review found a new route to TASK-861's whole-table delete: `!(x.Score > nul && x.Amount > 3)` with a null
          `nul` folds to `NOT (1 = 0 AND …)`, which deleted 6 of 6 rows (on HEAD it rendered `Score IS NULL`). It is
          fixed by TASK-861's general reduction, taken next, before any push.
        - TASK-861 (P1): a negated always-false group deletes every row; it predates this task.
        - TASK-862: a MySQL keyword-named column.
        - TASK-863 (P1): inverted boolean comparisons that already fail on HEAD.
        - TASK-864: `RuleConditionConverter` still builds SQL-semantics conditions.
        - `GetValueOrDefault` and `string.Compare` were added to TASK-860.
      → **Residual risk, accepted:** whether a column can be NULL is read from the CLR type and `[Required]`, not from
        the database. A column that gained `[Required]` while holding NULL rows keeps SQL semantics on `!=`.

## Out of scope

- Decimal fragment comparison on SQLite (TASK-856).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
