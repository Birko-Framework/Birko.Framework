---
id: TASK-264
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A migration's declared column metadata is dropped on the way to the connector

## Context — spawned by TASK-257's close gate

`Birko.Data.Migrations.SQL/Context/SchemaField.cs:10-13` builds an `AbstractField` from a
`FieldDescriptor` and forwards only some of it:

```csharp
public SchemaField(FieldDescriptor descriptor)
    : base(null!, descriptor.Name, MapFieldType(descriptor.Type),
           descriptor.IsPrimary, descriptor.IsRequired, descriptor.IsUnique, descriptor.IsAutoIncrement)
```

Two things never arrive:

1. **`FieldDescriptor.MaxLength` is never read.** So a migration's
   `WithField("Code", FieldType.String, maxLength: 50)` produces an **unbounded** column on all four
   providers — `NVARCHAR(MAX)` / `TEXT` / `LONGTEXT`. The declared bound is silently discarded.
2. **`IsIndexed` is never set**, because nothing resolves a migration's index declarations back to
   their fields the way `DataBase.LoadIndexes` does for attribute-mapped entities. On MSSql that means
   a separately-declared `SqlIndexBuilder` index over a plain string still targets an
   `NVARCHAR(MAX)` column and still fails with **Msg 1919**, recorded on `IndexCreationFailures` and
   silent (TASK-204). On MySQL the same shape is ERROR 1170.

**Third instance of TASK-245's rule** — *"when you find the same statement written three times, look for
the field that gets lost on the way in"*. TASK-246 was the second (`SqlIndexBuilder.Build()` dropped
`Unique`, so a migration's `.Unique()` built a plain index on all four providers).

`Birko.Data.Migrations.SQL.Tests` asserts nothing about either gap today.

## Why it matters

A migration is the one path a consumer uses to evolve a shipped schema, so a dropped bound is not
cosmetic: the column that the migration declared as `VARCHAR(50)` is created unbounded, and on MSSql any
index the migration then declares over it cannot be built at all. Both failures are silent.

## Also in scope — the untested composition TASK-257 left behind

TASK-257 made `MSSqlConnector.ConvertType` consult `AbstractField.IsInIndexKey`
(`IsIndexed || IsUnique || IsPrimary`). Because `SchemaField` *does* forward `IsUnique`/`IsPrimary`, that
change **fixed** a case nobody had filed: a migration declaring a `.Unique()` or primary string column
previously got `TEXT` on MSSql, and an inline `UNIQUE TEXT` fails the whole `CREATE TABLE` with 1919 — so
such a migration could never run there. It now emits `NVARCHAR(255)` and works.

That is **untested**, deliberately: no test project imports both `Birko.Data.SQL.MSSql` and
`Birko.Data.Migrations.SQL`, so covering it needs a `.csproj` change that was out of TASK-257's scope.
Add the import and the test here, since this task is already in that file.

## Acceptance criteria

- [ ] `FieldDescriptor.MaxLength` reaches the emitted column on all four providers; a migration's
      `maxLength: 50` produces `NVARCHAR(50)` / `VARCHAR(50)` and not the unbounded type.
- [ ] A migration-declared index over an unlengthed string is either built successfully or refused
      **loudly** — decide which and record the reason. If bounding, reuse
      `IsInIndexKey`/`IndexedStringColumnLength` rather than inventing a second answer.
- [ ] The MSSql unique/primary migration column is covered by a test (needs the
      `Birko.Data.SQL.MSSql` import added to a test project, or a new one).
- [ ] A test that takes the **live** branch, not a fallback. TASK-247 found six tests in this project
      exercising a `connector == null` branch nothing ships; check which branch the fixture selects
      before trusting a green result.
- [ ] Proven able to fail — a named revert per gap, with counts.

## Out of scope

- Typing an attribute-mapped entity's columns — TASK-257 owns that and is done.
- MySQL's `IsUnique`/`IsPrimary` hole — TASK-265.

## Human test plan

- [ ] N/A — mechanical; the proof is a migration producing the declared column type and index against a
      real server.
