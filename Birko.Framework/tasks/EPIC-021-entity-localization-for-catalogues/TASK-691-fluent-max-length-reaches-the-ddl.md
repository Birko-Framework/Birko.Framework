---
id: TASK-691
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] A fluent `HasMaxLength(n)` on a `string` property renders the same column as `[MaxLengthField(n)]` on every SQL
      provider, by one producer. The fluent value wins over an attribute, like `HasColumnName`.
- [ ] On an index-key column the declared length is rendered as written, never widened to `IndexedStringColumnLength`
      (rule 37). A length above a provider's index cap behaves as the attribute does today: the database refuses the
      index and schema-ensure reports it (decided by the user).
- [ ] `HasMaxLength` on a `byte[]` bounds it as `BinaryField.MaxLength` does. On any other type, `ApplyToDatabase`
      refuses it (rule 55).
- [ ] Applying twice is idempotent. A type's rename, flags and fluent indexes survive the cache refresh.
- [ ] Tests:
  - [ ] Birko.Models.SQL.Tests for each point above, with `MappingLayer_RetainsMetadataThatIsNotApplied` rewritten.
  - [ ] Connector column-type tests: a fluent-bounded index column renders `VARCHAR(n)` / `NVARCHAR(n)`.
  - [ ] A mutation (registration removed) turns them red.

## Out of scope

- The translation mapping's bounds and the MySQL key: [[TASK-684]].
- `HasPrecision` / `HasScale` are still accepted and dropped: [[TASK-692]].

## Human test plan

N/A — asserted by unit tests and, end to end, by TASK-684's live MySQL suite.

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
