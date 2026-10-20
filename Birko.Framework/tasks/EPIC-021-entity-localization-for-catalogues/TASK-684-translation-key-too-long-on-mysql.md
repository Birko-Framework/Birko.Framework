---
id: TASK-684
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-19
depends-on: [TASK-691]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# The translation table's unique key is too long for MySQL, and a fluent mapping cannot bound the columns

## Context

Spawned from [[TASK-680]] (2026-10-19), found by its live MySQL run. `Birko.Data.Localization.SQL` maps the unique key
`(EntityGuid, EntityType, FieldName, Culture)`. On MySQL 8.4 the three string columns become `VARCHAR(255)` (rule 37:
an index-key string is bounded to `IndexedStringColumnLength`), and under `utf8mb4` the key is
36×4 + 3×255×4 = **3 204 bytes** — over InnoDB's 3 072. Measured: `CREATE UNIQUE INDEX … ERROR 1071 (42000): Specified
key was too long; max key length is 3072 bytes`. Schema-ensure degrades and reports it (`IndexCreationFailures`,
rule 49), so on MySQL the key is absent and two racing writers can still store two rows for one key (SH-M074 stays
open there). SQLite, PostgreSQL and SQL Server (3×255×2 + 16 = 1 546 bytes, under its 1 700) hold the key.

The fix is to bound the key columns (an entity type or field name needs far less than 255 characters, a culture name
about 16), but the fluent path cannot: `ModelMapRegistry.ApplyToDatabase` documents that `HasMaxLength` is not applied,
because `AbstractField` has no length to assign after the table is loaded. The default model cannot carry SQL length
attributes either, since `Birko.Data.Localization` has no SQL dependency (TASK-680's decision).

`TranslationTableTests.A_provider_that_cannot_hold_the_key_reports_it` pins the reported failure on MySQL; invert it
when this lands (rule 56).

## Acceptance criteria

- [x] A fluent mapping can bound a string column's length, honoured by every SQL provider's DDL (or another way to
      bound the translation key columns is chosen and written down)
- [x] `EntityTranslationMapping` bounds `EntityType`, `FieldName` and `Culture` so the key fits MySQL's 3 072 bytes
- [x] On live MySQL the unique key exists, a second create of one key is refused, and the race test leaves one row;
      the MySQL reported-failure test is inverted and the skipped key tests run there
- [x] An existing MySQL table without the key is reported (schema-ensure never alters an existing column; say so in the
      README)

## Out of scope

- The value index — [[TASK-681]].

## Human test plan

N/A — asserted by the live MySQL suite.

## Outcome (2026-10-20)

All criteria met; measured live on SQLite, PostgreSQL 16, SQL Server 2022 and MySQL 8.4.

- **AC 1:** chosen and written down. A fluent `HasMaxLength` now reaches the DDL, split out as [[TASK-691]] (done,
  `85f66d7d`).
- **AC 2:** `EntityTranslationMapping` bounds `EntityType` and `FieldName` at 128 and `Culture` at 85 (decided by the
  user), as public consts. The key is 1 508 bytes on MySQL utf8mb4 and 698 on SQL Server.
- **AC 3:**
  - On MySQL the key exists, a second create is refused, both race tests leave one row, and the boundary race is
    retried in a new transaction.
  - `A_provider_that_cannot_hold_the_key_reports_it` is inverted into `…could_not_hold_the_key_holds_it_now` (rule 56).
  - `HoldsTheKey` / `RequireKey` are removed, because every provider holds the key (rule 53).
  - The catalogue reports the three lengths on MySQL, PostgreSQL and SQL Server, `DetectDrift` is clean (rule 63), and
    an 86-character `Culture` is refused there.
  - **Measured, as AC 3's reading required:** MySQL inside a caller's transaction does **not** retry in place. The
    refused INSERT leaves the transaction usable, but the re-read runs on the REPEATABLE READ snapshot taken before the
    other writer committed, so it finds no row and the refusal stands. MySQL's `RetriesInsideATransaction` is `false`.
- **AC 4:** an old-shape MySQL table (TASK-680's DDL, read from the live database) keeps `VARCHAR(255)`. Its key is
  reported in `IndexCreationFailures`, and `DetectDrift` names the three columns. The README gives the remedy: remove
  duplicates, `ALTER … MODIFY`, re-init.
- **Live tables:** the old-shape `BirkoEntityTranslations` and `BirkoTestTenantTranslations` are dropped once per
  database per process, before first use, never in a teardown (rules 7 and 61).
- **Mutation:** removing the three `HasMaxLength` calls turns 15 red: 11 MySQL tests (the key, races, inverted test,
  read-back, refusal, old table) plus read-back and refusal on PostgreSQL and SQL Server.
- **Tests:** SQL 116/116 live (+4 per provider, minus the removed key gates); unit suites unchanged.
- **Close review** (8 findings, all fixed):
  - The catalogue read is scoped to the connection's schema.
  - PostgreSQL / SQL Server upgrades are documented: an existing table there now drifts, with a remedy per provider for
    any table name.
  - MySQL refuses only under strict `sql_mode`; without it, it truncates.
  - The refusal test asserts a `DbException` in the chain, not any exception.
  - The old-table test pins exactly the three drifting columns.
  - The Culture comment no longer claims nothing exceeds 85.
  - The table drop states why it cannot hit a sibling.

## Implementation plan

_Drafted 2026-10-20 (Plan agent). Decided by the user: split the fluent length into [[TASK-691]], which goes first;
a length above an index cap behaves as the attribute does; `Culture` is bounded at 85._

**AC 3, as read here:** "the skipped key tests run there" includes the boundary race test, which is also gated on
`RetriesInsideATransaction`. Measure what InnoDB does with a refused INSERT inside a transaction, and set MySQL's value
from that (rule 54).

1. **`EntityTranslationMapping`:** add consts `MaxEntityTypeLength = 128` and `MaxFieldNameLength = 128` (CLR simple
   names; SQL Server's `sysname`), and `MaxCultureLength = 85` (Windows `LOCALE_NAME_MAX_LENGTH`; stored values are
   `CultureInfo.Name`, such as `sk-SK` or `sk`, about 15 characters at most). Chain `.HasMaxLength(...)` on the three
   key columns.
   - MySQL utf8mb4: 144 + (128 + 128 + 85) × 4 = **1 508 ≤ 3 072**.
   - SQL Server: 16 + 341 × 2 = **698 ≤ 1 700**.
   - Over-long writes are refused by the database (MySQL 1406, SQL Server 2628, PostgreSQL 22001); SQLite does not
     enforce lengths.
2. **Tests flip:**
   - MySQL: drop `HoldsTheKey => false` and set `RetriesInsideATransaction` from the measurement.
   - Invert `A_provider_that_cannot_hold_the_key_reports_it` (rule 56).
   - Catalogue read-back of `CHARACTER_MAXIMUM_LENGTH` on MySQL, PostgreSQL and SQL Server, plus `DetectDrift` clean
     (rule 63).
   - One over-long `Culture` write is refused on MySQL.
   - Before first use, drop the old-shape live tables `BirkoEntityTranslations` and `BirkoTestTenantTranslations` once
     per process, behind a static gate, never in teardown (rules 61 and 7).
3. **Existing tables** (AC 4): a hand-made old-shape `LegacyTranslation` table on MySQL.
   - Assert `IndexCreationFailures` names its key.
   - Assert `DetectDrift` reports the three columns, declared 128/85 against stored 255.
   - The README remedy: remove duplicates, `ALTER TABLE … MODIFY` the three columns, and the next schema-ensure creates
     the key.
4. **Mutation:** remove the `HasMaxLength` calls → MySQL key, inverted and read-back tests red.
5. **Docs:** the Localization.SQL README / CLAUDE.md (bounds, byte math, existing tables never altered, the remedy,
   SQLite not enforcing lengths) and Recent Updates.
