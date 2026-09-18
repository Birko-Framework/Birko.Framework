---
id: TASK-293
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-211, TASK-286, TASK-287, TASK-288, TASK-290, TASK-295]
findings: []
pr: 51cadf2 (Birko.Data.SQL) + bd52c82 (PostgreSQL) + 4be19b5 (MySQL) + 4bd0aa0 (MSSql) + 3ee701e/2a2ccc4/e5d24a6/8e772fc/56b65b0 (tests)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.MSSql]
---

# The escape "anomaly" is decided by a substring of the statement, so it fabricates anomalies

## What was wrong

`AbstractConnector` calls a schema escape **the anomaly** when a table it has recorded a `CREATE TABLE`
for is reported missing — TASK-286's *"but this connector already created it"*. It decided that by asking
the question of the **statement**:

```csharp
_tablesCreated.Where(kvp => commandText.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
```

That is the wrong question, twice over, and both were measured before a line was changed:

| channel | measured |
|---|---|
| **substring collision** — a recorded `Movement`, a first touch of `StockMovements` | `escapes=1 generation=1`, annotated *"…already created it — Movement created …"* against a statement on `StockMovements` |
| **multi-table statement**, no collision at all — a recorded `Ledger`, a statement naming `Ledger` and the never-created `StockMovements` | `escapes=1 generation=1`, annotated on the strength of `Ledger` |

The second needs no unlucky naming: it is the ordinary shape of a view or a multi-type count. Both are
recorded in `Birko.Data.SQL.SqLite.Tests.EscapeAnomalyDiscriminationTests`.

## Why a false positive stopped being cosmetic

`CreatedTablesNamedIn`'s own comment justified the looseness: *"a false positive costs one extra line in
an exception nobody sees unless something already went wrong"*. **TASK-288 made that false.** The same
answer now drives `SchemaGeneration`, and therefore every store's `CanTrustRememberedInitialization`. So
a fabricated anomaly:

- reports an anomaly that did not happen, on a channel a host escalates — and the whole point of
  TASK-287's channel is that it discriminates the anomaly from ordinary lazy first-touch, which is roughly
  **245× more common per bring-up**; and
- **invalidates the remembered initialization of every store on that connector**, each of which then
  re-runs `CREATE TABLE IF NOT EXISTS` under the connector's DDL lock while its own reads wait. Under load
  that is a positive feedback loop, which is exactly the profile [[TASK-290]]'s trigger has.

Three of the eight tables in the consumer's storm evidence sit on a substring relation
(`Movements`⊂`StockMovements`, `Reservations`⊂`StockReservations`, `Events`⊂`AlarmEvents`), so up to 5 of
its 12 escapes may have been fabricated. That is why TASK-290 ranked this ahead of another storm cycle.

## The fix

The provider's own error names the table that is **actually** missing, and names only that one. Measured,
on each provider rather than taken from its documentation:

| provider | error | extracted |
|---|---|---|
| SQLite | `no such table: StockMovements` | `StockMovements` |
| PostgreSQL 16 | `42P01: relation "PgAnomMovement" does not exist` | `PgAnomMovement` |
| MySQL 8.4 | `Table 'birkoview.MyAnomMovement' doesn't exist` | `MyAnomMovement` |
| SQL Server 2022 | `Invalid object name 'MsAnomMovement'.` (error 208) | `MsAnomMovement` |

`AbstractConnectorBase.MissingTableName` / `MissingTableNameChain` is the new member, deliberately in the
`IsMissingTableException` / `IsMissingTableExceptionChain` family — same shape, same chain-walk, same
per-provider override seam, so a reader who knows one knows the other. Five parts of the shape matter:

- **Extract around the QUOTES, not around the English.** PostgreSQL and MySQL localise the prose in these
  messages and never the identifier, so a phrase-anchored parse would silently stop matching on a server
  whose `lc_messages` is not English — and a silent non-match here disables TASK-288's healing rather than
  announcing anything. Same reasoning `IsMissingTableException` records for keying PostgreSQL on the
  SQLSTATE.
- **Gated on the classification.** PostgreSQL's `42P01` is also `missing FROM-clause entry for table "x"`,
  where the relation exists perfectly well — TASK-211 excluded that from `IsMissingTableException`, and the
  extractor must not reintroduce it by parsing the quotes of a message it was never entitled to read. It
  has its own live test, and ungating it reds exactly that one.
- **The qualifier is stripped.** `TablesCreated` is keyed by the bare `Table.Name` the framework created,
  so keeping MySQL's `birkoview.` prefix would fail to find the very entry it is looking for.
- **The fallback is kept, and it is the substring scan.** Reached only when the provider's wording cannot
  be parsed at all. Answering "not the anomaly" there would be the tidier code and the worse behaviour: a
  store whose table really did vanish would stay broken for the life of the process — the defect TASK-288
  exists to remove — and would do so silently. Erring toward re-running is the asymmetry
  `AbstractStore.CanRememberInitialization` already records. Its **trigger** is pinned by a test (a message
  containing the wording but no extractable name), so its reachability is a measured fact rather than an
  assumption.
- **One caller passes the original exception, not the rewrap.** `EnsureSchemaAndReport` builds
  `new Exception(annotatedText, ex)`; the record and the annotation are both computed from `ex`, so they
  cannot disagree about which table they matched.

## What this does NOT fix — and it is bigger than this task

⚠ **The whole escape/heal apparatus is SQLite-only in practice**, discovered while writing this task's
per-provider tests. `RecordTableCreated` is called from exactly one place — the **base**
`CreateTable(string, IEnumerable<string>)` — and PostgreSQL, MySQL and SQL Server each **override** that
method without recording. So `TablesCreated` is permanently empty on three of four providers, and with it
TASK-286's annotation (always *"NO recorded CREATE TABLE"*), TASK-287's channel and TASK-288's healing.
Measured on all three. [[TASK-295]] owns it, and each provider suite carries a test that pins the gap and
says not to "fix" it by adding a fourth copy.

This task's own claim does not depend on that: the discriminator is asserted per provider against the
typed exception, which is the half no offline test can produce.

## Acceptance

- [x] Both false-positive channels measured before the fix and closed after it.
- [x] Both true positives kept, including in a multi-table statement.
- [x] The extraction is measured against each provider's **typed** exception on a live server.
- [x] PostgreSQL's statement-shaped `42P01` yields no name, so TASK-211's narrowing is inherited.
- [x] The fallback's trigger is pinned rather than assumed.
- [x] Mutation-proven, disjointly.

## Measurements

`BIRKO_REQUIRE_LIVE` set, against live PostgreSQL 16, MySQL 8.4, SQL Server 2022 and on-disk SQLite:
`Birko.Data.SQL` **663** (655 → 663), SqLite **303** (298 → 303), PostgreSQL **96** (93 → 96),
MySQL **100** (98 → 100), MSSql **110** (108 → 110), Migrations.SQL **53** — 0 failed, 0 skipped. Without
the flag (no server needed): InMemory 69, JSON 23, XML 18, TimescaleDB 53, Migrations.TimescaleDB 81.

**Mutations, disjoint:**

| mutation | red |
|---|---|
| revert the anomaly decision to the substring scan | **2 of 5** — exactly the two false-positive tests; both true positives and the provider-error test stayed green |
| ungate the PostgreSQL extractor from `IsMissingTableException` | **1 of 3** — the `missing FROM-clause entry` test, live |
| remove the qualifier stripping | **MySQL 1 of 2** live, plus **1** offline (`main.Widgets`); **MSSql green**, because its message carries no qualifier |

## Out of scope

- [[TASK-295]] — the recording that three providers bypass.
- [[TASK-290]] — the mechanism behind the consumer's escapes, still open. This removes a contaminating
  channel from its evidence base rather than answering it.
- Whether `SchemaEscape.TableNames` should now always be a single name. It is still declared as a set, and
  the fallback can return several; narrowing the type would be a public-surface change for no measured
  gain.
