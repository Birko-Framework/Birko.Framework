---
id: TASK-577
parent: STORY-051
feature: FEATURE-014
status: todo
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

- [ ] Every attribute type in `Birko.Data.SQL.Attributes` either round-trips through the rebuild with all its values, or the rebuild throws naming the type it cannot rebuild — no `default: skip`
- [ ] Values are read as fields or properties, whichever the attribute declares
- [ ] A test compiles or emits an attribute of a foreign identity and asserts the rebuilt column (length, precision, scale, UTC, unique index)
- [ ] A regression test fails on the current code and passes with the fix
- [ ] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- Moving the attributes into a single shared assembly (a packaging decision, not this fix)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
