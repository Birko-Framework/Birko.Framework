---
id: TASK-450
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-09-16
depends-on: []
blocks: []
# findings: field-found while closing TASK-447. In fix-next's pool via EPIC-014's review-intake stamp.
findings: []
pr: "Birko.Data.Migrations.CosmosDB 5972a73 · tests 5c0459c"
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

- [x] Establish what Cosmos actually does with `'O''Brien'` and with a value containing a backslash.
      This determines the severity and must be settled before the fix is designed
- [x] Filter values are **parameterised** rather than escaped, for the reason TASK-447 recorded:
      `QueryDefinition.WithParameter` is available at all three sites, and escaping is a blacklist
      against a grammar that can grow. If a reason to escape instead is found, record it
- [x] `ParseFilterToSql`'s signature change is reflected in
      `Birko.Data.Migrations.CosmosDB.Tests/CosmosDataMigratorHelperTests.cs`, whose assertions pin the
      rendered string shape. **Invert them, do not delete them** (CLAUDE.md § TASK-211) — each still
      covers its operator, now asserting the placeholder plus the bound value
- [x] ⚠ The **identifier** side is untouched and stays untouched: `c["{escaped}"]` with
      `Replace("\\", "\\\\").Replace("\"", "\\\"")` is CR-M104's fix and is a different problem
      (identifiers cannot be parameterised). Say so in the change rather than leaving a reader to
      wonder why one half moved
- [x] `CosmosDBSchemaBuilder.cs:103` (`IS_DEFINED(c["{oldName}"])`) is checked in the same pass — it
      interpolates a caller-supplied field name into a statement with no escaping visible at that line
- [x] Red-verified: the payload test fails against the current escaping

## Out of scope

- `CosmosViewStore` — [[TASK-447]] parameterised it. This task is the migrator only, and should follow
  the same shape so the framework ends with one answer rather than two.
- The `DateTime` format in `FormatSqlValue` (`yyyy-MM-ddTHH:mm:ssZ`, which drops sub-second precision
  and ignores `Kind`). Real, but a fidelity question rather than a containment one — and it disappears
  anyway if values are parameterised, so record the change rather than fixing it separately.

## Human test plan

**N/A - covered by automated tests, with one limit stated.** The helpers are `internal static` and
assert offline; the three query sites and the schema builder are covered by source scans because they
touch a live container before building their statement.

⚠ What no test here can do is execute a statement against Cosmos. Criterion 1's severity finding rests
on measured *emission* plus the documented literal grammar. When a consumer selects
`DataProvider.CosmosDB`, one recorded live step is owed: run a filtered migration whose filter value
contains a quote and a backslash, and confirm it matches rather than erroring.

## Implementation plan

_Populated by `/tasks plan TASK-450` — leave empty until then._

## Progress log

- step 2 - picked by explicit user instruction. It was also the standing next pick from TASK-447's
  close: same subsystem, reasoning fresh, and its first acceptance criterion is a measurement rather
  than a design decision.
- step 3 - **CONFIRMED, and criterion 1 settled the severity: it is an injection, so P1 was
  understated.** Emission measured by calling `FormatSqlValue` directly (offline):

  | Input | Emitted | Under Cosmos NoSQL's literal grammar |
  |---|---|---|
  | `O'Brien` | `'O''Brien'` | `'O'` then `'Brien'` - two adjacent literals, **syntax error** |
  | `foo\` | `'foo\'` | the backslash escapes the terminator - **unterminated literal** |
  | `a\' OR 1=1 --` | `'a\'' OR 1=1 --'` | closes as the string `a'`, then ` OR 1=1 ` executes, `--` comments the tail - **injection** |

  So the `''` doubling is a *correctness* defect (Cosmos escapes with backslash, not by doubling) and
  the **unescaped backslash** is the injection - the same vector as TASK-447, reached differently:
  there a backslash defeated an escape the code had added, here nothing escapes backslashes at all.
  ⚠ As in TASK-447, the **emission** is measured and the **lexing** is read off the documented
  grammar, not executed - that needs a live account. Said plainly rather than implied.
- step 3b - **criterion 5 checked and it found a third sink.**
  `CosmosDBSchemaBuilder.RenameField:101,103` interpolates the caller's `oldName` into
  `c["{oldName}"]` with **no escaping at all**, while its sibling in the same project escapes
  identifiers (CR-M104). One rule, two behaviours, in two files that ship together.
- step 4 - layer: **local**. Same shape as TASK-447 so the framework ends with one answer, not two.
- step 5 - fix in `Birko.Data.Migrations.CosmosDB`. **Values** are parameterised:
  `ParseFilterToSql(json, parameters)` collects `@pN` bindings, `BindValue` replaces `FormatSqlValue`,
  a shared `Bind` helper attaches them, and all three call sites take it instead of a bare
  `new QueryDefinition(query)`. **Identifiers** keep escaping - they cannot be parameterised - but
  CR-M104's rule moved into `QuoteFieldPath`, and `CosmosDBSchemaBuilder` now uses it too. Tests:
  `CosmosDataMigratorHelperTests` inverted and extended, **14 -> 23**, 0 warnings under
  `-warnaserror`.
- step 5b - **a side effect named rather than left to be discovered:** the old `DateTime` branch
  formatted as `yyyy-MM-ddTHH:mm:ssZ`, dropping sub-second precision and ignoring `Kind`. Binding the
  value hands that to the SDK's serializer - the same one that wrote the documents - so the
  comparison matches the stored form by construction. That closes the task's own out-of-scope bullet
  about it, which predicted exactly this.
- step 6 - four mutations, all reverted:
  **(A) `''` doubling restored for strings** -> **10 of 23** red.
  **(B) the schema builder interpolating the raw field name again** -> **1 of 23**.
  **(C) `Bind` not attaching the collected parameters** -> **2 of 23**.
  **(D) the COUNT site bypassing `Bind`** -> **1 of 23**.
  **⚠ B, C and D each failed NOTHING on their first run**, and that is the session's real finding
  about my own work. All three live in code paths that call a live Cosmos container before they build
  their query, so none can be reached offline - and I had fixed them without noticing there was no
  test. Each got the cover it could actually have: `Bind` is `internal static` so it is tested
  directly, and the call sites plus the schema builder are covered by **source scans**, the same
  idiom used earlier this session for the seven workflow backends. Without them the fix would have
  shipped with a statement full of `@pN` placeholders and nothing bound to them.

## Outcome

**What was wrong.** `CosmosDBDataMigrator.FormatSqlValue` escaped a string value by **doubling the
quote** - the SQL-standard rule, which Cosmos NoSQL does not use. Cosmos escapes with a backslash, so
the doubling produced two adjacent literals (a syntax error) while a **backslash in the value was
never escaped at all** and closed the literal early: `a\' OR 1=1 --` renders as `'a\'' OR 1=1 --'`,
which lexes as the string `a'` followed by ` OR 1=1 ` and a comment. The framework therefore held two
mutually incompatible escapers for one dialect, and both were wrong.

**What was done.** Values are **parameterised**, matching what TASK-447 did for `CosmosViewStore`, so
the framework now has one answer. Identifiers keep escaping - they cannot be parameterised - and
CR-M104's rule became a shared `QuoteFieldPath` that `CosmosDBSchemaBuilder` also uses; it had been
interpolating a caller-supplied field name with no escaping whatsoever.

**Step-6 split.** 10 of 23, 1 of 23, 2 of 23, 1 of 23 - see the Progress log, including why three of
those four first measured zero.

**Judgement calls.**

- **Parameterise rather than fix the escaping**, for TASK-447's reason: all three sites already built
  a `QueryDefinition` and never bound anything, and a bound value has no grammar to break out of.
- **The identifier half is treated oppositely, deliberately.** An identifier cannot be a parameter, so
  escaping is the only containment; the change moves CR-M104's logic without altering its behaviour,
  which the tests assert.
- **Source scans for the three unreachable sites.** Weaker than a behavioural test and the honest
  alternative to no test at all - `RenameField` and the three query sites all touch a live container
  before building their statement. Each scan says in its remarks why it exists and what it measured.
- **⚠ The P1 rating was understated.** It was chosen deliberately, because the parser's behaviour was
  unmeasured and calling it a leak would have been a claim ahead of its evidence. Criterion 1 settled
  it: this is the same injection class as TASK-447's P0. Left at P1 in the frontmatter rather than
  rewritten after the fact, with the correction recorded here - the rating is a record of what was
  known when it was filed.
