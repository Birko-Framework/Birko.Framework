---
id: TASK-684
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-19
depends-on: []
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

- [ ] A fluent mapping can bound a string column's length, honoured by every SQL provider's DDL (or another way to
      bound the translation key columns is chosen and written down)
- [ ] `EntityTranslationMapping` bounds `EntityType`, `FieldName` and `Culture` so the key fits MySQL's 3 072 bytes
- [ ] On live MySQL the unique key exists, a second create of one key is refused, and the race test leaves one row;
      the MySQL reported-failure test is inverted and the skipped key tests run there
- [ ] An existing MySQL table without the key is reported (schema-ensure never alters an existing column; say so in the
      README)

## Out of scope

- The value index — [[TASK-681]].

## Human test plan

N/A — asserted by the live MySQL suite.

## Implementation plan

_Populated by `/tasks plan TASK-684` — leave empty until then._
