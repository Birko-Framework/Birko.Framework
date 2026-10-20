# Birko.Data.Localization.SQL.Tests

## Overview
xUnit + FluentAssertions tests for `Birko.Data.Localization.SQL`, against real databases: a SQLite file per test class
instance, and PostgreSQL / SQL Server / MySQL when their `BIRKO_*_HOST` is set (`BIRKO_REQUIRE_LIVE` makes a missing
server a failure). Kept apart from `Birko.Data.Localization.Tests` so that suite stays free of the SQL stack.

## Project Location
`tests/Birko.Data.Localization.SQL.Tests/`

## Structure
```
Birko.Data.Localization.SQL.Tests/
├── TranslationTableTests.cs          — abstract per provider: the unique key read back from the catalogue (rule 63),
│                                       a second create refused, a racing writer → one row (deterministic: a
│                                       delegating store stores the competing row between our read and our create),
│                                       a batched read past one batch (TASK-679), batched writes counted, a race
│                                       on one key of a batch, chunked and per-culture deletes, the wrappers'
│                                       collection/filter/delete writes on SQL (TASK-690), the explicit API on
│                                       SQL: get/set/remove, find with fallback and read counts, exists with
│                                       except, localize (TASK-682), a consumer model on its own table
│                                       through the wrappers; on MySQL, the reported key failure (TASK-684)
└── ProviderTranslationTableTests.cs  — SQLite, PostgreSQL, MySQL, SQL Server subclasses
```

## Conventions
- The mapping registry writes process-wide state: it is applied once, under a lock.
- Live tables persist between runs, and schema-ensure never drops an index: drop `BirkoEntityTranslations` and
  `BirkoTestTenantTranslations` before a mutation run, or the live providers keep the index and stay green.
- Column names are compared lower-cased: PostgreSQL folds unquoted names (rule 17).
