---
id: TASK-691
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-20
depends-on: []
blocks: [TASK-684]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A fluent `HasMaxLength` is accepted and dropped — make it reach the DDL

## Context

Split from [[TASK-684]] at plan time, 2026-10-20 (decided by the user). TASK-684 needs to bound the translation key
columns from a fluent mapping. The fluent path cannot do that today: `ModelMapRegistry.ApplyToDatabase` documents that
`HasMaxLength` is not applied.

A string's bound is the loaded field's runtime type. `AbstractField.CreateAbstractField` builds a `CharField(length)`
from `[MaxLengthField]` / `[MaxLength]` / `[StringLength]` / `[PrecisionField]`, and a `StringField` otherwise. Every
connector dispatches on `field is CharField`: MySQL `VARCHAR(n)`, SQL Server `NVARCHAR(n)`, PostgreSQL `VARCHAR(n)`,
SQLite `TEXT`. `ApplyToDatabase` mutates already-built instances, which cannot change type. Swapping the instance would
make a second producer, because `Table.Fields` and `DataBase._fieldsCache` share instances.

So the fluent door silently drops a declaration the attribute door honours (rule 55). Zero production mappings call
`HasMaxLength` today (grep), so nothing changes behaviour until one does.

## Acceptance criteria

- [x] A fluent `HasMaxLength(n)` on a `string` property renders the same column as `[MaxLengthField(n)]` on every SQL
      provider, by one producer. The fluent value wins over an attribute, like `HasColumnName`.
- [x] On an index-key column the declared length is rendered as written, never widened to `IndexedStringColumnLength`
      (rule 37). A length above a provider's index cap behaves as the attribute does today: the database refuses the
      index and schema-ensure reports it (decided by the user).
- [x] `HasMaxLength` on a `byte[]` bounds it as `BinaryField.MaxLength` does. On any other type, `ApplyToDatabase`
      refuses it (rule 55).
- [x] Applying twice is idempotent. A type's rename, flags and fluent indexes survive the cache refresh.
- [x] Tests:
  - [x] Birko.Models.SQL.Tests for each point above, with `MappingLayer_RetainsMetadataThatIsNotApplied` rewritten.
  - [x] Connector column-type tests: a fluent-bounded index column renders `VARCHAR(n)` / `NVARCHAR(n)`.
  - [x] A mutation (registration removed) turns them red.

## Out of scope

- The translation mapping's bounds and the MySQL key: [[TASK-684]].
- `HasPrecision` / `HasScale` are still accepted and dropped: [[TASK-692]].

## Human test plan

N/A — asserted by unit tests and, end to end, by TASK-684's live MySQL suite.

## Outcome (2026-10-20)

All criteria met.

- **Mechanism.** `DataBase.RegisterMaxLength` (Birko.Data.SQL) records a length per (model, property). `LoadField`
  appends it as a `MaxLengthField` after the property's own attributes, so `CreateAbstractField` stays the one producer
  and the fluent value wins.
  - `ModelMapRegistry.ApplyToDatabase` registers every `HasMaxLength` before any table loads.
  - A changed length drops the type's cached fields and table, and the same length again changes nothing. Measured:
    applying twice keeps the same field instance.
  - The rename, flags and fluent index survive the rebuild.
- **Index columns** (decided: like the attribute): a fluent length is rendered as declared (`VARCHAR(85)` /
  `NVARCHAR(85)`). An unlengthed index-key column keeps rule 37's 255.
- **Refused** (rule 55): a length that is not positive, a type other than `string` / `byte[]`, and a property that is
  no column (ignored, or without a setter).
- **Tests:**
  - Birko.Models.SQL.Tests 22 (FluentMaxLengthTests 13); connector column-type tests on MySQL and SQL Server.
  - Mutations red: registration pass removed (10 + MySQL 1 + MSSql 1), loader ignoring the registration, same length
    dropping the caches, lambda ignoring the model type, no base-type walk, non-positive skipped, non-column accepted
    (2), ambiguous lookup.
  - Regression: every test project importing Birko.Models.SQL is green (24 suites, live where gated), plus
    Birko.Data.SQL.Tests 723/723.
  - One intermittent failure: MySQL `EscapeAnomalyDiscriminationLiveTests.TASK295_a_vanished_table_heals…` failed once
    in three runs and passed on rerun. It is attribute-mapped and drops tables mid-run, which fits TASK-306
    (parallel live classes against one database), and it is not affected by this change.
- **Close review** (9 findings).
  - Fixed:
    - `HasMaxLength(0)` was skipped; it is now refused.
    - A registration did not reach derived types: the lookup now walks base types, most derived first, as an inherited
      attribute would.
    - A field built from a lambda over a base-declared property missed the model's length: `GetField` /
      `GetFieldFromLambda` now pass the lambda's model type.
    - Get-only and ignored properties are refused.
    - A property hidden with `new` threw `AmbiguousMatchException`.
    - A stale test comment, and the README / CLAUDE examples that bounded strings with `HasPrecision(256)`, now use
      `HasMaxLength`.
  - Documented, as `RegisterTableName` already behaves: a registration is process-wide and meant for startup; a change
    drops what other registrations wrote onto the cached table; a load racing a change can cache the old field. There
    is no unregister.
  - At the `DataBase` level the registry does not type-check; `ModelMapRegistry` does.
- **Spec:** schema-index-and-ddl regenerated, and the base-type / lambda lookup added after the fix. Map additions for
  `Birko.Models.SQL/Mapping` are left to TASK-689.

## Implementation plan

From TASK-684's plan (Plan agent, 2026-10-20):

1. **Birko.Data.SQL, one producer.** Add `DataBase.RegisterMaxLength(Type model, string property, int length)` next to
   `RegisterTableName`.
   - It stores the length in a `ConcurrentDictionary<(Type, string), int>`.
   - Only when the value changes does it drop `_fieldsCache` / `_tableCache` for that type.
   - `LoadFields(type)` appends a synthesised `MaxLengthField(n)` after the property's own attributes, so
     `CreateAbstractField`'s last-wins rule makes the fluent value win.
   - No connector, View or migrations change; `DetectDrift` picks it up through `ConvertType`.
2. **Birko.Models.SQL `ApplyToDatabase`.** A pass after table-name registration and before `LoadTable` registers every
   `MaxLength > 0`.
   - `string` and `byte[]` are honoured; anything else throws `InvalidOperationException` naming the type and property.
   - Update the XML docs on `FieldBuilder.HasMaxLength` and `ApplyToDatabase`.
3. **Tests:**
   - Models.SQL: a `CharField.Lenght` of n, fluent over attribute, `byte[]`, the `int` refusal, idempotence,
     rename/flags/indexes surviving the cache drop, an untouched type keeping its field instances, and the rewritten
     retained-metadata test.
   - Connector tests extending `IndexedStringColumnTypeTests`.
   - Mutation.
4. **Docs:**
   - Birko.Models.SQL CLAUDE.md / README: `HasMaxLength` is applied; `HasPrecision` / `HasScale` are not, and
     `HasPrecision` on a string is not a length on the fluent path.
   - Birko.Data.SQL CLAUDE.md: the `RegisterMaxLength` channel.
   - Recent Updates.

Risk: `ApplyToDatabase` must run before any store loads the type, as `RegisterTableName` already requires. Document it.
