---
id: TASK-112
parent: STORY-051
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-30
completed: 2026-08-08
depends-on: []
blocks: []
pr: d9637e6 (Birko.Data.SQL), ef71921 (Birko.Data.SQL.SqLite)
github-issue: null
jira-key: null
findings: [SH-H037]
---

# `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist

## Context

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs:235` — **CONFIRMED**.

`CreateAbstractField`'s type dispatch handles only `bool`, `DateTime`, `decimal`, `Guid`, `int`, `char`,
`string` (plus nullable variants) and `enum → int`. Every other CLR type falls to `return null`, commented
*"Unsupported type — skip, filtered by LoadField"*. `LoadField` (`DataBase_Field.cs:110`) turns that null
into `Array.Empty`.

So a `[Table]` model with `public long Ticks`, `public double Ratio` or `public byte[] Blob` gets a
`CREATE TABLE` **without those columns**. `Write()` never emits them and `Read()` never restores them —
**silent write-side data loss with no exception and no log entry.**

Two things make this worse than a missing feature:

- **`decimal` *is* mapped, so money is safe** — which is precisely why this has survived. The types that
  vanish are identifiers (`long`), measurements (`double`/`float`) and blobs (`byte[]`): exactly the
  properties whose loss you notice late.
- **The portable `FieldType` enum already names `Long`, `Double` and `Binary`.** Callers reading that enum
  reasonably expect support; the framework advertises the capability it drops.

## Approach

**Rescoped at step 3 (2026-08-08) — the defect holds exactly as described, but three-quarters of the work
this section predicted does not exist.** Verified by reading the four connectors:

- **The per-provider column types are already complete.** Every `ConvertType` already has arms for
  `DbType.Int16` / `Int64` / `Single` / `Double` / `Binary`, emitting precisely the types this section asked
  to add — SQLite `INTEGER`/`REAL`/`BLOB`, PostgreSQL `SMALLINT`/`BIGINT`/`DOUBLE PRECISION`/`REAL`/`BYTEA`,
  MySQL `SMALLINT`/`BIGINT`/`DOUBLE`/`FLOAT`/`LONGBLOB`, MSSql `SMALLINT`/`BIGINT`/`FLOAT`/`REAL`/
  `VARBINARY(MAX)`. The DDL half needs **no provider change**. The mapping was built for a `DbType` that
  `CreateAbstractField` could never produce.
- **Parameter binding needs no work either.** `AddParameter` sets `parameter.Value` untyped (it only
  normalises enums), so `long`/`double`/`float`/`short`/`byte[]` bind natively on all four providers.
- **`ModelMap<T>` does no type dispatch at all** — `ApplyToDatabase` patches an already-`LoadTable`d schema
  (name + primary/unique/required/autoincrement flags). It depends on `CreateAbstractField` rather than
  duplicating it, so there is **no divergence to close**; this task's fix covers the fluent path for free.

So the whole defect is confined to `CreateAbstractField`'s dispatch: the field classes that would carry
these `DbType`s were never written. The work is new `AbstractField` subclasses + reader materialisation +
wiring + fail-fast.

**One genuine per-provider defect found in the same family, taken in scope.** SQLite maps
`DbType.Single` → `INTEGER`, grouped with the integer types — the identical mistake PostgreSQL and MSSql
both fixed under CR-H087 (their arms carry the comment). It is inert today because nothing can produce a
`Single` field; the moment `float` maps, SQLite becomes the one provider that declares a float column as an
integer, and SQLite is the default test provider. Fixing it here rather than filing it, because shipping
`float` support that knowingly mis-declares its column on the reference provider is the same silent-wrong
-answer shape this task exists to remove.

**Decide explicitly what happens to a type that is still unsupported after this.** The current silent
`return null` is the actual defect: it means any future unmapped type repeats this bug. A model carrying a
property the mapper cannot express should **fail at table load**, the same way the
`[CompositeIndex]` work chose to fail fast on an unmapped property name. If a genuine opt-out is needed,
that is what an explicit `[NotMapped]`-style attribute is for — silence is not a design.

Check what `Birko.Models.SQL`'s `ModelMap<T>` does with these types too; if it can already map them, the two
paths disagree and that divergence should be closed in the same pass.

## Acceptance criteria

- [x] `long`, `long?`, `double`, `double?`, `float`, `float?`, `short`, `short?` and `byte[]` map to columns
      and round-trip through `Write()` / `Read()` end-to-end
- [x] Boundary values round-trip exactly: `long.MinValue` / `MaxValue`, `double` precision at the extremes,
      an empty `byte[]`, and a null `byte[]`
- [x] `CREATE TABLE` emits the correct column type on **all four** providers (SQLite, PostgreSQL, MySQL,
      MSSQL) — DDL asserted per provider
- [x] SQLite declares a `float` column as `REAL`, not `INTEGER` (the CR-H087 fix PostgreSQL and MSSql
      already carry) — added at step 3, see § Approach
- [x] A CLR type still unsupported after this change **throws at table load**, naming the property and its
      type, instead of silently producing no column
- [x] `decimal`, `int`, `Guid`, `DateTime`, `bool`, `string` and enum mappings are unchanged — asserted, so
      the type-dispatch rewrite cannot regress what worked
- [x] `FieldType.Long` / `.Double` / `.Binary` now correspond to something real
- [x] Regression tests in `Birko.Data.SQL.Tests` (DDL per provider) and `Birko.Data.SQL.SqLite.Tests`
      (round-trip, boundary values)
- [x] `/specs regen` for `schema-index-and-ddl`, spec diff reviewed

## Out of scope

- Migrating existing consumer tables to add the newly-mapped columns. A consumer whose model has a `long`
  today has a table with no such column; adding the mapping means their DDL and their live schema diverge.
  **This needs to be called out in the fix's `Recent Updates` entry** — it is the one part of this task that
  can break a running deployment, and it is a consumer decision, not a framework one.
- `SH-H038` (ES reindex reporting success with per-document failures) — same area, unverified, separate.
- Non-primitive complex types, collections, and `TimeSpan`/`DateTimeOffset` unless they fall out for free.

## Human test plan

N/A — covered by automated tests. Round-trip and DDL assertions cover it per provider; there is no visual
surface.

## Outcome

**A `[Table]` model with a `long`, `short`, `double`, `float` or `byte[]` property got a table without that
column — the value was dropped on every save and read back as the type's default, with no exception and no
log entry. Those five types now map to real columns and round-trip; a type that still cannot be mapped
throws at table load instead of disappearing.**

The whole defect lived in one method. `AbstractField.CreateAbstractField`'s type dispatch ended in
`return null` for anything it did not recognise, and `LoadField` turned that null into an empty field set.
Five new `AbstractField` subclasses (`Long`, `Short`, `Double`, `Float`, `Binary`, plus nullable variants
for the four value types) and their dispatch arms close it.

### What the step-3 re-verification changed

The finding held verbatim, but its **cost model was wrong**, and following it would have meant a large
pointless change. The Approach called the per-provider type mapping "the bulk of the work"; in fact all
four `ConvertType` implementations **already** had `Int16` / `Int64` / `Single` / `Double` / `Binary` arms
emitting exactly the column types requested, `AddParameter` binds untyped so no provider binding work was
needed, and `ModelMap<T>` does no type dispatch at all (it patches an already-loaded table), so there was
no divergence to close. The mapping had been built for a `DbType` the dispatch could never produce. Scope
went from four providers to one method plus five small classes.

### The step-6 split, and why it ran twice

**First run: 15 of the new tests passed with the fix reverted** — every per-provider DDL test. They
constructed `new LongField(...)` by hand, and the field classes survive a revert that only touches the
dispatch, so they were pinning the providers' `ConvertType` contract while appearing to witness this fix.
Rewritten to drive `DataBase.LoadTable(typeof(Model))` instead. Second run: **43 of 52 new tests fail on
the revert** (SQL 16/24, SqLite 12/13, and 5/5 in each of the four provider suites — up from 0/15).

The **9 that still pass are contract pins, not evidence**: six `AlreadyMappedType_IsUnchanged` cases,
`EnumStillMapsToInteger` and `DeliberateOptOut_StillExcludesTheColumnWithoutThrowing` exist precisely to
pass on both sides (they pin that the dispatch rewrite and the new throw regressed nothing), and
`NullablesLeftUnset_ReadBackAsNull_NotAsZero` cannot witness the defect at all — an unset nullable reads
back null whether or not the column exists.

### Judgement calls

- **Fail-fast on an unmapped type, as the task prescribed** — the silence was the deeper half of the
  defect, since it guaranteed the next unmapped type would repeat it. The gentler option (keep returning
  null, just add the five arms) was rejected for that reason. It is a **breaking change for consumers**:
  a model carrying an unmappable property now throws at table load where it previously loaded fine minus
  a column. Two mitigations made it acceptable — the escape hatches (`[IgnoreField]`, `[NotMapped]`)
  already existed and are checked *before* the dispatch, and the blast radius was measured rather than
  assumed: **19 SQL-touching suites, including all six `Birko.Models.*.SQL` domain suites, all green**, so
  no framework model carries one. The consumer-facing risk is real and is called out below.
- **`char?` now throws.** Not a type this task set out to map, and left unmapped — but the fail-fast
  changes its behaviour, and it was previously dropped in silence, which is the *same* data-loss shape as
  SH-H037. Pinned by `NullableChar_NowReportsItselfInsteadOfVanishing` and specced, rather than quietly
  mapped, because mapping it was not in this task's scope. Filed as follow-up.
- **The SQLite `Single` → `INTEGER` arm was fixed here rather than filed.** Same root-cause family
  (CR-H087, which PostgreSQL and MSSql both already carry), inert until this task gave `float` a field
  class, and SQLite is the reference/test provider — shipping `float` support that mis-declares its column
  on the provider every suite runs against would have been a new silent-wrong-answer.
- **`double` is not routed through `DecimalField`, and `long` not through `IntegerField`**, though both
  would have been fewer lines. Exact base-10 vs binary floating point are different column types on every
  provider, and an `Int32` column truncates silently past 2^31 — the defect class this task exists to
  remove. Asserted both ways.

### What the merge gate found

**The review caught a failure mode the fix would have introduced.** `DataBase.GetProperties` enumerates
*every* public instance property and does not filter **indexers** — so a model declaring
`public string this[string key]` would have hit the new throw and failed table load outright, where it
previously loaded fine. That is not an unmapped *type*: an indexer has no single value to store and cannot
even be read through `GetValue(obj, null)`, so no mapping could ever fix it and the model would have been
rejected for something unfixable. Indexers are now skipped before the dispatch, alongside the two opt-out
attributes. Step 6 was re-run for the new check (per the standing rule that a check added after step 6
needs its own revert): removing the guard fails `Indexer_IsSkipped_NotReportedAsAnUnmappedType`, so it is
fix-dependent rather than a restatement.

`verify-conventions` also required two things this change owed the repo, both now in `CLAUDE.md`: a
`Recent Updates` entry (12 files changed, and it carries the consumer-breaking migration warning the
`Out of scope` section asked for), and a **register-on-introduce** § Conventions rule, since "a mapper that
cannot express something refuses rather than dropping it quietly" is a new cross-cutting pattern.

### Flagged, not fixed

- **Existing consumer tables will not have the newly-mapped columns.** A consumer whose model already has
  a `long` has a live table without that column; their DDL and their schema now diverge, and a read will
  fail rather than silently return zero. This was already `Out of scope` and needs the `Recent Updates`
  callout named there — it is the one part of this change that can break a running deployment.
- **`char?`, `TimeSpan` and `DateTimeOffset` have no mapping** and now throw. They fail loudly instead of
  silently, which is an improvement but not a resolution → **[[TASK-150]]**, which also carries a lead the
  spec handed over: `schema-index-and-ddl` records that `CharField.Read` assigns a `string` to a `char`
  property, so plain `char` may already be broken on read — mapping `char?` onto the same class without
  checking would ship a second broken mapping.

## Progress log

- step 2 — picked; ranked above TASK-117 (`RedisCache.ClearAsync` → `FLUSHDB`) because silent write-side
  data loss outranks an unbounded destructive write on the severity ladder, and this one is reachable by
  merely declaring a `long` property rather than by calling a destructive API. TASK-111's injection angle
  rates higher in kind but its reachability is conditional on a consumer exposing rule authoring.
- step 3 — verified: **held**, mechanism confirmed verbatim at `AbstractField.cs:235`. **Rescoped**: the
  Approach's "per-provider type mapping is the bulk of the work" is false — all four `ConvertType`s already
  map these `DbType`s, `AddParameter` is untyped, and `ModelMap<T>` does no dispatch. Work is confined to
  `CreateAbstractField` + new field classes. Pulled in one same-root-cause defect (SQLite `Single` →
  `INTEGER`); acceptance criteria amended for it before any code was written.
- step 4 — layer: local (`Birko.Data.SQL` owns `CreateAbstractField`; the SQLite `Single` arm is in
  `Birko.Data.SQL.SqLite`, a sibling in the same framework, not an upstream package)
- step 5 — fix in `Birko.Data.SQL/SQL/Fields/{Long,Short,Double,Float,Binary}Field.cs` (new),
  `AbstractField.cs` (dispatch + fail-fast), `Birko.Data.SQL.projitems`,
  `Birko.Data.SQL.SqLite/.../SqLiteConnector.cs` (`Single` → `REAL`). Tests in
  `Birko.Data.SQL.Tests/DataBase/PrimitiveTypeMappingTests.cs` (24),
  `Birko.Data.SQL.SqLite.Tests/PrimitiveTypeRoundTripTests.cs` (8) +
  `SqLitePrimitiveColumnTypeTests.cs` (5), and one `*PrimitiveColumnTypeTests.cs` per remaining provider
  (5 each). Suites: SQL 386/386, SqLite 126/126, MSSql 26/26, MySQL 19/19, PostgreSQL 23/23.
  Fail-fast blast radius measured across **19** SQL-touching suites (incl. all six `Birko.Models.*.SQL`,
  BackgroundJobs.SQL, Workflow.SQL, Sync.Sql, Migrations.SQL, all View suites) — **all green**, so no
  framework model carries an unmappable property.
- step 6 — reverted fix, **ran twice**. **First run exposed a defect in the tests, not the fix**: all 15
  provider DDL tests passed the revert, because they built `new LongField(...)` by hand and the field
  classes survive a dispatch-only revert — they asserted the provider `ConvertType` contract, never this
  fix. Rewritten to go through `DataBase.LoadTable`. Second run: **43 of 52 new tests fail** on the revert
  (SQL 16/24, SqLite 12/13, MSSql 5/5, MySQL 5/5, PostgreSQL 5/5; provider suites went 0 → 15).
  Fix-dependent = every `PreviouslyUnmappedType_NowMapsToItsOwnFieldAndDbType` case,
  `EveryPreviouslyDroppedProperty_IsNowAColumnOnTheTable`, `LongIsNotRoutedThroughInteger`,
  `DoubleIsNotRoutedThroughDecimal`, all three `PortableFieldTypeHasAnAttributeDrivenCounterpart` cases,
  `UnsupportedType_ThrowsAtTableLoad_NamingThePropertyAndItsType`, all 7 failing round-trip tests, and all
  20 `*PrimitiveColumnTypeTests` cases across the four providers.
  **Contract pins — not evidence — 9:** the 6 `AlreadyMappedType_IsUnchanged` cases,
  `EnumStillMapsToInteger`, `DeliberateOptOut_StillExcludesTheColumnWithoutThrowing` (all three pin that
  the dispatch rewrite and the new throw did not regress what already worked, which is exactly why they
  must pass both before and after), and `NullablesLeftUnset_ReadBackAsNull_NotAsZero` — a nullable left
  unset reads back null whether the column exists or not, so it cannot witness the defect.
  **Re-run a third time** for the indexer guard the merge gate added: removing it fails
  `Indexer_IsSkipped_NotReportedAsAnUnmappedType`, so that check is fix-dependent too (52 → 53 new tests,
  43 → 44 fix-dependent).
- step 7 — respecced `schema-index-and-ddl` (stamped at `d9637e6`). Requirements changed: *CLR-type to SQL
  field mapping* (five new type arms; `return null` → `FieldAttributeException`), *Portable field type
  vocabulary* (the `Long`/`Double`/`Binary` scenario asserted the gap and now asserts the agreement),
  *Reader materialisation per field type* (`GetInt64`/`GetInt16`/`GetDouble`/`GetFloat`/
  `GetFieldValue<byte[]>`). New requirement: *An unmappable property fails table load rather than
  vanishing*, with the indexer and opt-out scenarios. Three scenarios that documented the defect as shipped
  behaviour were rewritten — *long, double and byte[] properties are dropped without warning*, *Nullable
  char is dropped*, and the `long` half of *An index on an unmapped property silently names a non-existent
  column* (unreachable now: an unsupported type fails load outright, so only the `[IgnoreField]` path
  reaches it). Nothing in the diff was unintended.
- step 8 — merge gate: `verify-conventions` clean on checks 1–8/10 (no nullable warnings in the new files;
  no store/`*Core`, settings, path or new-project findings), and required check #9 (`Recent Updates`) plus
  step-0b (register-on-introduce) — both added to `CLAUDE.md`. Review found the indexer gap; fixed,
  specced, and step 6 re-run for it. Commits: `d9637e6` + `ef71921` (production), `7978b28` / `e6cb7e2` /
  `71dc9c4` / `2aad9c9` / `8068a8a` (tests, five repos). Closed **done**.
