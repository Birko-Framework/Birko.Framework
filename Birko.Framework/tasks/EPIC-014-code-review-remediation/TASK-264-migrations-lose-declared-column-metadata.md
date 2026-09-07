---
id: TASK-264
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-257]
findings: []
pr: "Birko.Data.Migrations.SQL 2e49513 + Birko.Data.Migrations.SQL.Tests eb91184"
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

- [x] `FieldDescriptor.MaxLength` reaches the emitted column on all four providers; a migration's
      `maxLength: 50` produces `NVARCHAR(50)` / `VARCHAR(50)` and not the unbounded type.
- [x] A migration-declared index over an unlengthed string is either built successfully or refused
      **loudly** — decide which and record the reason. If bounding, reuse
      `IsInIndexKey`/`IndexedStringColumnLength` rather than inventing a second answer.
- [x] The MSSql unique/primary migration column is covered by a test (needs the
      `Birko.Data.SQL.MSSql` import added to a test project, or a new one).
- [x] A test that takes the **live** branch, not a fallback. TASK-247 found six tests in this project
      exercising a `connector == null` branch nothing ships; check which branch the fixture selects
      before trusting a green result.
- [x] Proven able to fail — a named revert per gap, with counts.

## Out of scope

- Typing an attribute-mapped entity's columns — TASK-257 owns that and is done.
- MySQL's `IsUnique`/`IsPrimary` hole — TASK-265.

## Implementation plan

### Step 0 — measured 2026-09-07, before a line changed. Two premises moved.

**`SchemaField` forwards 5 of `FieldDescriptor`'s 15 properties.** Nine are dropped, and they do not all
matter equally:

| dropped | measured consequence | verdict |
|---|---|---|
| `MaxLength` | `ConvertType` needs `field is CharField` to see a length; `SchemaField` is not one, so a declared `maxLength: 50` emits the unbounded type | **fix** (filed) |
| `Precision` / `Scale` | same shape, **worse outcome**: `field is DecimalField && Precision != null && Scale != null` fails, so `precision: 18, scale: 2` emits bare `DECIMAL` — `DECIMAL(18,0)` on SQL Server, i.e. **money silently truncated to whole units** | **fix** — unfiled, same method, same cause |
| `IsIgnored` | an ignored field still gets a column | **fix** — one line |
| `ColumnName` | silently ignored; the column is always named `Name` | **fix** — one line |
| `DefaultValue` | **no connector emits `DEFAULT` at all** — grepped, zero hits in any connector | **file**: a knob the mechanism cannot deliver (§ TASK-296), and adding the DDL is a feature |
| `IndexName` / `IndexOrder` / `IndexDescending` | consumed **nowhere in the framework** — the only references are their own declarations | **file**: inert API |

**⚠ Premise 1 corrected — the index failure is NOT silent.** The task states it is *"recorded on
`IndexCreationFailures` and silent (TASK-204)"*. Measured in `AbstractConnector_Create.cs`:
`CreateIndexes` catches **only** `IsIndexAlreadyExistsException` (MySQL 1061). Msg 1919 is a different
error, so it **propagates**; the recording lives in *schema-ensure's* per-index catch, which the
migration path does not go through. TASK-204's own contract says an explicit call still throws, and it
does. So criterion 2's "refused loudly" is already the shipped behaviour.

**⚠ Premise 2 corrected — `IsIndexed` cannot be set here, and does not need to be.**
`SqlCollectionBuilder` (CREATE TABLE) and `SqlIndexBuilder` (CREATE INDEX) are separate nested classes
with separate `Build()` calls and no shared state — possibly in different migrations entirely. So the
column builder cannot know an index will later target its column, and `DataBase.LoadIndexes`' trick
(see the whole entity's attributes at once) has no analogue. **Fixing `MaxLength` resolves the case that
matters**: a declared length yields an indexable column, and a migration that declares no length gets
the unbounded type and, if it then declares an index, a loud Msg 1919 that says so. That asymmetry is
honest and is the answer to criterion 2 — *built when a length is declared, loud when not* — and it
matches what the author must do on MySQL regardless.

### The fix — one producer, mirroring `CreateAbstractField`

`SchemaField` becomes a **factory** returning the field subclass the connector can actually read:

- `FieldType.String` with a positive `MaxLength` (falling back to `Precision`, as `CreateAbstractField`
  does for backwards compatibility) → a bounded char field
- `FieldType.Decimal` with both `Precision` and `Scale` → a decimal field carrying them
- otherwise → the existing plain `SchemaField`

Every subclass keeps the no-op `Read` override, because a schema-only field has `Property = null!` and
`CharField.Read`/`DecimalField.Read` would dereference it. All three construction sites in
`SqlSchemaBuilder` (lines 110, 116, 278) go through the factory, so a fourth is correct without being
told — § TASK-295's rule.

`IsIgnored` is filtered where "which fields become columns" is decided (`Build()`), matching the
`[IgnoreField]` / `[NotMapped]` check `CreateAbstractField` already performs before its dispatch, rather
than by a factory returning null (§ SH-H037: a mapper refuses, it does not hand back null).

### Tests

- The migrations test project imports SQLite and PostgreSQL but **not MSSql** — that is criterion 3's
  missing import, so `Birko.Data.SQL.MSSql.projitems` is added.
- Criterion 4 (take the live branch) is **structurally satisfied since TASK-247** made the connector
  required — there is no `connector == null` branch left to take. Verify rather than assume.

### Proven able to fail — one revert per gap

`MaxLength` dropped · `Precision`/`Scale` dropped · `IsIgnored` unfiltered · `ColumnName` ignored ·
the factory bypassed at one of the three sites.

### Out of scope

- **`DefaultValue` and the three `Index*` properties** — spawned rather than absorbed; both need work
  this task's criteria do not describe (DDL support, and an index the framework never emits).
- Typing an attribute-mapped entity's columns — TASK-257, done.

## Human test plan

- [x] N/A — mechanical; the proof is a migration producing the declared column type and index against a
      real server.

---

## Closed 2026-09-07 — and Step 0 moved two of this task's own premises

### What was measured, before a line changed

`SchemaField` forwarded **5 of `FieldDescriptor`'s 15** properties. The connectors read a column's size
off the field's *runtime type* — `field is CharField` before a length, `field is DecimalField && Precision
!= null && Scale != null` before a precision — and `SchemaField` derived straight from `AbstractField`, so
it satisfied neither test. Emitted DDL, all four providers:

| descriptor | SQLite | PostgreSQL | MSSql | MySQL |
|---|---|---|---|---|
| `maxLength: 50` **was** | `TEXT` | `TEXT` | `NVARCHAR(MAX)` | `LONGTEXT` |
| `maxLength: 50` **now** | `TEXT` | `VARCHAR(50)` | `NVARCHAR(50)` | `VARCHAR(50)` |
| `precision: 18, scale: 2` **was** | **`REAL`** | `NUMERIC` | `DECIMAL` | `DECIMAL` |
| `precision: 18, scale: 2` **now** | `NUMERIC(18,2)` | `NUMERIC(18,2)` | `DECIMAL(18,2)` | `DECIMAL(18,2)` |

### ⚠ The decimal half was not in this task, and it is the worse one

Same method, same cause, unfiled. A bare `DECIMAL` has default scale **0** on SQL Server and MySQL, so a
declared money column was **truncated to whole units**; on **SQLite — this framework's default
provider** — an unqualified decimal falls back to **`REAL`**, so a column declared `DECIMAL(18,2)` held
binary floating point. Both silent. Fixing `MaxLength` and leaving this would have been § TASK-207's
*"re-keying half a dictionary is not a fix, it is a narrower bug"*.

Two more dropped properties had a visible consequence and went in the same change: **`ColumnName`**
(silently ignored, so a migration that named its column got the logical name) and **`IsIgnored`** (an
ignored descriptor got a column anyway).

### ⚠ Two premises in this task file were wrong, and measuring inverted both

- **"recorded on `IndexCreationFailures` and silent (TASK-204)"** — no. `CreateIndexes` catches only
  `IsIndexAlreadyExistsException`, which the base returns **`false`** for and which **MSSql does not
  override** (only MySQL does, for 1061). So on MSSql that filter cannot match *any* exception and Msg
  1919 propagates. The recording lives in *schema-ensure's* per-index catch, which the migration path
  never enters. Criterion 2's "refused loudly" was already the shipped behaviour.
- **"`IsIndexed` is never set"** — true, and it **cannot** be. `SqlCollectionBuilder` and
  `SqlIndexBuilder` are separate nested classes with separate `Build()` calls and no shared state, often
  in separate migrations, so at `CREATE TABLE` time nothing knows an index is coming;
  `DataBase.LoadIndexes`' trick of seeing a whole entity's attributes at once has no analogue here.

**So criterion 2 is answered *built when declared, loud when not*:** a declared length yields an
indexable column, and an undeclared one yields the unbounded type plus a loud failure if an index
follows. That asymmetry is honest, needs no cross-builder state, imposes no ceiling — and it is what the
author must do on MySQL regardless. The one shape where the information *is* available at column time is
an inline `FieldDescriptor.IndexName`, which nothing reads: [[TASK-299]].

### The fix

`SchemaField.For` is the one producer, mirroring `CreateAbstractField`'s dispatch **including its
`MaxLength`-then-`Precision` fallback for strings**, so the two paths cannot disagree about what a length
is. Two new subclasses (`SchemaCharField : CharField`, `SchemaDecimalField : DecimalField`) carry the
metadata and keep the no-op `Read`, because a schema-only field has `Property = null!` and the base
`Read` would dereference it. All three construction sites go through the factory, so a fourth is correct
without being told (§ TASK-295). `IsIgnored` is filtered where *which fields become columns* is decided,
matching the `[IgnoreField]`/`[NotMapped]` check `CreateAbstractField` performs before its own dispatch.

Fourth instance of § TASK-245's *"when you find the same statement written three times, look for the
field that gets lost on the way in"* — after `SqlIndexManager` (TASK-245), `SqlIndexBuilder.Build()`
(TASK-246) and the six index builders (TASK-274).

### Measurements

`Birko.Data.Migrations.SQL.Tests` **87 passed** (54 → 87, +33), 0 failed, 0 skipped. Adjacent suites
that import this project, both unaffected and green: `Migrations.TimescaleDB` **81**,
`SQL.View.Migrations` **14** — **183 total, 0 failed, 0 skipped**. Build clean, 0 warnings.

**Mutations, five, one per gap:**

| mutation | red |
|---|---|
| drop the bounded-string branch | **12** |
| drop the decimal branch | **7** |
| stop honouring `IsIgnored` | **2**, disjoint |
| ignore `ColumnName` | **2**, disjoint |
| bypass the factory at the `AddField` site only | **1** — exactly `AddField_carries_the_declared_metadata_too` |

The last is the useful one: it isolates a single construction site, proving the `ALTER TABLE ADD` path
needed wiring independently of `CREATE TABLE`. The `ColumnName` tests also fall under the maxlength
mutation because they declare a length to assert against; the reverse is not true, so `ColumnName` is
cleanly isolated in one direction.

### Acceptance

- [x] `MaxLength` reaches the emitted column — on **three** providers, not four, and SQLite's `TEXT` is
      asserted as correct rather than as a gap: SQLite has no length-enforcing string type, so a
      `VARCHAR(50)` there is TEXT affinity with the number ignored. The criterion's "all four" was
      optimistic; pinning `TEXT` is what stops a later reader "fixing" it into a divergence.
- [x] The index case decided and recorded — **built when a length is declared, loud when not**, with the
      "loud" half verified from the type system rather than from prose.
- [x] MSSql covered — `Birko.Data.SQL.MSSql.projitems` imported (the project had SQLite and PostgreSQL
      only, so MSSql column typing was untestable here). MySQL added too, so "all four" is real.
- [x] The tests take the **live** branch — verified, not assumed: TASK-247 made the connector required
      and `SqlSchemaBuilder` throws `ArgumentNullException` for a null one, so no `connector == null`
      branch survives.
- [x] Proven able to fail — five named mutations with counts, above.

### ⚠ Deliberately not done

- **No live MSSql/MySQL server run.** This fix's entire effect is the emitted DDL string, and that is
  asserted exactly per provider offline. The two provider facts it *relies* on are already live-measured
  in this repo: that `NVARCHAR(255)`-class columns are indexable while `NVARCHAR(MAX)` is Msg 1919
  (TASK-257, on 16.0.4265.3) and that an explicit `CreateIndexes` throws (TASK-204). Adding a live
  end-to-end would need an MSSql fixture this project does not have — real work beyond the criteria — and
  would test SQL Server's parser rather than this change.
- **`DefaultValue`** — accepted by `WithField` and **no connector emits `DEFAULT` at all**. A knob the
  mechanism cannot deliver (§ TASK-296), and adding the DDL is a feature: [[TASK-298]].
- **`IndexName` / `IndexOrder` / `IndexDescending`** — read by nothing in any backend, so an inline index
  declaration produces a column and no index, silently: [[TASK-299]].
- **A composite `PRIMARY KEY` via migrations** still needs connector support — pre-existing, recorded by
  TASK-247, untouched.
- **⚠ A fixture note on my own process:** a throwaway `TempProbe.cs` used to measure the DDL table above
  survived a failed heredoc and was still in the build when I first reported **88** tests. It was never
  staged, but the count was wrong by one; removed, re-measured at **87**, and the test commit message
  corrected before it went anywhere. The mutation red-counts are unaffected — that probe carries no
  assertions.
