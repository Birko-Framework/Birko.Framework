---
id: TASK-304
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-303, TASK-275, TASK-254]
findings: []
pr: null
github-issue: null
affects: [Birko.Data.SQL]
---

# `AbstractDatabaseModel`'s `[UniqueField]` on `Guid` forbids the composite key TASK-303 just enabled

## Found by measurement, at TASK-303's close

TASK-303 made a composite `PRIMARY KEY (a, b)` expressible and proved it live on TimescaleDB. Writing its
round-trip test then found that **the framework's own base model cannot use it**.

`Birko.Data.Models.AbstractDatabaseModel` (in `Birko.Data.SQL/Models/AbstractModel.cs`) declares:

```csharp
[UniqueField]
[PrimaryField]
public override Guid? Guid { get; set; } = null;
```

The `[UniqueField]` is a **standalone** `UNIQUE (Guid)` — separate from the primary key — so a subclass
adding a second key column gets a table where two rows may never share a Guid, whatever the composite
primary key says.

Measured on SQLite, with a `Reading : AbstractDatabaseModel` adding `[PrimaryField] DateTime Ts`:

```
Microsoft.Data.Sqlite.SqliteException : SQLite Error 19:
    'UNIQUE constraint failed: Readings.Guid'
```

— on the **second** insert, i.e. the first row that shares a Guid. Every composite-key test failed this
way until the fixture was rebased onto `Birko.Data.Models.AbstractModel` with its own `[PrimaryField]`.

## Why it matters

The shape TASK-303 exists for is `(Guid, Ts)` telemetry on TimescaleDB, and the natural way to write such
an entity is to derive from the framework's database base model. So the capability is real but its most
likely caller cannot reach it — the same "named an escape hatch that does not open" shape § Conventions
records under TASK-263, one level up.

The workaround is real and cheap (derive from `Birko.Data.Models.AbstractModel` and declare
`[PrimaryField] public override Guid? Guid`), which is why this is P2 rather than higher — but it is
undocumented, and a consumer would meet it as a runtime `UNIQUE constraint failed` rather than as a
compile-time or DDL-time refusal.

## The decision to make

1. **Suppress the standalone `UNIQUE` when the field is part of a composite key.** Mechanically small —
   `UsesInlineUniqueConstraint` and `DataBase.LoadIndexes`' synthesised index would both consult the same
   "is this a composite key member" answer TASK-303 added
   (`AbstractField.UsesInlinePrimaryConstraint`'s inverse). ⚠ But it **silently weakens a constraint the
   base class declares**, and § Conventions is emphatic that a weaker-than-declared constraint is the
   outcome to avoid (TASK-248 rejected prefix indexes for exactly this).
2. **Refuse the combination** with a message naming the workaround — honest, and consistent with
   TASK-303's autoincrement refusal, but it leaves the natural shape unreachable from the base model.
3. **Split the base model**: an `AbstractDatabaseModel` variant whose `Guid` is primary but not
   independently unique. Most explicit, and a new public type to justify.

Answer it from a measurement of how many entities rely on the standalone `UNIQUE (Guid)` being separate
from the primary key — on most providers `PRIMARY KEY` already implies uniqueness, so the `[UniqueField]`
may be redundant for every single-key entity, which would make option 1 far less risky than it looks.

## Acceptance criteria

- [ ] **Measure first**: does `[UniqueField]` on `Guid` add anything a `PRIMARY KEY` does not already
      guarantee, per provider? If it is redundant everywhere, say so with the emitted DDL — that changes
      which option is right.
- [ ] A decision recorded on the three options, with the measurement behind it.
- [ ] Whatever is chosen, an entity deriving from the framework's own base model can express `(Guid, Ts)`
      **or** is refused with a message naming the way to do it.
- [ ] A live TimescaleDB test with a base-model-derived entity, since that is the shape this is about.
- [ ] Proven able to fail.

## Out of scope

- The composite key itself — [[TASK-303]] shipped it, with a live TimescaleDB proof and a SQLite
  round-trip showing update and delete address one row using both key columns.
- `[UniqueField]` on any other column.

## Human test plan

- [ ] N/A — mechanical; the proof is a base-model-derived entity storing two rows that share a Guid.
