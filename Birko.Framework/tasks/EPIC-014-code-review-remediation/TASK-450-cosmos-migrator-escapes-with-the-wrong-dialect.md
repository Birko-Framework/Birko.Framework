---
id: TASK-450
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-09-16
depends-on: []
blocks: []
# findings: field-found while closing TASK-447. In fix-next's pool via EPIC-014's review-intake stamp.
findings: []
pr: null
github-issue: null
jira-key: null
---

# The Cosmos migrator escapes filter values with SQL-standard doubling, which Cosmos does not use

## Context

Found while closing [[TASK-447]], by its criterion 6 — *check whether the Cosmos migration emitters
interpolate caller values the same way*. They do, and they are wrong in a **different** way, which is
what makes this its own task rather than a second instance of the same bug.

`Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.FormatSqlValue:246`:

```csharp
if (value is string s) return $"'{s.Replace("'", "''")}'";
```

That is **SQL-standard quote doubling**. Cosmos NoSQL does not use it — it uses **backslash** escapes
inside a string literal (`\'`, `\"`, `\\`, `\n`, …), which is what the view store used before TASK-447
replaced it with bound parameters. So the two Cosmos value formatters in this framework disagree with
each other about the same dialect, and at most one of them can be right (CLAUDE.md § TASK-274, *two
doors onto one feature must give one answer*).

`FormatSqlValue` feeds `ParseFilterToSql`, whose output is interpolated at three sites:

| Site | Statement |
|---|---|
| `CosmosDBDataMigrator.cs:83` | `SELECT {projection} FROM c WHERE {whereClause}` |
| `CosmosDBDataMigrator.cs:110` | `SELECT {projection} FROM c WHERE {whereClause}` |
| `CosmosDBDataMigrator.cs:135` | `SELECT VALUE COUNT(1) FROM c WHERE {whereClause}` |

each wrapped in a bare `new QueryDefinition(query)` — so, exactly as in TASK-447, **parameters are
available and simply are not used**.

⚠ **What is measured and what is not.** The *rendering* is read off the source. Whether Cosmos accepts
`''` as an escape, treats `'O''Brien'` as two adjacent literals (a syntax error), or does something
else is **not measured** — it needs a live account, and the answer decides whether this is a
correctness defect, an injection, or both. Establish that first; do not inherit TASK-447's "this is an
injection" conclusion, because the escaping scheme is different.

§ TASK-253 already recorded `Birko.Data.Migrations.CosmosDB` as the **one** site deliberately left out
of the `SqlLiteral.EscapeLiteral` convergence, so this file has been known to be an outlier.

**Measured reach, 2026-09-16.** Latent: no consumer selects `DataProvider.CosmosDB`, and the migrator
runs only when a Cosmos migration is executed. Rated P1 rather than P0 because — unlike TASK-447 —
the payload's effect on the parser is not yet established, so calling it a leak would be a claim ahead
of its evidence.

## Acceptance criteria

- [ ] Establish what Cosmos actually does with `'O''Brien'` and with a value containing a backslash.
      This determines the severity and must be settled before the fix is designed
- [ ] Filter values are **parameterised** rather than escaped, for the reason TASK-447 recorded:
      `QueryDefinition.WithParameter` is available at all three sites, and escaping is a blacklist
      against a grammar that can grow. If a reason to escape instead is found, record it
- [ ] `ParseFilterToSql`'s signature change is reflected in
      `Birko.Data.Migrations.CosmosDB.Tests/CosmosDataMigratorHelperTests.cs`, whose assertions pin the
      rendered string shape. **Invert them, do not delete them** (CLAUDE.md § TASK-211) — each still
      covers its operator, now asserting the placeholder plus the bound value
- [ ] ⚠ The **identifier** side is untouched and stays untouched: `c["{escaped}"]` with
      `Replace("\\", "\\\\").Replace("\"", "\\\"")` is CR-M104's fix and is a different problem
      (identifiers cannot be parameterised). Say so in the change rather than leaving a reader to
      wonder why one half moved
- [ ] `CosmosDBSchemaBuilder.cs:103` (`IS_DEFINED(c["{oldName}"])`) is checked in the same pass — it
      interpolates a caller-supplied field name into a statement with no escaping visible at that line
- [ ] Red-verified: the payload test fails against the current escaping

## Out of scope

- `CosmosViewStore` — [[TASK-447]] parameterised it. This task is the migrator only, and should follow
  the same shape so the framework ends with one answer rather than two.
- The `DateTime` format in `FormatSqlValue` (`yyyy-MM-ddTHH:mm:ssZ`, which drops sub-second precision
  and ignores `Kind`). Real, but a fidelity question rather than a containment one — and it disappears
  anyway if values are parameterised, so record the change rather than fixing it separately.

## Human test plan

_Resolve before `/tasks close`. Likely one recorded live step, because the first acceptance criterion
cannot be answered offline._

## Implementation plan

_Populated by `/tasks plan TASK-450` — leave empty until then._
