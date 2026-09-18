---
id: TASK-257
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-242, TASK-243, TASK-256, TASK-264, TASK-265, TASK-266, TASK-267, TASK-268, TASK-269]
findings: []
pr: "Birko.Data.SQL c218c72 · Birko.Data.SQL.MSSql 2029ff5 · Birko.Data.SQL.MySQL ba0abcd · tests: Birko.Data.SQL.Tests ab3ef0c · Birko.Data.SQL.MSSql.Tests 8b34264 · Birko.Data.SQL.MySQL.Tests 4bedc29"
github-issue: null
jira-key: null
---

# On MSSql an unlengthed `string` column becomes `TEXT`, so **no predicate on it works**

## Context — found by consumer Symbio (TASK-472) running a real entity against MSSql 2022

A `DeleteWhereAsync(x => x.Label == "a")` against MSSql 16.00.4265 failed, and not on anything to do with the
boundary it was verifying:

```
Microsoft.Data.SqlClient.SqlException :
  The data types text and nvarchar are incompatible in the equal to operator.
   at AbstractAsyncConnector.ReadOnAsync (AbstractAsyncConnector.cs:376)
```

`MSSqlConnector.ConvertType` (`Database/Connector/MSSqlConnector.cs:187-199`) maps `DbType.String` and friends
to `NVARCHAR(n)` **only when the field is a `CharField` with a declared length**, and to **`TEXT`** otherwise:

```csharp
case DbType.String:
…
default:
    if (field is CharField charField) return string.Format("NVARCHAR({0})", charField.Lenght);
    else                              return "TEXT";
```

`TEXT` is deprecated in SQL Server and **cannot be used with `=`, `<>`, `GROUP BY`, `ORDER BY` or `DISTINCT`**.
Parameters bind as `nvarchar`, so every comparison is a type clash.

## Why it matters

A plain `public string Name { get; set; }` with no length attribute is the common shape in consumer entities.
On MSSql that makes every `FindAsync`/`FindAllAsync`/`CountAsync`/`DeleteWhereAsync` predicate touching a
string column throw, along with any `SortBy` over one.

⚠ Consumer Symbio's `CLAUDE.md` § *Pravidla pre repository predikaty* lists `==`, `!=`, `Contains`,
`StartsWith`, `ToLower()` on a string column in its **SAFE** column. That is true on SQLite and PostgreSQL and
**false on MSSql for any unlengthed string** — a rule verified on one backend and assumed on the others, which
is the failure mode that section itself warns about.

⚠ Never exercised: consumers test on SQLite, and the MSSql live suites added by TASK-242/243 assert on bulk
writes and lazy init rather than on string predicates.

## What to decide

1. **Map unlengthed strings to `NVARCHAR(MAX)`.** Supports comparison, fixes the whole class. Changes the
   declared type of existing columns — but a `TEXT` column was already unusable in a predicate, so no existing
   deployment can have depended on that behaviour.
2. **Require a length** — make an unlengthed `string` a schema-time error instead of a silent `TEXT`. Honest,
   and it would have caught this at first use; breaking for every consumer.
3. **Keep `TEXT` and cast in predicates** (`CAST(col AS NVARCHAR(MAX)) = @p`). No schema change, but makes
   every string predicate non-sargable — trades a hard failure for a silent performance cliff, the worse
   outcome.

Option 1 is the obvious candidate; the decision to record is `NVARCHAR(MAX)` versus a default length, and what
happens to existing `TEXT` columns.

## Acceptance criteria

- [x] A decision recorded with its reason, stating what an unlengthed `string` means on MSSql.
      → `CLAUDE.md` § Conventions (*"A column type is only correct for the operations the provider allows ON
      it"*) + the dated Recent Updates entry, and in code at `MSSqlConnector.ConvertType`. The statement:
      **an unlengthed `string` — meaning a `StringField`, i.e. no `[MaxLengthField]`/`[PrecisionField]`/
      `[MaxLength]`/`[StringLength]`, per `AbstractField.cs:343` — declares `NVARCHAR(MAX)`; where an index
      key names the column it declares `NVARCHAR(255)`.**
- [x] Against a real MSSql: `==`, `Contains`/`StartsWith`/`EndsWith`, `ToLower()` on a string column, and a
      `SortBy` over one, all execute and return the **right rows** — asserted on values, not on the absence of
      an exception.
      → `StringPredicateLiveTests` (9), all asserting returned rows or asserted sequences; the delete case
      counts survivors on a **separate connection**. `!=` and `IN` (`labels.Contains(x.Label)`) were added
      because they fail identically and were not on this list — `IN` is the canonical batch-fetch shape.
      ⚠ **The LIKE three passed *before* the fix** — `LIKE` is legal on `text` (measured) — so they are
      contract pins; the provers are `==`, `IN`, `ToLower()` and the two sorts. See § Step 0.
- [x] An MSSql live fixture whose probe entity has an **unlengthed** string, so the suite covers the shape
      consumers actually write.
      → `MsStrRow` (no length attribute, no `ModelMap`) and the three probes in
      `IndexOverUnlengthedStringLiveTests`. Also **de-rigged the two pre-existing fixtures**, one of which was
      rigged by omission: `BulkTransactionBoundaryLiveTests`'s `HasPrecision(100)` was a no-op, so that column
      had always been `TEXT` while looking bounded.
- [x] Stated explicitly what happens to an MSSql database that already holds `TEXT` columns.
      → **Nothing; the framework will not repair it.** `CreateTable` is guarded by `IF NOT EXISTS` and
      schema-ensure never reconciles an existing table's columns, so such a database keeps its `TEXT` columns
      and keeps failing every predicate after the fix. Remedy is a hand-run
      `ALTER TABLE [T] ALTER COLUMN [C] NVARCHAR(MAX) NULL` (supported, preserves data, no index to drop —
      `TEXT` could never have one). Auto-`ALTER` on schema-ensure was considered and rejected: store init
      rewriting existing production columns is a quiet destructive write. Blast radius measured as **zero**
      (no deployment selects MSSql). Recorded residual: nothing reports a stale column *type* — Symbio's
      `SchemaDriftCheck` compares presence only, so an operator gets no signal (split signal 4).
- [x] Proven able to fail. → **four reverts, run one at a time against the live server:**
      - **(a)** restore `return "TEXT"` → **25 of 85 fail**. The LIKE test is **not** among them, which is what
        confirms those assertions are pins rather than provers.
      - **(b)** always `NVARCHAR(MAX)`, bounded branch unreachable → **11 of 85 fail**, and **all 9 predicate
        tests stay green** — the two halves proven independent, not argued.
      - **(c)** drop each `IsIndexed` marking site in turn (`DataBase_Table.cs:198` / `:264`) → **4** and **2**,
        in **disjoint sets**. TASK-248's equivalent revert failed **0**, which is why this suite declares one
        entity per attribute form.
      - **(d)** narrow `IsInIndexKey` back to `IsIndexed` → **4 fail**, exactly the UNIQUE/PRIMARY cases,
        isolating the widening from the bound.

## Verification

Live **SQL Server 2022 (16.0.4265.3)** — the build the consumer reported — plus live **PostgreSQL 16**, live
**MySQL 8.4** and on-disk SQLite, with `BIRKO_REQUIRE_LIVE` set throughout so no gated suite silently skipped.
**1,117 passed, 0 failed** across seven suites; **40 new**. No new nullable warning in any touched project;
the 64 standing ones in `Birko.Data.SQL.Tests` are in that suite's own test files and are identical with the
change stashed.

| Suite | Result |
|---|---|
| `Birko.Data.SQL.MSSql.Tests` | 87 (was 61) |
| `Birko.Data.SQL.Tests` | 575 (was 565) |
| `Birko.Data.SQL.SqLite.Tests` | 229 |
| `Birko.Data.SQL.MySQL.Tests` | 78 (was 74) |
| `Birko.Data.SQL.PostgreSQL.Tests` | 82 |
| `Birko.Data.SQL.MSSql.View.Tests` | 19 |
| `Birko.Data.Migrations.SQL.Tests` | 47 |

> ⚠ An earlier step-7 run reported all four providers green **without** `BIRKO_REQUIRE_LIVE`, which silently
> skipped every MySQL and PostgreSQL live test. Re-run with the flag it was 46 and 48 failures — pure
> skip-as-failure, no real defect, but it is exactly the trap this epic keeps rediscovering, so the servers
> were stood up rather than the caveat accepted.

Container commands used:

```
docker run -d --name birko-mssql-257 -p 11433:1433 -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD='Birko!Passw0rd' mcr.microsoft.com/mssql/server:2022-latest
docker run -d --name birko-pg-257    -p 15432:5432 -e POSTGRES_PASSWORD='Birko!Passw0rd' \
  -e POSTGRES_DB=birkoview postgres:16
docker run -d --name birko-mysql-257 -p 13306:3306 -e MYSQL_ROOT_PASSWORD='Birko!Passw0rd' \
  -e MYSQL_DATABASE=birkoview mysql:8.4
```


## Recorded side effect — migrations on MSSql, found at the close gate

`Birko.Data.Migrations.SQL`'s `SchemaField` passes `descriptor.IsUnique` and `descriptor.IsPrimary` to the
`AbstractField` base, so `IsInIndexKey` is **true** for a migration's unique/primary string column. That means
this change also fixes a case nobody filed: a migration declaring a `.Unique()` string column previously got
`TEXT` on MSSql, and an inline `UNIQUE TEXT` fails the whole `CREATE TABLE` with Msg 1919 — so such a migration
could never run there. It now declares `NVARCHAR(255)` and works.

**Deliberately untested here, and that is a scope call rather than an oversight.** Neither test project imports
both `Birko.Data.SQL.MSSql` and `Birko.Data.Migrations.SQL`, so covering the composition needs a `.csproj`
change beyond this task. Both halves *are* covered — `IsUnique ⇒ NVARCHAR(255)` in
`IndexedStringColumnTypeTests`, and `SchemaField` setting `IsUnique` from the descriptor is plain constructor
delegation. Folded into split signal 1's scope, which already owns `SchemaField`'s dropped metadata.

The same applies to `AlterTableAdd`: adding a unique/primary string column to an existing table now emits
`NVARCHAR(255)` where it emitted `TEXT` before, so that path is fixed on MSSql too. Reachable sinks were
enumerated to be sure the widening cannot leak: `ConvertType` is called **only** from each provider's own
`FieldDefinition`, and `FieldDefinition` only from `CreateTable`, `AlterTableAdd` and migrations'
`SqlSchemaBuilder` — all DDL, all per-provider. `IsInIndexKey` is read by `MSSqlConnector` alone.

Two migration cases remain broken on MSSql and are **not** improved by this change:
- `SchemaField` never sets `IsIndexed` (nothing resolves migration index declarations back to fields), so a
  separately-declared `SqlIndexBuilder` index over a plain string still hits a `NVARCHAR(MAX)` column and still
  fails with 1919 — the same failure as before, by the same mechanism it had under `TEXT`.
- `SchemaField` drops `FieldDescriptor.MaxLength` entirely, so a migration's `maxLength: 50` still produces an
  unbounded column on all four providers.

## Out of scope

- The transaction boundary — TASK-242/243. This is column typing; it merely blocked two of that verification's
  member/provider pairs.
- PostgreSQL's UTC `DateTime` COPY defect — TASK-256, the sibling found in the same consumer run.
- **MySQL's identical `IsUnique`/`IsPrimary` hole** — [[TASK-265]] owns it. `MySQLConnector` deliberately still
  reads the narrow `IsIndexed`, so `[UniqueField]` on an unlengthed string still emits `LONGTEXT UNIQUE`
  (ERROR 1170). Pinned by `IndexKeyPredicateScopeTests` so the gap is a recorded decision rather than an
  oversight, and that test is what should fail when TASK-265 lands.
- **A binary column has the same index-key defect, in the same method** — [[TASK-266]] owns it. Measured at
  this task's close gate: `VARBINARY(MAX)` with inline `UNIQUE` or under `CREATE INDEX` raises the same
  Msg 1919, so `[UniqueField] public byte[] Hash` kills the whole `CREATE TABLE` on MSSql today. Deliberately
  not folded in — every criterion here says *string*, and MySQL's `LONGBLOB` half needs its own live
  measurement.
- **A composite of four or more bounded strings overflows the key limit** — [[TASK-266]] owns it. 4 × 510 B =
  2040 B > 1700 B: SQL Server creates the index (warning 1708) and then fails the **INSERT** with Msg 1946.
  Recorded on `IndexedStringColumnLength`'s remarks; capping a per-index budget is a different design question
  from typing one column, and `ConvertType` sees one field at a time so it cannot compute it.
- **Migrations drop declared column metadata** — [[TASK-264]] owns it. `SchemaField` never reads
  `FieldDescriptor.MaxLength` and never sets `IsIndexed`, so a migration's `maxLength` is ignored on all four
  providers and a migration-declared index over a string still fails 1919 on MSSql. It also owns the untested
  MSSql unique-column composition described above.
- **`TimeOnly` and `CharField.Lenght`** — [[TASK-268]] owns both. `TimeOnlyField` declares the widest string
  type on every provider despite being fixed-width 8; `CharField.Lenght` is `int?` so `NVARCHAR()` is a
  reachable syntax error from public surface (not from any model).
- **Nothing reports a stale declared column type** — [[TASK-269]] owns it. This is what makes the "existing
  `TEXT` columns are not repaired" decision above silent for an operator: the framework has no drift surface,
  and Symbio's `SchemaDriftCheck` compares presence only.
- **The project-local `verify-conventions` did not run at this task's own close gate** — [[TASK-267]] owns it.
  The Birko-specific checks were executed by hand instead; check 5 found a real miss, which is the evidence
  the gap costs something.

## Implementation plan

> Planned 2026-08-20, grilled 2026-08-21. Every premise below marked *measured* was executed against a live
> **SQL Server 2022 (RTM-CU26) 16.0.4265.3** container — the same build the consumer reported. Step 0 is
> therefore already done and its results are recorded here rather than left as an intention.

### Step 0 — MEASURED, not cited

The plan originally carried four unexecuted premises. All are now settled, and two of them were **wrong**.

A `TEXT` column, seeded with three rows, parameter bound as `nvarchar`:

| Operation | Result |
|---|---|
| `= @p` · `<> @p` · `IN (@p)` | **Msg 402** — *"The data types text and nvarchar are incompatible…"* — the consumer's exact error |
| `LIKE @p` | **legal** |
| `IS NULL` | **legal** |
| `LOWER(Col)` | **Msg 8116** — *"Argument data type text is invalid for argument 1 of lower function"* |
| `ORDER BY Col` | **Msg 306** |
| `GROUP BY Col` | **Msg 306** |
| `SELECT DISTINCT Col` | **Msg 421** |
| `CREATE INDEX` over it | **Msg 1919** |
| inline `UNIQUE TEXT` in `CREATE TABLE` | **Msg 1919** — the whole table creation dies, not just an index |

**Corrections to the drafted plan:** equality/`IN` is **402**, not 8116 (8116 is the `LOWER` case alone);
`GROUP BY` shares **306** with `ORDER BY`; and `DISTINCT` is a fourth code, **421**, which the draft did not
predict at all.

The same probes against **`NVARCHAR(MAX)`**: `=`, `IN`, `LOWER`, `ORDER BY`, `GROUP BY`, `DISTINCT` **all
succeed** — so it does fix the whole predicate class. But `CREATE INDEX`, inline `UNIQUE` and inline
`PRIMARY KEY` over `NVARCHAR(MAX)` are **1919, identical to `TEXT`**. Against **`NVARCHAR(255)`**: index and
inline `UNIQUE` both succeed.

**That measurement is what makes the two halves of this fix provably independent and both necessary** — it is
revert (b)'s claim, established before the code was written rather than asserted after.

### Resolved acceptance-criteria questions

**Question 1 — the LIKE family was already green, so it is a pin and not a prover.** `LIKE` and `IS NULL` are
legal on `text` (measured above). Of the five things criterion 2 lists, the three LIKE ones therefore pass
**before** the fix: they are contract pins that must not regress. The revert-provers are `==`, `IN`,
`ToLower()` and `SortBy`. No criterion text changes; this note records which half carries the proof.

**Question 2 — the indexed/unique/primary case is IN SCOPE, and it needed a shared predicate.** Measured:
`NVARCHAR(MAX)` is refused as an index key exactly as `TEXT` is, so mapping unlengthed strings to
`NVARCHAR(MAX)` alone would have left every `[IndexedField]`/`[CompositeIndex]`/`[UniqueField]` unlengthed
string as broken as before. Folded in — same one-producer method, same declaration, § Conventions'
*guard the whole verb family or none of it*.

**Resolved: the predicate is a new computed property, not an inline OR.** `AbstractField.IsIndexed` (TASK-248)
answers only *"does a declared index name this column"* — `LoadIndexes` never marks unique/primary columns,
while `FieldDefinition` emits `UNIQUE`/`PRIMARY KEY` **inline on all four providers** (MySQL `:289/:293`,
PostgreSQL `:289/:293`, MSSql `:212/:216`, SQLite `:178/:182`). Rather than OR three flags at one connector —
a second spelling of a question the framework already has a name for — add:

```csharp
// AbstractField.cs, beside IsIndexed
/// <summary>Any declared index, UNIQUE or PRIMARY KEY names this column, so it is an index KEY: a
/// provider whose key types are restricted must bound it. Wider than IsIndexed, which covers only
/// [IndexedField]/[CompositeIndex] — FieldDefinition emits UNIQUE/PRIMARY KEY inline on all four
/// providers and LoadIndexes never marks those columns.</summary>
public bool IsInIndexKey => IsIndexed || IsUnique || IsPrimary;
```

MSSql reads `IsInIndexKey`. **MySQL deliberately keeps reading the narrower `IsIndexed`** until it is measured
on a live 8.4 — it has the identical hole (`LONGTEXT UNIQUE` → ERROR 1170 at `CREATE TABLE`, confirmed by
reading `MySQLConnector.cs:269`), and closing it is split signal 2, now a one-word change rather than a
re-derivation of the same OR. PostgreSQL and SQLite need neither, for real reasons: PostgreSQL indexes `TEXT`
via btree, and SQLite has type affinity — it returns `TEXT` unconditionally and **ignores `CharField.Lenght`
altogether** (`SqLiteConnector.cs:150`).

### Findings

**1. Where a `string` becomes a field, and what "unlengthed" means concretely.**
`Birko.Data.SQL/SQL/Fields/AbstractField.cs:335-356` is the sole `string` arm: `length = maxLength ?? precision`,
and **`CharField` only when `length != null && length > 0`** (`:343`), otherwise `StringField` (`:347`). So
**"unlengthed" = no `[MaxLengthField]`, no `[PrecisionField]`, no `[MaxLength]`, no `[StringLength]`** → a
`StringField`, `DbType.String`, MSSql's `else` branch. Attribute reads at `:142-145`, `:146-149`, `:183-196`;
`CreateAbstractField` is the only producer of fields (`:209-230` says so).

- **`CharField.Lenght == null` is not reachable from a model.** `CharField.cs:11` declares `int? Lenght = 1`, but
  the only two constructions are `AbstractField.cs:332` (literal `1`, CLR `char`) and `:343` (guarded
  `length > 0`). An `NVARCHAR()` syntax error needs a consumer hand-constructing `new CharField(…, lenght: null)`
  — public surface, never exercised. Recorded, not fixed here (split signal 5).
- **A second, unfiled sink into the same `else` branch: `TimeOnlyField`** (`SQL/Fields/TimeOnlyField.cs:46-54`)
  is an `AbstractField` with `DbType.String`, **not** a `CharField`. Its own doc (`:27-33`) argues fixed-width
  `HH:mm:ss` text "compares correctly with `<`, `>` and `BETWEEN`" — **false on MSSql today**, because the
  column is `TEXT`. Every `TimeOnly` column on MSSql has the identical defect; this fix repairs it.
- **`Birko.Models.SQL`'s fluent `HasMaxLength`/`HasPrecision` is mapping metadata only and is deliberately not
  applied to the SQL field** (`Mapping/ModelMapRegistry.cs:80-92,119`). This matters for the fixtures:
  `BulkTransactionBoundaryLiveTests.cs:103`'s `map.Property(x => x.Name).HasPrecision(100)` is a **no-op**, so
  `BulkRow.Name` (`:94`) is already an unlengthed `TEXT` column on the live MSSql suite — a fixture that looks
  bounded and is not.
- TASK-248's precedent to mirror: `AbstractField.cs:19-31` (`IsIndexed`, reasoning in its `<remarks>`), consumed
  by `MySQLConnector.cs:246-275` alone, bound named at `MySQLConnector.cs:65-79`
  (`protected virtual int IndexedStringColumnLength => 255`).

**2. The index interaction — MSSql needs the `IsIndexed` branch, and two more flags besides.**
`MSSqlConnector.cs:187-199` is the mapping; `FieldDefinition` (`:203-229`) is its sole DDL caller and also emits
inline **`PRIMARY KEY`** (`:210-213`) and **`UNIQUE`** (`:214-217`).

- **Today an indexed unlengthed string fails, silently.** `TEXT` cannot be an index key (error 1919), so
  `CreateTable` → `AbstractConnector_Create.cs:56-69` records it on `IndexCreationFailures` /
  `OnIndexCreationFailed` per TASK-204 and continues; nothing in the tree subscribes. **`NVARCHAR(MAX)` fails
  identically** (`MAX` is not permitted in a key at all), so the `NVARCHAR(MAX)` change alone does not move this
  case.
- **Worse than MySQL's version: the `UNIQUE`/`PRIMARY KEY` case kills the table, not just the index.**
  `FieldDefinition:214` emits `UNIQUE` inline — a unique index — so `[UniqueField] public string Code` with no
  length makes the whole `CREATE TABLE` fail, today and equally under `NVARCHAR(MAX)`. `IsIndexed` does **not**
  cover it: `LoadIndexes` marks only `[IndexedField]` / `[CompositeIndex]` columns. So MSSql's bounded branch
  must read `field.IsIndexed || field.IsUnique || field.IsPrimary`.
- **Both `IsIndexed` marking sites are real and must both be covered by tests.**
  `SQL/DataBase_Table.cs:196-199` (per-property `[IndexedField]`) and `:262-264` (class-level
  `[CompositeIndex]`). Ordering is safe: `ComputeTable` calls `LoadFields` then `LoadIndexes` (`:104`/`:112`,
  same at `:126`/`:132`) and caches the result (`:62-68`), so `IsIndexed` is set before any `FieldDefinition`
  runs. TASK-248's own lesson applies — its MySQL suite used only the class-level form, so reverting the
  per-property marking failed **0** tests. The MSSql suite must declare **one entity per attribute form**.
- **Two comments in the tree state a falsehood about MSSql** and must be corrected by this task:
  `MySQLConnector.cs:257` ("SQLite, PostgreSQL and MSSql index a TEXT column happily") and
  `AbstractField.cs:27-29` ("The other three providers index a TEXT column happily and ignore this flag").
  MSSql cannot. `IndexedStringColumnTypeTests.cs:20-24` already records it as a known out-of-scope divergence.
- **Blast radius of a 255-char bound, measured against a known instance** (TASK-248's rule): consumer Symbio
  declares the shape 7×, fully-qualified — `Symbio.Module.Eshop/Domain/Entities/Order.cs:12,15`
  (`[CompositeIndex("ux_eshoporder_docnum", …, IsUnique=true)]` over a plain `string OrderNumber`) and
  `Symbio.Module.Customers/.../CustomerAccount.cs:26-43` (`Email`). Document numbers and e-mail addresses; 255
  is not a ceiling any of them can reach. **Zero framework models** declare `[IndexedField]`/`[CompositeIndex]`
  at all.

**3. Does `TEXT` reach any other MSSql sink? No.** A grep for `TEXT|NVARCHAR|VARCHAR` over
`Birko.Data.SQL.MSSql` + `.MSSql.View` returns exactly `MSSqlConnector.cs:194` and `:198`. `ConvertType` has
four implementations framework-wide, one per provider (`AbstractConnectorBase.cs:201` abstract), and
**`MSSqlConnector` has no subclass** (unlike `TimescaleDBConnector : PostgreSQLConnector`), so there is no
inherited copy to miss. `MSSqlIndexManager` and `MSSqlConnector_View` emit no column types.

- **Migrations reach the same producer, so they are fixed for free** —
  `Birko.Data.Migrations.SQL/Context/SchemaField.cs:10-13` is an `AbstractField` (not a `CharField`) carrying
  `MapFieldType` (`:20-35`), and `SqlSchemaBuilder.AddField` (`:102-106`) / `SqlCollectionBuilder.Build` route
  through the connector's `FieldDefinition`. **But `SchemaField` drops `FieldDescriptor.MaxLength` entirely**, so
  a migration's `WithField("Code", FieldType.String, maxLength: 50)` produces an unbounded column on **all four**
  providers, and a migration-created string column can never take the bounded branch (`IsIndexed` is never set on
  a `SchemaField` either). Adjacent, not part of this task → split signal 1.

**4. The existing MSSql live fixture.** `Birko.Data.SQL.MSSql.Tests/DeclaredIndexLiveTests.cs` is the template:
gate `BIRKO_MSSQL_HOST` (+ `_PORT`/`_USER`/`_PASSWORD`/`_DB`, defaults `sa` / `Birko!Passw0rd` / `birkoview`) at
`:40-45`; `RequireServer()` at `:51-65` writes a `SKIPPED:` line and **throws** when `BIRKO_REQUIRE_LIVE` is set;
`Settings()` at `:67-70` with `TrustServerCertificate = true`; probe entity at `:79-91` deriving
`AbstractDatabaseLogModel` — **not** `AbstractLogModel`, because `default(DateTime)` is 1753-out-of-range on SQL
Server (`:72-78`); `Exec()` + `DROP TABLE IF EXISTS` in `Dispose()` (`:93-106`).

- **Both existing live probes are length-rigged, one by accident**: `MsIdxRow` declares `[MaxLengthField(64)]`/
  `[MaxLengthField(32)]` (`:86-90`), and `BulkRow.Name` *looks* bounded via the no-op `HasPrecision(100)`.
  Neither suite puts a string in a predicate. Exactly TASK-256's *two fixtures were rigged, one by omission*.
- Offline DDL precedent: `MSSqlPrimitiveColumnTypeTests.cs:38-44` goes through `DataBase.LoadTable`, with the
  reason at `:18-21` — **"a hand-built field survives a dispatch-only revert, so such a test cannot witness this
  fix."** The suite to rewrite is `IndexedStringColumnTypeTests.cs:35-48`, which currently asserts `TEXT` for
  indexed **and** unindexed and builds its field by hand.

**5. `ToLower()` / `Contains` / `StartsWith` / `EndsWith` translation — provider-independent, nothing to change.**
`SQL/DataBase.cs:597-639` (`ParseConditionExpression`): `StartsWith`→`StartsWith`, `EndsWith`→`EndsWith`,
`Contains`→ **`Like` when declared on `String`, `In` otherwise** (`:605-609`), `ToLower`/`ToLowerInvariant`
rewrites the *column name* to `LOWER(col)` (`:610-624`), `ToUpper` likewise; a second `LOWER(` producer for
join/view expressions at `:226-235`; only the first argument of a string pattern method is the operand
(`:640-658`). `Strategies/LikeConditionStrategy.cs:25` → `col LIKE @p`;
`Strategies/EqualConditionStrategy.cs:23` → `col = @p` / `col <> @p`; `InConditionStrategy` → `col IN (@p…)`;
wildcards added to the **value** at `SqlBuilderContext.cs:42-55`; sort keys resolve at `DataBase_OrderBy.cs:44`
and interpolate bare.

- The clash is entirely at the binding end: `MSSqlConnector.cs:247-261` uses `AddWithValue`, so a `string` binds
  as `nvarchar` → *"The data types text and nvarchar are incompatible in the equal to operator"*. So
  `LOWER(text_col)`, `text_col = @p`, `text_col IN (…)` and `ORDER BY text_col` are all refused by the server,
  while `text_col LIKE @p` is legal (see question 1). **`ids.Contains(x.Code)` → `IN` is also broken today** —
  the canonical N+1 batch shape, and not on the criteria's list.

### Decision to record

> **On MSSql an unlengthed `string` (and any `DbType.String` field that is not a `CharField`, which includes
> `TimeOnly`) declares `NVARCHAR(MAX)`. Where the schema puts that column in an index key —
> `[IndexedField]`, `[CompositeIndex]`, `[UniqueField]` or `[PrimaryField]` — it declares `NVARCHAR(255)`
> instead, because SQL Server permits no `MAX` type in an index key. The bound is the provider's own limit, not
> a framework choice. Nothing alters an existing column: a database created before this fix keeps its `TEXT`
> columns and keeps failing every predicate until an operator runs
> `ALTER TABLE … ALTER COLUMN … NVARCHAR(MAX)`.**

**`NVARCHAR(MAX)` over a default length**, argued from the tree:

- **A default length converts a working write into a failure; `MAX` cannot.** `TEXT` holds 2 GB and accepts
  writes today — reads and predicates are what fail. `NVARCHAR(4000)` would start refusing a value the same code
  stored yesterday. TASK-248's rule verbatim ("bounding the column on all four would have imposed a
  255-character ceiling on columns that have none… breaking three working providers to fix one"), applied
  within one provider.
- **`MAX` keeps MSSql in step with the other three.** SQLite `TEXT`, PostgreSQL `TEXT`, MySQL `LONGTEXT` are all
  unbounded; a bounded MSSql default would make it the only provider with a silent write ceiling. *Uniformity
  beat per-provider fidelity* is the recorded outcome of TASK-256 and TASK-263, for the same reason: consumers
  test on SQLite.
- **The precedent is in the same `switch`.** `DbType.Binary` → `VARBINARY(MAX)` (CR-M137,
  `MSSqlConnector.cs:180-184`) was chosen for exactly this reason — a bare `BINARY` defaults to `BINARY(1)`.
- **`NVARCHAR(MAX)` is a first-class comparable type**: `=`, `<>`, `IN`, `LIKE`, `LOWER()`, `ORDER BY`,
  `GROUP BY`, `DISTINCT` all work — the whole class the task names. It is also not deprecated, which `TEXT` is.
- **Option 3 (cast in predicates) rejected on the ledger's own terms**: non-sargable, and it would have to be
  taught to five condition strategies, the `LOWER`/`UPPER` rewrites, `ORDER BY` and `GROUP BY` — enumerating
  producers, which this codebase has been bitten by four times in the identifier family. **Option 2 (refuse the
  declaration) rejected**: TASK-248 already measured that exact refusal and inverted it — 7 live consumer
  entities declare the shape and work on PostgreSQL today.

**Indexed columns: `NVARCHAR(255)` via a `protected virtual int IndexedStringColumnLength => 255`**, mirroring
`MySQLConnector.cs:65-79` name-for-name.

- 255 rather than 450 (= 900 bytes, the legacy key ceiling) **because MySQL already picked 255**: a value that
  indexes on one server must index on the other, and the same model runs on both. Per-provider headroom would
  buy nothing and cost a divergence.
- Covers `IsIndexed || IsUnique || IsPrimary` — the inline `UNIQUE`/`PRIMARY KEY` in `FieldDefinition:210-217`
  are index keys too, and today they make the `CREATE TABLE` itself fail.
- Prefix indexes are not an option on SQL Server, so TASK-248's *prefer the loud narrow failure to the quiet
  weak one* needs no re-argument: an over-255 write is refused loudly.

**What happens to an existing MSSql database holding `TEXT` columns: nothing, and the framework will not repair
it.**

- `MSSqlConnector.CreateTable` (`:263-281`) is guarded by
  `IF NOT EXISTS (SELECT * FROM sys.tables WHERE name=…)`; schema-ensure **never reconciles the columns of an
  existing table** (`AbstractConnector_Create.cs:74-98` is create-only; the only `ALTER` path is
  `AlterTableAdd`, which adds columns and is called by migrations, not store init). So an old table keeps
  `TEXT`, no error is raised on either side, and **every string predicate on it keeps throwing after the fix**.
- The honest remedy is by hand: `ALTER TABLE [T] ALTER COLUMN [C] NVARCHAR(MAX) NULL;` per affected column (no
  index to drop first — a `TEXT` column could never have one), or drop and recreate. `text → nvarchar(max)` is a
  supported `ALTER` and preserves data.
- **Blast radius measured, and it is empty.** Consumer Symbio's `appsettings.json:8-16` sets
  `DataProviders.Default = "SQLite"` with `MsSql` configured-but-unselected; that file is the only MSSql wiring
  in any consumer repo. And a `TEXT` column was unusable in a predicate, so no deployment can have depended on
  it. **No migration is owed** — and as in TASK-219/256 that window closes the moment MSSql is used in
  production, which is the argument for landing this now.
- One thing to write down rather than build: Symbio's `SchemaDriftCheck` compares column **presence** only, so
  it will **not** report a stale `TEXT` column. An operator has no automatic signal → split signal 4.

### Steps

**0 — ✅ DONE. Baseline measured; results and the two corrections are recorded in § Step 0 above.**
Container: `docker run -d --name birko-mssql-257 -p 11433:1433 -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Birko!Passw0rd' mcr.microsoft.com/mssql/server:2022-latest`,
database `birkoview`. Export `BIRKO_MSSQL_HOST=localhost`, `BIRKO_MSSQL_PORT=11433`, `BIRKO_REQUIRE_LIVE=1`
before any suite run, or the live half skips and reports green having exercised nothing.

**1 — production repo `Framework/Birko.Data.SQL`: the shared predicate.** Add
`AbstractField.IsInIndexKey => IsIndexed || IsUnique || IsPrimary` beside `IsIndexed`, doc-commented as in the
resolved-question block above. Also **correct the false claim** in `IsIndexed`'s own `<remarks>`
(`SQL/Fields/AbstractField.cs:27-29`, "The other three providers index a TEXT column happily and ignore this
flag") — MSSql cannot, and as of this task it reads `IsInIndexKey`.

**2 — production repo `Framework/Birko.Data.SQL.MSSql`: the type mapping.**
`Database/Connector/MSSqlConnector.cs` — add `protected virtual int IndexedStringColumnLength => 255`,
doc-commented like `MySQLConnector.cs:65-79`, stating that the **1700-byte nonclustered / 900-byte clustered
key limit is the real ceiling** (so 850 / 450 characters at 2 bytes each), that `MAX` is not permitted in a key
at all, and that 255 matches MySQL deliberately so one model indexes on both. Rewrite the `DbType.String` arm
(`:187-199`): `CharField` → `NVARCHAR({Lenght})` (unchanged); else `field.IsInIndexKey` →
`NVARCHAR({IndexedStringColumnLength})`; else `NVARCHAR(MAX)`. The comment carries the measured evidence — 402
on equality, 1919 on an index over *either* `TEXT` or `NVARCHAR(MAX)` — because that pair is what makes both
branches necessary. No other file in this repo changes.

**2b — separate repo `Framework/Birko.Data.SQL.MySQL`**: correct the same false claim at
`Database/Connectors/MySQLConnector.cs:257`. Comment-only; MySQL keeps reading `IsIndexed`, and its
`IsUnique`/`IsPrimary` gap stays split signal 2. **Do not switch MySQL to `IsInIndexKey` here** — that changes
DDL on a provider this task does not measure.

> **Note on the escape hatch's portability.** The draft called `[MaxLengthField(n)]` "portable, it applies on
> every provider". Measured false: `SqLiteConnector.cs:150` returns `TEXT` for every string and never reads
> `CharField.Lenght`. It is honoured on three of four providers and ignored on SQLite (harmlessly — type
> affinity), so say *three of four* rather than repeating a claim the code contradicts.

**3 — tests repo `Framework.Tests/Birko.Data.SQL.MSSql.Tests`: the offline DDL half.** **Rewrite**
`IndexedStringColumnTypeTests.cs` (its whole premise inverts): route every case through `DataBase.LoadTable` on
attribute-declared probe entities rather than hand-built fields (`MSSqlPrimitiveColumnTypeTests.cs:18-21`'s
reason), asserting plain `string` → `NVARCHAR(MAX)`; `[MaxLengthField(64)]` → `NVARCHAR(64)`; `[IndexedField]`
unlengthed → `NVARCHAR(255)`; `[CompositeIndex]` unlengthed → `NVARCHAR(255)`; `[UniqueField]` unlengthed →
`NVARCHAR(255)` **and** the emitted `FieldDefinition` contains `UNIQUE`; `[PrimaryField]` unlengthed →
`NVARCHAR(255)` + `PRIMARY KEY`; a `TimeOnly` property → `NVARCHAR(MAX)` (pins finding 1's second sink). **Two
separate entities** for the `[IndexedField]` / `[CompositeIndex]` forms, so either `DataBase_Table.cs` marking
site reverts red. Keep one hand-built-field case for `ConvertType` as public surface, clearly labelled.

**4 — tests repo: `StringPredicateLiveTests.cs` (new, live).** Copy `DeclaredIndexLiveTests`'
gate/`Settings()`/`Exec`/`Dispose` shape. Probe entity
`[Table("MsStrRows")] class MsStrRow : AbstractDatabaseLogModel { public string Label; public string? Note; public int Amount; }`
— **no length attribute, no `ModelMap`**. Seed a known set through `AsyncMSSqlStore<MsStrRow>`, then assert on
**returned rows**, not absence of an exception: `ReadAsync(x => x.Label == "beta")` → exactly the beta row;
`!=`; `labels.Contains(x.Label)` (→ `IN`); `Contains`/`StartsWith`/`EndsWith`; `x.Label.ToLower() == "beta"`
(→ `LOWER(col)`); `ReadAsync(null, OrderBy<MsStrRow>.By(x => x.Label))` and `.ByDescending(…)` → asserted
**sequence**; a `GROUP BY` over `Label` if the store exposes one; `DeleteAsync(x => x.Label == "a")` — the
consumer's original failing call — then count survivors on a fresh connection. Plus one nullable-`Note` case so
the NULL path is covered.

**5 — tests repo: `IndexOverUnlengthedStringLiveTests.cs` (new, live).** Two probe entities — one
per-property `[IndexedField("ix_…")]`, one `[CompositeIndex("ux_…", TenantGuid, Number, IsUnique = true)]` with
`Number` unlengthed (Symbio's exact shape). Assert via `sys.indexes`/`sys.index_columns` (reuse
`DeclaredIndexLiveTests.IndexColumns`/`IsUnique`) that both indexes exist, `IsUnique` where declared, and
`connector.IndexCreationFailures` is **empty** — per TASK-209, "did not throw" is worthless here because
schema-ensure records and continues. Plus: a 300-character write is **refused loudly** (the 255 bound's stated
cost), and a `[UniqueField]` unlengthed entity's `CREATE TABLE` now **succeeds** where it previously failed
outright.

**6 — tests repo: de-rig the two existing fixtures.** `BulkTransactionBoundaryLiveTests.cs:103` — delete the
no-op `HasPrecision(100)` and note in the mapping comment that fluent length is metadata only
(`ModelMapRegistry.cs:80-92`), so `Name` is knowingly unlengthed. `DeclaredIndexLiveTests.cs:20-24` — that XML
doc's "out of scope here, not silently fixed" paragraph is now stale; point it at this task.

**7 — verify the other three providers are untouched.** Run `Birko.Data.SQL.Tests`, `.SqLite.Tests`,
`.MySQL.Tests`, `.PostgreSQL.Tests` (their `IndexedStringColumnTypeTests` already pin
`TEXT`/`LONGTEXT`/`VARCHAR(255)`), plus `Birko.Data.SQL.MSSql.View.Tests` and `Birko.Data.Migrations.SQL.Tests`.
No source change expected in any; if one goes red, that is the finding.

**8 — record the decision.** Aggregator repo: a § Conventions entry (the rule + why `MAX`, why 255 for keys, and
the "existing `TEXT` columns are not migrated" sentence), a `### … (date)` Recent Updates entry with the measured
revert counts, and the task file's acceptance boxes + `pr:` SHAs.

**9 — reverts, run one at a time** (below), then the three commits in order: production
(`Birko.Data.SQL.MSSql`, then the comment-only `Birko.Data.SQL` + `Birko.Data.SQL.MySQL`), tests
(`Birko.Data.SQL.MSSql.Tests`), aggregator last with the SHAs.

### How each acceptance criterion is met

| Criterion | Met by |
|---|---|
| A decision recorded with its reason, stating what an unlengthed `string` means on MSSql | Step 8 (§ Conventions + Recent Updates) and step 1's in-code comment. The statement is the block quote above; it names `StringField` vs `CharField` as the concrete meaning of "unlengthed" (finding 1). |
| `==`, LIKE family, `ToLower()`, `SortBy` all execute and return the right rows, asserted on values | Step 4 — every assertion on returned rows/sequences, or on a survivor count taken on a separate connection. `IN` and the `DeleteAsync(filter)` from the original consumer report are added because they are the same class and are not on the list. |
| An MSSql live fixture whose probe entity has an unlengthed string | Step 4's `MsStrRow` (no length attribute, no `ModelMap`) and step 5's two index probes; step 6 removes the rigging that made both existing fixtures look bounded. |
| Stated explicitly what happens to a database that already holds `TEXT` columns | § Decision, third block: nothing is altered (`MSSqlConnector.cs:271`'s `IF NOT EXISTS` + no reconcile path), the predicate keeps failing, the operator runs the named `ALTER`, blast radius measured as zero deployments. Also stated: `SchemaDriftCheck` will not report it. |
| **Proven able to fail** | Three distinct reverts, each with a named test. **(a)** restore `return "TEXT"` in the string arm → `StringPredicateLiveTests` fails on equality (402), `IN` (402), `ToLower` (8116) and both sort tests (306), and the offline `An_unlengthed_string_declares_nvarchar_max` fails. The **LIKE tests stay green** — measured, they pass on `text` — so they are pins, and a revert that took them down too would mean the harness is testing something other than what it claims. **(b)** drop only the `IsInIndexKey` branch, keeping `NVARCHAR(MAX)` → `IndexOverUnlengthedStringLiveTests` fails on both index entities (`IndexCreationFailures` non-empty, 1919) and on the `[UniqueField]` `CREATE TABLE` (also 1919), while **every predicate test stays green** — which is what proves the two halves independent. Already established ahead of the code by step 0's measurement that `NVARCHAR(MAX)` is refused as a key exactly as `TEXT` is. **(c)** drop `DataBase_Table.cs:198` (per-property marking) keeping `:264` → the `[IndexedField]` test fails and the `[CompositeIndex]` one does not, and vice versa. TASK-248's revert failed 0 here; if either direction of (c) fails 0, the suite is missing an entity, not the fix. A fourth is available if wanted: revert `IsInIndexKey` to `IsIndexed` alone → only the unique/primary cases fail, isolating the widening from the bound. Record all counts in step 8. |

### Tradeoffs and risks

- **Could this break the other three providers?** No code path is shared: `ConvertType` has one implementation
  per provider and `MSSqlConnector` has no subclass (finding 3). Steps 2/2b are comment-only.
  `AbstractField.IsIndexed` already exists and is already set; MSSql becomes its **second** reader, which is
  additive. Residual risk is the *shared* flag acquiring a second meaning — mitigated by the other three suites'
  existing `TEXT`/`LONGTEXT` pins (step 7).
- **`NVARCHAR(MAX)` is off-row / LOB-ish and cannot be an index key.** So was `TEXT`, and worse; but a consumer
  who silently relied on a wide string column now gets one they can also sort and group by, at whatever cost
  `nvarchar(max)` carries versus a bounded `nvarchar`. The escape hatch is `[MaxLengthField(n)]`, portable and
  visible at the model — say so in the rule.
- **The 255 bound is a new refusal surface on MSSql.** A previously-accepted 300-character value in an indexed
  column now fails the write. Deliberate and pinned by a test (TASK-248's *degrade the thing the caller can
  see*), and the measurement says no live entity is near it. **Asymmetry to record**: the same property is
  `NVARCHAR(MAX)` if unindexed and `NVARCHAR(255)` if an index names it, so **adding an index to a shipped
  entity narrows its column** — on a fresh table only, since nothing alters an existing one.
- **`IsInIndexKey` is read by MSSql only, and MySQL has the identical hole** (`LONGTEXT UNIQUE` → ERROR 1170 at
  `CREATE TABLE`, confirmed by reading `MySQLConnector.cs:269`). Adding the property is additive — nothing else
  reads it — so it cannot change MySQL, PostgreSQL or SQLite DDL. Do **not** widen this task to MySQL; that
  needs a live 8.4 measurement → split signal 2. **A test should assert MySQL still reads the narrow flag**,
  otherwise the deliberate asymmetry looks like an oversight and someone unifies it from symmetry.
- **Needs a live SQL Server:** step 0, steps 4 and 5, and all three reverts. Steps 1-3 and 6-8 are offline. A run
  without `BIRKO_MSSQL_HOST` reports green having exercised only the offline half — set `BIRKO_REQUIRE_LIVE`
  (`DeclaredIndexLiveTests.cs:45,60-63`).
- **`DataBase._tableCache` is static per process** (`DataBase_Table.cs:16`), so each probe entity needs a
  distinct CLR type and table name, and no test may mutate a shared entity's flags.
- ~~Unverified-by-reading claims that step 0 must settle~~ → **all settled, see § Step 0.** Two were wrong
  (equality is 402 not 8116; `DISTINCT` is a fourth code, 421). Kept in the record because "measure, don't
  cite" earned its place here: the draft would have shipped a test asserting the wrong error number, and a
  comment stating it.

### Split signals — DEFERRED to the close gate (decided 2026-08-21)

**Resolved at the close gate — all now have ids**, plus two more the gate itself found:

| Signal | Owner |
|---|---|
| 1 — `SchemaField` drops `MaxLength` / never sets `IsIndexed` | [[TASK-264]] |
| 2 — MySQL's `IsUnique`/`IsPrimary` hole (`LONGTEXT UNIQUE`, 1170) | [[TASK-265]] |
| 3 — `TimeOnlyField` unbounded despite fixed width 8 | [[TASK-268]] |
| 4 — nothing reports a stale declared column *type* | [[TASK-269]] |
| 5 — `CharField.Lenght` is `int?`, `NVARCHAR()` is a syntax error | [[TASK-268]] |
| **6 — `VARBINARY(MAX)` has the identical index-key defect** (found by the close-gate review, measured 1919) | [[TASK-266]] |
| **7 — the project-local `verify-conventions` did not shadow, so its checks did not run** (observed at this gate) | [[TASK-267]] |

Signals 3 and 5 were grouped into one task, per § Conventions' *several small ones from the same thread → one
grouped task*. Signal 2 was the one flagged as needing the hardest look, and it got its own task rather than
being folded in: `IsInIndexKey` was built to serve it, but changing MySQL's emitted DDL needs a live 8.4 run
this task did not do.

1. **`SchemaField` drops `FieldDescriptor.MaxLength` (and `IndexName`)** — `Birko.Data.Migrations.SQL/Context/SchemaField.cs:10-13`.
   A migration's declared `maxLength` is silently ignored on **all four** providers, and a migration-created
   string column can never take the bounded branch. Third instance of TASK-245's *look for the field that gets
   lost on the way in*. `Birko.Data.Migrations.SQL.Tests` asserts nothing about it today.
2. **MySQL's `IsUnique`/`IsPrimary` gap** — `[UniqueField]`/`[PrimaryField]` on an unlengthed string emits
   `LONGTEXT UNIQUE`, ERROR 1170 at `CREATE TABLE`, so TASK-248's fix covers `[IndexedField]`/`[CompositeIndex]`
   only. Same one-line shape as step 1, different provider, needs a live 8.4 measurement.
3. **`TimeOnlyField` should be a bounded field, not an unbounded one** — fixed-width 8 characters by
   construction (`TimeOnlyField.cs:49`) yet declaring the widest string type on every provider. Making it a
   `CharField(8)`-equivalent would make its documented ordering guarantee true everywhere and needs no
   per-provider branch. This task merely stops it being *broken* on MSSql.
4. **Nothing reports a stale declared column type.** Symbio's `SchemaDriftCheck` compares presence only, and the
   framework has no schema-drift surface at all. Consumer-side, or a framework `IIndexManager`-style column-type
   probe.
5. **`CharField.Lenght` is `int?` and `NVARCHAR()` / `VARCHAR()` is a syntax error** in three connectors.
   Unreachable from any model (finding 1) but reachable from public surface. A one-line defensive default across
   MSSql/MySQL/PostgreSQL, or a `CharField` constructor that refuses null — a decision, not a defect.

## Human test plan

- [x] N/A — mechanical; the proof is a predicate executing against a real MSSql and returning the right rows,
      which the live suites do. Nothing here needs human judgement or hardware.
