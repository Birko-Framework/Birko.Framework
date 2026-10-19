---
id: TASK-680
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-168, TASK-194]
blocks: [TASK-681, TASK-682]
findings: [SH-M074]
pr: null
github-issue: null
jira-key: null
---

# The translation model is fixed and unkeyed — make it the consumer's type, with a unique key

## Context

Read in source 2026-10-19 (Symbio session, for its STORY-081):

- The wrappers are typed on `EntityTranslationModel : AbstractModel`. A multi-tenant consumer cannot scope or purge
  translations with its tenant machinery (Symbio's tenant filter, soft delete and tenant purge act on `TenantEntity`
  rows); the rows are global. Symbio needs its translation rows to be its own tenant entity.
- Nothing prevents two rows for one `(EntityGuid, EntityType, FieldName, Culture)`: writes read, then create or update,
  per field, with no key — two concurrent writers can both create. Reads then pick one arbitrarily.

**Also remediates SH-M074** (handed over by [[TASK-168]], confirmed 2026-10-19): the upsert in `LocalizedTranslationRows.Save` / `SaveAsync` (the one producer since TASK-168) reads, then creates when absent, with no key; reads then keep whichever duplicate is enumerated last.

**Left for this task by [[TASK-679]]:** list reads now fetch translations as `guids.Contains(t.EntityGuid) && …` in
batches of `LocalizedTranslationRows.ReadBatchSize` = 500 GUIDs, chosen to stay under MSSQL's ~2100 parameters. That is
**unmeasured on SQL** — `EntityTranslationModel` has no SQL mapping yet, so no SQL store can hold it. Once this task
puts the model on SQLite / PostgreSQL, run a `ReadMany` over more than 500 GUIDs there and confirm the `IN` list
renders and fits; adjust the constant on measurement (rule 54).

## Acceptance criteria

- [x] The four wrappers (and TASK-682's API) are generic over `TTranslation : AbstractModel, IEntityTranslation, new()`,
      so a consumer supplies its own model (e.g. one deriving from its tenant base); `EntityTranslationModel` stays the
      default and existing call sites compile unchanged (a non-generic alias or default overload).
      _Rescoped before the work, decided by the user 2026-10-19: the original `TTranslation : EntityTranslationModel`
      cannot serve Symbio — its model must derive from `TenantEntity`, and C# allows one base class._
      — Met for the four wrappers. TASK-682's API does not exist yet; it builds on the same `TTranslation` constraint.
      Delivered looser than written: no `new()` (close review — rows come from the store's `CreateInstance()`, so a
      model with `required` members also fits); the members must be public properties, checked at construction.
- [x] The model declares a **unique** index on `(EntityGuid, EntityType, FieldName, Culture)`, created by schema-ensure
      on the SQL providers, measured on SQLite and PostgreSQL.
      _Rescoped before the work, decided by the user 2026-10-19: the non-unique `(EntityType, FieldName, Culture, Value)`
      index moved to TASK-681. Indexing `Value` bounds it to 255 characters on MySQL and SQL Server (rule 37: those
      providers refuse TEXT/MAX as an index key) and to ~2.7 KB on PostgreSQL's btree, so every translation — long
      descriptions included — would be capped to serve a lookup only slugs need._
- [x] A concurrent double-write of one key yields one row (the second updates or fails loudly — the choice is written
      down), asserted on a real database; mutation-proven by removing the unique index.
- [x] README: how a tenant-aware consumer plugs in its own model.

## Out of scope

- Value-length limits for `Value` on indexed providers — note the index's provider limits; decide if a test shows a problem.

## Human test plan

N/A — asserted by tests.

## Implementation plan

Drafted 2026-10-19 at pick, after three decisions by the user: an **interface** for the model, a **new
`Birko.Data.Localization.SQL`** project for the SQL declaration, and a double write that **re-reads and updates**.

1. **`IEntityTranslation`** (`EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value`, `UpdatedAt`) in
   `Birko.Data.Localization/Models`; `EntityTranslationModel` implements it.
2. **Generic wrappers**: `LocalizedStoreWrapper<TStore, T, TTranslation>` (and the three twins) with
   `TTranslation : AbstractModel, IEntityTranslation, new()`; the existing two-parameter classes become subclasses
   fixed to `EntityTranslationModel`, so every call site compiles unchanged. `LocalizedTranslationRows` is generic
   over `TTranslation` and builds its filters with `Expression.Property` on the **concrete** type, never through the
   interface, so provider translators see the mapped property (a member access through an interface is what a
   column lookup by declaring type can miss). `EntityTranslationFilter` stays for the default model.
3. **Unique-key race (SH-M074)**: the upsert's create catches the store's duplicate-create refusal (the
   `StoreException` the bulk stores raise when a unique index rejects a create, TASK-625), re-reads the row by the
   four-part key and updates it. One row, last write wins, no exception. Any other exception propagates.
4. **`Birko.Data.Localization.SQL`** (new, via `new-birko-subproject`), on the `Birko.Security.Grants.SQL` pattern:
   `EntityTranslationMapping<TTranslation> : IModelMapping<TTranslation>` — table (default `BirkoEntityTranslations`),
   Guid primary, the four key columns required, a **unique** index `UX_{table}_Key` over
   `(EntityGuid, EntityType, FieldName, Culture)` (the value index went to TASK-681, see criterion 2); `LocalizationSqlMappings.Register(registry)` for the default model and
   `Register<TTranslation>(registry, table)` for a consumer's. Registered in `.slnx`, `.code-workspace`, aggregator
   csproj, CLAUDE-projects catalogue + dependency flow, README, docs.
5. **Tests**: `tests/Birko.Data.Localization.SQL.Tests` on **SQLite**: schema-ensure creates both indexes (read from
   the catalogue, rule 63); a concurrent double write of one key leaves one row with the last value; mutation —
   drop the unique index from the mapping and the double write leaves two rows; a `ReadMany` over 1 001 GUIDs runs and
   returns the right rows (TASK-679's batch measured). **PostgreSQL**: the same suite live on Docker per
   `live-tests.yml`. The unit suite gets the interface/generic wrapper tests (a custom model that is not an
   `EntityTranslationModel`, the default still compiling) and the retry against a test double that enforces the key.
6. **README**: how a tenant-aware consumer plugs in its own model (implement `IEntityTranslation`, map it with
   `Register<TTranslation>`, wrap with the three-parameter wrapper).

Kept out, recorded: the value index (TASK-681); a `Value` length limit for the value index on providers that cap index key length (the task's
own Out of scope; measured, not decided ahead).

## Progress log

- 2026-10-19 — picked. Decisions by the user: an `IEntityTranslation` interface (criterion 1 rescoped: Symbio's model
  derives from `TenantEntity`), a new `Birko.Data.Localization.SQL` for the SQL declaration, a racing create retried as
  an update; then the value index moved to TASK-681 (criterion 2 rescoped: indexing `Value` caps every translation at
  255 characters on MySQL / SQL Server).
- 2026-10-19 — `IEntityTranslation`; the four wrappers are `X<TStore, T, TTranslation>` with `X<TStore, T>` fixed to
  `EntityTranslationModel` — all 188 existing tests compile and pass unchanged. `LocalizedTranslationRows` generic, its
  filters built with `Expression.Property` on the concrete type; the upsert catches `StoreUniqueViolationException`,
  re-reads the key and updates (rethrows when no row is there).
- 2026-10-19 — `Birko.Data.Localization.SQL` (`EntityTranslationMapping<TTranslation>`, `LocalizationSqlMappings`),
  registered in `.slnx`, `.code-workspace`, `live-tests.yml`, CLAUDE-projects (catalogue + dependency flow), README,
  `docs/dependencies.md`, `docs/localization.md`, the spec map, and the Sandbox aggregator (its own repo).
- 2026-10-19 — tests. Unit (`LocalizationCustomModelTests`, 5): a consumer model that is not an `EntityTranslationModel`
  (sync bulk + async single), the race retried (sync + async), a refusal with no row propagates. SQL
  (`Birko.Data.Localization.SQL.Tests`, 6 per provider): **SQLite, live PostgreSQL 16 and SQL Server 2022** — the key
  read from the catalogue, a second create refused, a racing writer → one row, a batched read of 1 001 GUIDs (TASK-679's
  batch measured: renders and returns every row on all three), a consumer model on its own table. 24/24 green.
  **MySQL 8.4 cannot hold the key**: 3 204 bytes over InnoDB's 3 072 (ERROR 1071) — schema-ensure reports it, a test
  pins that, spawned as **TASK-684**.
- 2026-10-19 — mutations. Unique index removed from the mapping (fresh tables): **13 red** — the four key tests on each
  of SQLite, PostgreSQL, SQL Server, and MySQL's report test. Retry catch disabled: the 2 unit race tests and the live
  race on all three providers red. Pin: the refusal-with-no-row test passes either way.
- 2026-10-19 — close-gate reviews (no blockers). **Correctness + conventions both found** the retry inside a caller's
  transaction unmeasured: measured on live PostgreSQL 16 the refused INSERT aborts the transaction and the re-read threw a
  raw `25P02` in place of the unique violation (rule 9); SQL Server retries in place. Fixed: a re-read that fails
  rethrows the original `StoreUniqueViolationException` (with the read failure in the message); new live test pins both
  (PostgreSQL → the typed violation, the other writer's row stands; SQL Server → commits, one row, ours), red with the
  wrapping removed. The same shape in the slug and default decorators spawned as **TASK-685** (EPIC-014).
  Also fixed: the SQLite teardown cleared every pool (rule 61) — now only its own; the mapping gate was a static per
  closed generic test class (rule 62) — moved to one non-generic gate, and the test models moved out of the generic class
  (a nested type is another CLR type per provider); `new()` dropped — rows come from the store's `CreateInstance()`;
  a model implementing `IEntityTranslation` explicitly is refused at construction (new test, red without the check); the
  table name is validated (plain identifier ≤ 56 characters, so the index name survives PostgreSQL's 63) and required
  for a consumer's model. Recorded, not registered: building filters on the concrete type is a precaution — the
  interface-access failure it guards against was not measured (rule 54).
  194 unit + 28 live (SQLite, PostgreSQL, SQL Server, MySQL) green, no warnings; Sandbox builds.
