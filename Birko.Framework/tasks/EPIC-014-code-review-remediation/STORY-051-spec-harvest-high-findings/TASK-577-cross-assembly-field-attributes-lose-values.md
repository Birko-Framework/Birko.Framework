---
id: TASK-577
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: [SH-H061]
pr: null
github-issue: null
jira-key: null
---

# A cross-assembly SQL field attribute is rebuilt without its value: lengths, precision and scale become 0, and `[UtcField]` is dropped

## Context

Found by the 2026-10-12 `/specs regen` of all 25 areas at `c50ed580` (`schema-index-and-ddl`); traced 2026-10-12.

When `Birko.Data.SQL` is compiled into more than one assembly (the split-aggregator layout CLAUDE.md recommends), a
model's attributes have a different CLR type identity, and `DataBase.LoadField` (`Birko.Data.SQL/SQL/DataBase_Field.cs:41-105`)
rebuilds them by short type name. Three losses, all silent:

- `MaxLengthField.MaxLength`, `PrecisionField.Precision` and `ScaleField.Scale` are public **fields**
  (`Attributes/Field.cs:57,67,77`), but the rebuild reads them with `GetProperty`, which returns null — so each is rebuilt
  as 0 (unbounded length, default precision).
- `UtcField` has no case and falls to `default`, so a UTC column becomes a wall-clock one.
- `IndexedField.IsUnique` is not copied, so a unique index is built non-unique. (Found while tracing; not in the harvest.)

Reach: 23 consumer source files use these attributes; the defect fires only in the multi-assembly layout.

## Acceptance criteria

- [x] Every attribute type in `Birko.Data.SQL.Attributes` either round-trips through the rebuild with all its values, or the rebuild throws naming the type it cannot rebuild — no `default: skip`
- [x] Values are read as fields or properties, whichever the attribute declares
- [x] A test compiles or emits an attribute of a foreign identity and asserts the rebuilt column (length, precision, scale, UTC, unique index)
- [x] A regression test fails on the current code and passes with the fix
- [x] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint) — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)

## Resolution (2026-10-15)

`DataBase.LoadField`'s rebuild moved into `RebuildForeignFieldAttribute` (`Birko.Data.SQL/SQL/DataBase_Field.cs`): one
case per field attribute, each value read through `ForeignValue` as a property or a field, whichever the attribute
declares, and `default` throws `FieldAttributeException` naming the type and its assembly. `[UtcField]` has its case;
`IndexedField` is rebuilt with `IsUnique`, `WhereNotNull` and `WhereNull`.

**The test is the real layout, not an imitation.** `tests/Birko.Data.SQL.Tests.ForeignAttributes` compiles
`Birko.Data.SQL/Attributes/Field.cs` and `Table.cs` into a second assembly, with a model carrying every attribute type.
`Birko.Data.SQL.Tests` references it with `Aliases="foreign"` (`CrossAssemblyFieldAttributeTests`, 8 tests). A
hand-emitted attribute would have encoded the test author's idea of the shape, and the shape (field vs property) is
exactly what the defect got wrong. A coverage test requires every concrete field attribute on the foreign model, so the
next attribute added without a rebuild case fails it.

Measured on the old code: 4 of 8 red. Length (`StringField`, not `CharField(64)`), precision/scale (0), `[UtcField]`
(`DateTimeField`), and the unknown-attribute refusal (skipped silently).

**The unique-index loss did not reproduce, and that is recorded rather than claimed.** The field-level rebuild did drop
`IsUnique`, but `CreateAbstractField` never reads `IndexedField`. Indexes come from `DataBase_Table.cs`, which has its
own reflective read and already copied `IsUnique` and the predicate lists (TASK-273). The table test was green before
and after. It stays as a pin, and the field rebuild now copies the values anyway, so the two paths agree.

## Out of scope

- Moving the attributes into a single shared assembly (a packaging decision, not this fix)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
