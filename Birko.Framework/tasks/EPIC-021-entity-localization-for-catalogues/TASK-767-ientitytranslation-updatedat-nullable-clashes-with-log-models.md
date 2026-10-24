---
id: TASK-767
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `IEntityTranslation.UpdatedAt` is `DateTime?`, so a translation model cannot derive from a log model

## Context

Found 2026-10-23 while planning Symbio TASK-945, the first consumer of EPIC-021. The Symbio side is blocked on this.

TASK-680 made the translation model consumer-supplied (`TTranslation : AbstractModel, IEntityTranslation`). The interface
declares `DateTime? UpdatedAt` (`Birko.Data.Localization/Models/IEntityTranslation.cs:28`). The SQL log model declares
`public override DateTime UpdatedAt` (`Birko.Data.SQL/Models/AbstractLogModel.cs:18`). Every consumer entity built on a log
model therefore cannot implement the interface. In Symbio that is every entity, because `BaseEntity` → `TenantEntity` sit on it.

The workarounds both fail:
- **Hiding the property with `new`** makes `Type.GetProperty("UpdatedAt")` ambiguous inside
  `LocalizedTranslationRows.RequireQueryableModel`.
- **Implementing the interface explicitly** is refused by that same check.

The framework's own test model (`tests/Birko.Data.Localization.SQL.Tests/TranslationTableTests.cs:30`) derives from a bare
`AbstractModel`, so this combination was never exercised.

**Decision (2026-10-24, decided by the user): option (a). `IEntityTranslation.UpdatedAt` becomes `DateTime`, and
`EntityTranslationModel` follows.** The options weighed:
- **(a) `DateTime` on the interface.** A model built on a log model satisfies it with no code. It breaks every model
  declaring `DateTime?` (the default model and the test models), and a stored row with a null `UpdatedAt` no longer
  hydrates. The writers always stamp it, so only rows written around them can be null. An existing SQL table keeps its
  nullable column.
- **(b) Off the interface, stamped through `ITimestamped`.** Any base model would work, but `EntityTranslationModel`
  (`DateTime?`, not `ITimestamped`) would need a special case or new columns that schema-ensure does not add to an
  existing table.
- **(c) The interface stays `DateTime?`, and the check accepts an explicit implementation of `UpdatedAt`.** Nothing
  breaks, but every log-model consumer writes a bridge line, and one member becomes an exception to the
  explicit-implementation rule.

Chosen because the only consumer, Symbio, has not shipped localization, so the break costs nothing today, while (c)'s
bridge would stay in every consumer. Nothing filters or orders on `UpdatedAt`; it is only written.

## Acceptance criteria

- [x] Choose and record here: `UpdatedAt` becomes `DateTime` on the interface; or it leaves the interface and the model owns
      its timestamp (the decorator stamps through `ITimestamped` when present); or the check accepts both.
- [x] A translation model deriving from the SQL `AbstractLogModel` (i.e. `DateTime UpdatedAt`) works through
      `EntityTranslations` end to end on SQLite: set, read back, remove. `RequireQueryableModel` accepts it.
- [x] `EntityTranslationModel` and the existing tests keep working. Mutation-proven: the new test fails against today's
      interface.
- [x] Docs of `Birko.Data.Localization` say which base models a translation model may derive from.

## Out of scope

- Soft delete on the translation chain stays refused (`RequireNoSoftDelete`, a user ruling). A consumer whose base model is
  `ISoftDeletable` hard-deletes translations through a chain without that wrapper; that is the consumer's wiring.
- The open-generic scan crash — TASK-766.

## Human test plan

N/A — covered by the tests; the consumer-side proof is Symbio TASK-945.

## Outcome (2026-10-24)

All criteria met, with option (a) (see Context).

- **Change.** `IEntityTranslation.UpdatedAt` and `EntityTranslationModel.UpdatedAt` are `DateTime`. The refused-create
  retry (`LocalizedTranslationRows.PlanRetry`) passes the row's stamp directly; it was `UpdatedAt ?? DateTime.UtcNow`,
  and every row reaching it was filled with a stamp anyway. `RequireQueryableModel` is unchanged: it compares each
  member's type with the interface's, which now matches the log model's.
- **Test.** `LogTranslation : AbstractDatabaseLogModel, IEntityTranslation` (the SQL log model, `DateTime UpdatedAt`
  inherited) is mapped with `LocalizationSqlMappings.Register<LogTranslation>` and driven by
  `TASK767_a_translation_model_on_a_log_model_sets_reads_and_removes_translations`: `RequireQueryableModel`, then
  `EntityTranslations<SqlProduct, LogTranslation>` sets a Slovak name, reads it back through the api and as the stored
  row (value, stamped `UpdatedAt`), and removes it. It passes on SQLite, and live on PostgreSQL 16, MySQL 8.4 and
  SQL Server 2022.
- **Mutation** (the three production files reverted to today's `DateTime?` and the test models with them, the new test
  kept): the test project **does not compile**, with one error, `CS0738: 'LogTranslation' does not implement interface
  member 'IEntityTranslation.UpdatedAt' … does not have the matching return type of 'DateTime?'`. The split: only
  `TASK767_…` depends on the fix; nothing else in the suite references `LogTranslation`. A compile error is the honest
  red here: against today's interface the model cannot be declared at all, which is the defect. Restored, 148/148.
- **Breaking, as chosen.** The default model and seven test models changed `DateTime?` to `DateTime`
  (`TenantTranslation`, `LegacyTranslation`, `SoftTranslation`, the InMemory custom and fallback models, the MongoDB
  boundary model, and the explicit-implementation model). `EntityTranslationModelTests.DefaultValues_AreEmptyStrings`
  now expects `default`, not null.
- **Measured consequence on an older SQL table (MySQL live).** A table created with a nullable `UpdatedAt` now reports a
  `NullabilityMismatch` on `updatedat` beside TASK-684's three type mismatches. This made
  `An_existing_table_with_unbounded_key_columns_is_reported_never_altered` go red live on MySQL, and it now lists the
  fourth drift with the reason. A throwaway probe (removed) inserted a row with NULL `UpdatedAt` into that table: the
  store's read threw `InvalidCastException: Unable to cast object of type 'System.DBNull' to type 'System.DateTime'`,
  so a NULL fails every read that includes the row. The Localization.SQL README documents the fill:
  `UPDATE <table> SET UpdatedAt = CURRENT_TIMESTAMP WHERE UpdatedAt IS NULL`. The wrappers always stamp the column, so
  only rows written around them can be NULL.
- **Regression.** Every test project importing `Birko.Data.Localization.projitems` is green offline (11 suites, 1 567
  passed). Birko.Data.Localization.SQL.Tests is green live on all four providers (148/148, `BIRKO_REQUIRE_LIVE=1`, local
  Docker per live-tests.yml). `dotnet build Birko.Framework.slnx`: 0 errors.
- **Docs.** Birko.Data.Localization README § 5 ("Which base model"), its CLAUDE.md, docs/localization.md (member list
  and type) say a translation model derives from `AbstractModel` or anything on it, a log model included. The
  Localization.SQL README's `TenantEntity` example no longer redeclares `UpdatedAt`, and gains the `ValueHash` it was
  missing; a section covers a table created before TASK-767.
- **Not measured / flagged:**
  - Document stores (EPIC-022: MongoDB, RavenDB, ElasticSearch, Cosmos DB, JSON, XML) were not run live. A stored
    document with a null `UpdatedAt` is expected to fail to deserialize into `DateTime` there too, as on SQL; unmeasured.
    The wrappers have always stamped it, so only documents written around them are affected.
  - The Birko.Data.Localization README § 5 says a tenant base's "soft delete … apply to translations too", which
    contradicts `RequireNoSoftDelete` (TASK-698). Pre-existing wording, out of scope here, left as it was.
  - Symbio verifies from its side (TASK-945); the Symbio repo was not touched.
- **Recent Updates:** entry added; the oldest (TASK-685) rolled into CHANGELOG.md, below TASK-686 of the same date
  (9 638 bytes after).

## Implementation plan

_Drafted when picked, after the choice in criterion 1._
