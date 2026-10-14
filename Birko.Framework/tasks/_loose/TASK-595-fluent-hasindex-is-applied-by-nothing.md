---
id: TASK-595
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-033]
pr: null
github-issue: null
jira-key: null
---

# Fluent `HasIndex` is applied by nothing, so `Birko.Models.Users.SQL`'s lookup indexes likely exist on no database

## Context

Found 2026-10-14 while building `Birko.Security.Grants.SQL` (TASK-594), which measured it on SQLite: a model mapped
through `Birko.Models.SQL` gets its table, columns, primary key and `IsUnique()` constraints, but **no index from
`FieldBuilder.HasIndex(...)`**. Only `sqlite_autoindex_*` (the key and the unique constraint) appeared in
`sqlite_master`.

Why, from the code:
- `Birko.Models.SQL/Mapping/ModelMapRegistry.ApplyToDatabase` applies the table name, column name and the primary /
  unique / required / auto-increment flags. Its doc comment says `HasIndex` is "mapping metadata only".
- `Birko.Models.SQL/CLAUDE.md` says to declare indexes "via the model's SQL field attributes / migrations".
- The CR-L324 remediation (EPIC-014 STORY-027, batch BZ) **added `HasIndex` to `Birko.Models.Users.SQL`** on
  `UserRole.UserGuid` / `RoleGuid`, `UserTenant.UserGuid`, `RolePermission.RoleGuid`, `UserLogin.UserGuid` and the
  `UX_UserLogin_Provider_ProviderKey` composite, to fix "membership lookups would table-scan". It recorded them as
  "consumed by migrations". But `Birko.Data.Migrations.SQL` never reads `FieldDescriptor.IndexName`; a grep finds no
  reader. So the fix most likely does nothing, and the table scans it was meant to remove are still there (rule 51:
  a declaration nothing honours).

Not yet measured on the Users mappings themselves. Measure before fixing (rule 54).

`UX_UserLogin_Provider_ProviderKey` is named as a **unique** index. If it doesn't exist, two `UserLogin` rows can share
(Provider, ProviderKey), which is the lookup key of an external sign-in. That makes this more than a performance
issue: the external-login lookup pattern this framework's README teaches (find by provider + subject) assumes it is
unique.

`Birko.Security.Grants.SQL` sidestepped the gap: it creates its indexes through the provider's `IIndexManager`
(`GrantSqlIndexes.EnsureAsync`) instead of declaring them.

## Acceptance criteria

- [x] Measured on SQLite: the Users mappings' declared `HasIndex` indexes are present or absent in `sqlite_master`
      after `ApplyToDatabase` + store init. The finding is confirmed, or falsified and this task closed with the
      measurement (rule 54).
- [x] If confirmed: a fluent `HasIndex` reaches the DDL. Either `ApplyToDatabase` carries it into the SQL layer's
      index declarations, as the attribute path does, or the API stops offering it (rule 55: a builder honours a
      declaration or refuses it). The composite `order:` is preserved, and a unique index is created as unique.
- [x] `UX_UserLogin_Provider_ProviderKey` is shown to reject a duplicate (Provider, ProviderKey) on SQLite.
- [x] Docs corrected: `Birko.Models.SQL/CLAUDE.md`, the `ModelMapRegistry` doc comment, and the STORY-027 batch BZ
      note's "consumed by migrations".
- [x] Regression tests fail with the fix removed.
      _Done as the first option: `ApplyToDatabase` carries it. It needed a new `HasUniqueIndex` primitive, because
      `IsUnique` is single-column and there was no way to declare a unique composite. It also needed rule 37's
      `IsIndexed` marking, or MySQL / SQL Server would type the unbounded `Provider` as TEXT and refuse the index._

## Out of scope

- `Birko.Security.Grants.SQL`'s indexes. They are created through `IIndexManager` and stay that way unless this fix
  makes `HasIndex` the simpler producer, which would be a follow-up there.
- `HasMaxLength` / `HasPrecision` being metadata only too. That is the same mechanism, so a fix here should say
  whether it covers them. If it doesn't, they get their own task.

## Human test plan

N/A — fully covered by automated tests. Whether an index exists is read from the database catalogue.

## Implementation plan

_Populated by `/tasks plan TASK-595` — leave empty until then._

## Outcome

**Fix.** A fluent index now exists in the database. `ModelMapRegistry.ApplyToDatabase` writes each `HasIndex` into
the table's index declarations, which schema-ensure creates. Before, it was metadata nothing read: measured, every
`Birko.Models.Users.SQL` table had **zero** indexes though seven were declared, and the "consumed by migrations"
claim was false (no migration reads `IndexName`).
- `Birko.Data.Patterns/Schema/FieldDescriptor.cs`: `IndexUnique`.
- `Birko.Models.SQL/Mapping/FieldBuilder.cs`: `HasUniqueIndex(name, order, descending)`, the first way to declare a
  **unique composite** index fluently (`IsUnique` is single-column).
- `Birko.Models.SQL/Mapping/ModelMapRegistry.cs`: `ApplyIndexes`. It orders columns by `IndexOrder`, uses physical
  column names, sets `IsIndexed` (rule 37), refuses a uniqueness disagreement within one index, and is idempotent.
- `Birko.Models.Users.SQL/Mappings/UserLoginMapping.cs`: `UX_UserLogin_Provider_ProviderKey` is now a
  `HasUniqueIndex`. It is the lookup key of an external sign-in, and it now really rejects a duplicate.

**Step-6 split** (`tests/Birko.Models.Users.SQL.Tests/AppliedIndexTests.cs`):
- Removing the `ApplyIndexes` call fails 6 of 15: `UserLogin_LookupIndex_ReachesTheTable`,
  `UserLogin_NaturalKey_IsAUniqueCompositeIndex_InOrder`, `UserRole_ForeignKeyIndexes_ReachTheTable`,
  `OnSQLite_TheNaturalKeyRejectsADuplicateLogin_AndTheIndexesExist`, `MixingHasIndexAndHasUniqueIndex…_IsRefused`,
  `ApplyingTwice_DoesNotDuplicateIndexColumns`.
- Reverting `UserLogin` to a plain `HasIndex` fails 2 of 15: the uniqueness test and the SQLite duplicate test.
- Removing the `IsIndexed` marking fails 1 of 16: `FluentIndexColumns_AreMarkedIndexed…`.
- **Contract pins, not evidence:** the pre-existing `UserLoginMappingTests` / `UserMappingTests`. They check mapping
  metadata only, and passed while no index existed.
- 86 suites using `Birko.Models.SQL` or `Birko.Data.Patterns` are green, and the aggregator builds.

**Judgement calls.**
- **Applied in `ApplyToDatabase`, not refused.** The API's whole purpose is to declare the index, and the attribute
  path already creates the same `IndexDefinition`.
- **`HasUniqueIndex` added** rather than inferring uniqueness from a `UX_` name prefix. A name convention that
  silently changes semantics is the rule 55 failure this task exists to remove.
- **`HasMaxLength` / `HasPrecision` / `HasScale` stay metadata only.** They would have to be applied to an
  `AbstractField` whose type is fixed at load, which is a different mechanism; the docs now say so for those three only.

**Observable change (migration).**
- Databases mapped through `Birko.Models.*.SQL` (or any fluent mapping) get the declared indexes at the next
  schema-ensure of a created table.
- A `UserLogins` table that already holds duplicate (Provider, ProviderKey) rows cannot get the unique index. That
  is reported in the connector's `IndexCreationFailures` (lazy schema-ensure degrades and reports, rule 49) until
  the duplicates are resolved. Two rows for one external identity is the bug the constraint prevents.
- On MySQL and SQL Server, fluent-indexed unbounded strings are now emitted bounded (`VARCHAR(255)` /
  `NVARCHAR(450)`), as attribute-indexed ones already were. This affects new tables. Existing ones are reported by
  `DetectDrift`, not altered.

**Flagged, not fixed.**
- Live MySQL / SQL Server suites were not run here, only their offline DDL suites (green).
- `Birko.Security.Grants.SQL` could now declare its indexes with `HasIndex` instead of `GrantSqlIndexes.EnsureAsync`.
  That is optional, and recorded in its CLAUDE.md.

## Progress log

- step 2 — picked; the last of the two defects the user named (TASK-596 done first: silent wrong data outranks a missing index)
- step 3 — verified: held (0 indexes on every Users.SQL table after ApplyToDatabase); rescoped: a unique composite needs a new HasUniqueIndex primitive (IsUnique is single-column)
- step 4 — layer: local (Birko.Models.SQL, Birko.Data.Patterns)
- step 5 — fix in FieldDescriptor.cs, FieldBuilder.cs, ModelMapRegistry.cs, UserLoginMapping.cs; tests in tests/Birko.Models.Users.SQL.Tests/AppliedIndexTests.cs; 16/16, 86 dependent suites green; IsIndexed marking added after reading spec "A declared index marks its columns"
- step 6 — no ApplyIndexes: 6/15 failed; plain HasIndex on UX: 2/15; no IsIndexed: 1/16; contract pins = UserLoginMappingTests, UserMappingTests
- step 7 — respecced schema-index-and-ddl (added requirement: a fluent index reaches the table and marks its columns)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
