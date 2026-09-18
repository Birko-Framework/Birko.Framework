---
id: TASK-245
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-204, TASK-243, TASK-246, TASK-247, TASK-248]
findings: []
pr: "Birko.Data.SQL 7bda999 | Birko.Data.SQL.MySQL 280523d | Birko.Data.SQL.PostgreSQL d0959f3 | Birko.Data.SQL.MSSql c5a3a8a"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MSSql]
---

# Index DDL every provider accepts — MySQL rejected the clause, PostgreSQL could not resolve the columns

## The defect — measured end-to-end

`AbstractConnectorBase.CreateIndexSql` emits:

```sql
CREATE {UNIQUE }INDEX IF NOT EXISTS `name` ON `Table` (`Col`, ...)
```

**MySQL does not support `IF NOT EXISTS` on `CREATE INDEX`.** Measured on MySQL 8.4:

```
mysql> CREATE INDEX IF NOT EXISTS ix_probe ON MdlProbe (v);
ERROR 1064 (42000): You have an error in your SQL syntax; ... near 'IF NOT EXISTS ix_probe ON MdlProbe (v)'
```

MSSql overrides `CreateIndexSql` (it wraps the statement in
`IF NOT EXISTS (SELECT * FROM sys.indexes ...)`); PostgreSQL and SQLite both support the `IF NOT EXISTS`
clause. **MySQL is the one supported provider that neither overrides nor supports it**, so every declared
index on a MySQL entity fails to build.

End-to-end against MySQL 8.4, via `MySQLConnector.CreateTable`:

```
declared indexes = 1
index SQL        = CREATE INDEX IF NOT EXISTS `ix_idxrows_code` ON `IdxRows` (`Code`, `Other`)
FAILURE          = index 'ix_idxrows_code' on table 'IdxRows': <that statement>
present on table = (none)
```

Every `[IndexedField]`, `[CompositeIndex]` and fluent `HasIndex` on MySQL is therefore **absent** — no
index, and for a `UNIQUE` one, no constraint either.

## Why it has been shipping unnoticed

[[TASK-204]] deliberately made schema-ensure **record** an unbuildable index instead of throwing, because
an unbuildable index used to take down the entity's whole read surface. That is the right call and this is
its cost: on MySQL the failure lands in `AbstractConnector.IndexCreationFailures` and raises
`OnIndexCreationFailed`, and a host that subscribes to neither sees nothing at all. The degradation is not
silent by accident — it is silent because nobody is listening.

Not caught by tests either: there was no live MySQL suite in the tree until TASK-242, and the index tests
that exist (`ClassLevelCompositeIndexEndToEndTests`, `CompositeUniqueIndexEndToEndTests`) run on SQLite,
which accepts the clause.

## Shape of a fix

Override `CreateIndexSql` in `MySQLConnector`. MySQL has no conditional-DDL form for this, so the options
are:

1. **Probe then emit.** `SqlIndexManager.IndexExistsSql` already asks
   `information_schema.statistics` for exactly this, and `MySqlIndexManager` already declares the base
   implementation correct for MySQL — so the query exists and is the natural reuse. Costs one round trip
   per declared index per schema-ensure, which is per store instance, which with scoped stores is per
   request. Measure that before accepting it.
2. **Emit and tolerate duplicate-key-name (error 1061).** One statement, no probe; `CreateTable` already
   catches and records, so the change is to *not record* a failure whose only cause is "already there".
   Cheaper and closer to the `IF NOT EXISTS` semantics being emulated. Needs the error code checked
   against the driver, not the message.
3. **`ALTER TABLE ... ADD INDEX`** — same problem, no conditional form. Not a fix on its own.

Option 2 looks right; option 1 is the safer read if 1061 turns out not to be the only shape.

⚠ Whichever is chosen, the emitted statement is **DDL** and must go through `DoDdlCommand`
(TASK-243) — on MySQL it implicitly commits an open transaction, and index creation runs inside
schema-ensure, which is exactly the path that defect was about. `CreateIndexes` already routes through the
funnel today; a new MySQL override must not reintroduce a `DoCommand` bypass, which is precisely how
TASK-243's first attempt failed.

## Acceptance

- A gated MySQL test that declares a `[CompositeIndex]` (unique and non-unique), runs schema-ensure, and
  asserts the index is **present in `information_schema.statistics`** — not merely that nothing threw, and
  not merely that `IndexCreationFailures` is empty.
- The same assertion for the second schema-ensure on an already-indexed table: no failure recorded, no
  duplicate index, and — if option 1 is taken — a stated count of the extra round trips.
- A test that a genuinely unbuildable index (a `UNIQUE` over data that already violates it) is still
  **recorded rather than thrown**, so this fix does not undo TASK-204.
- Prove the index DDL still honours a transaction boundary on MySQL: declare the index, open a boundary,
  write, roll back, expect zero rows.

## Out of scope

- **PostgreSQL has the identical symptom — MEASURED, not suspected** (see plan risk R4, and the
  PostgreSQL 16 probe recorded there). `CreateIndexSql` quotes column identifiers while base-table DDL
  emits them bare, so on PostgreSQL the quoted name cannot resolve the folded stored column: **no
  declared PascalCase index can be created there either**, silently, since TASK-204. Seventh instance
  of the identifier family. Spawned as its own task — this one is MySQL.
- **Declared-vs-actual index reconciliation.** A same-name/different-columns index is silently
  accepted after this fix — faithfully, because PostgreSQL's and SQLite's `IF NOT EXISTS` are also
  name-only skips. Closing that is a new capability across four providers, not a widening.
- The SQLite index suites (`ClassLevelCompositeIndexEndToEndTests`, `CompositeUniqueIndexEndToEndTests`)
  stay untouched — they are the evidence that the conditional clause is still emitted where it works.

## Implementation plan

⚠ **Acceptance criteria question:** the four criteria cover `CREATE INDEX` only, and two adjacent
emitters are broken on MySQL by the *identical* clause:
(a) `AbstractConnectorBase.DropIndexSql` (`AbstractConnectorBase.cs:527`) emits ``DROP INDEX IF EXISTS `name` `` —
measured ERROR 1064, and doubly wrong on MySQL since it also omits the mandatory `ON <table>`;
(b) `SqlIndexManager.CreateUniqueIndexSql` (`SqlIndexManager.cs:176-182`) emits its own
`CREATE UNIQUE INDEX IF NOT EXISTS` and `MySqlIndexManager` does not override it, so after this fix
`IIndexManager.CreateAsync` on MySQL would *start* working for non-unique indexes and *keep* failing for
unique ones — an asymmetry created by the fix itself.
Both are one clause in the same family (§ Conventions "guard the whole verb family or none of it").
**DECIDED 2026-08-18: both (a) and (b) are IN SCOPE** — the whole verb family or none of it. (b) in
particular is an asymmetry this fix would otherwise *create*, so leaving it out would ship an index
manager that works for non-unique indexes and fails for unique ones. Steps 4.4/4.5 and reverts E/F
are therefore live, and the four acceptance criteria are knowingly exceeded — recorded here rather
than by silently widening the criteria list.

### Grill outcome (2026-08-18) — resolved decisions

The grill measured three of the plan's assertions and reshaped four of its steps. **Where this block
disagrees with §§ 1–9 below, this block wins**; the superseded text is kept because the way it was wrong
is part of the record.

#### Resolved decisions

- **Fix option → option 2** (emit plain `CREATE INDEX`, tolerate 1061) — `IIndexManager` is async-only, so
  option 1 has no sync probe for the sync `CreateIndexes` at all; and `IndexExistsSql` has no
  `TABLE_SCHEMA` filter, so option 1 would consume a known false-positive defect and *skip* the index.
- **Base `CreateIndexSql` column quoting → UNQUOTED, here, not spawned** — measured: PostgreSQL 16 cannot
  resolve `"Status"` against the folded `status` that bare-column base-table DDL creates, so **no declared
  PascalCase index can be created on PostgreSQL either**; bare columns work on MySQL including `DESC`;
  MSSql overrides so is unaffected; and no existing test asserts the quoted form (they use substring
  `Contain`, which a bare identifier satisfies). § Conventions already mandates bare column identifiers,
  so this is bringing a stray sink into line, and TASK-209 already measured the reserved-word objection
  away. **Supersedes step 4.4's "keep `QuoteIdentifier` on columns"** — the MySQL override now inherits the
  base's column rendering and differs from it only by dropping the conditional clause.
- **Tolerance placement → at `CreateIndexes[Async]`, WITH an explicit opt-out** — 1061 means "already
  there", which the other three providers already report as success, so tolerating it makes MySQL agree
  rather than diverge; the sole external caller (`SqlSchemaBuilder.cs:330`) actively wants idempotence.
  Nothing in the tree asserts the current behaviour, so the plan's contract pins are new coverage.
- **Opt-out shape → parameterise the emitter too** — `CreateIndexes(tableName, indexes, bool throwIfExists = false)`
  **and** `CreateIndexSql(tableName, index, bool conditional = true)`. A bare `throwIfExists` would be
  honourable on MySQL alone and silently ignored on the three providers whose conditional DDL cannot raise —
  the exact silent-drop shape § Conventions ranks worst. With the emitter parameterised the flag *means* the
  same thing everywhere: default = uniform "ensure", `throwIfExists: true` = uniform "create, fail if
  present" (base drops `IF NOT EXISTS`, MSSql drops its `sys.indexes` guard, MySQL stops tolerating 1061).
  **`Birko.Data.SQL.MSSql` therefore joins `affects:`.**
- **`CreateUniqueIndexSql` family → COLLAPSED to one producer** — `ToSqlIndexDefinition`
  (`SqlIndexManager.cs:196`) never set `Unique`, which is the sole reason a parallel unique emitter exists in
  three classes plus the ternary at `CreateAsync:61-63`. Set the property, always call
  `_connector.CreateIndexSql`, and **delete `CreateUniqueIndexSql` from the base, `PostgreSqlIndexManager:42`
  and `MSSqlIndexManager:42`** — verified byte-equivalent to what delegation produces on each.
  **Supersedes step 4.5**: asymmetry (b) disappears with **no MySQL override at all**, PostgreSQL's
  index-manager quoting is fixed for free, and the new `conditional` parameter lives on one emitter instead
  of four. Accepted cost: removes a `protected virtual` extension point (no subclass in this tree).
- **1061 on a same-name/different-columns index → faithful emulation, accepted, not a hole** — measured on
  PostgreSQL 16: `CREATE INDEX IF NOT EXISTS ix_skip ON "SkipT" (w)` with `ix_skip` already on `(v)` emits
  *NOTICE: relation already exists, skipping* and **keeps the old definition**. MSSql's guard compares
  `name` only, so it is name-only by construction. SQLite **inferred, not measured** (no CLI available) —
  same clause. So every provider silently accepts a redefined index, before and after this change.
- **Tracking shape → one task, retitled, `affects:` widened** — precedent in this repo (TASK-242's 21
  connector methods + 24 store publications + 43 tests; TASK-243's four suites) favours one coherent task
  over a STORY, and splitting would add cross-task ordering overhead for identical work. The four original
  acceptance criteria stand; criteria for the PostgreSQL unquote and the conditional parameter are added
  explicitly rather than absorbed silently.
- **`SqlIndexBuilder.Build` never sets `Unique` → spawned as TASK-246** — a migration's `.Unique()` emits a
  plain `CREATE INDEX` on all four providers, so a declared unique **constraint** silently is not one. Found
  during this grill; not MySQL-specific and not covered by any criterion here.
- **`SqlSchemaBuilder`'s raw-SQL fallbacks → spawned as TASK-247** — `:335-337` emits
  `CREATE … INDEX IF NOT EXISTS` with quoted columns (rejected by MySQL, unresolvable on PostgreSQL) and
  `:77` emits `DROP INDEX IF EXISTS … ON …` (rejected by MySQL, invalid syntax on PostgreSQL, whose
  `DROP INDEX` takes no `ON`). Third and fourth copies of the clause this task fixes, reachable only when
  no connector is supplied.

#### Consequent change to the work list

| plan step | status after the grill |
|---|---|
| 4.1 `IsIndexAlreadyExistsException` on the base | unchanged |
| 4.2 / 4.3 the two `catch when` filters | unchanged, plus the `throwIfExists` short-circuit |
| **new** | `CreateIndexSql` gains `bool conditional = true`; base emits columns **bare**; `CreateIndexes[Async]` gains `bool throwIfExists = false` |
| **new** | `MSSqlConnector.CreateIndexSql` honours `conditional`; its `CreateUniqueIndexSql` override deleted |
| **new** | `PostgreSqlIndexManager.CreateUniqueIndexSql` override deleted |
| **new** | `ToSqlIndexDefinition` sets `Unique`; `CreateAsync` ternary collapsed; base `CreateUniqueIndexSql` deleted |
| 4.4 MySQL `CreateIndexSql` / `DropIndexSql` / predicate | kept, **minus** the "keep `QuoteIdentifier` on columns" decision |
| **4.5 MySqlIndexManager override** | **DROPPED** — superseded by the collapse |
| 4.6 docs + spec regen | unchanged, plus the PostgreSQL identifier entry (7th instance of that family) |

#### Added acceptance criteria (beyond the original four)

- A gated **PostgreSQL** test that declares a `[CompositeIndex]` on a PascalCase-columned entity, runs
  schema-ensure, and asserts the index is present in `pg_indexes` — the R4 regression. Must fail against the
  quoted emitter.
- `CreateIndexSql(…, conditional: false)` emits no conditional form on **each** provider (base, MSSql,
  MySQL), and `CreateIndexes(…, throwIfExists: true)` throws for an already-present index on **each** — the
  parameter means the same thing everywhere, asserted rather than argued.
- One test per deleted `CreateUniqueIndexSql` override proving the collapsed producer emits what that
  override used to emit, so the deletion is a measured equivalence and not a hope.

### Live measurements (MySQL 8.4, docker `mysql:8.4`, executed 2026-08-18)

| statement | result |
|---|---|
| `CREATE INDEX IF NOT EXISTS ix ON T (v)` | **1064** syntax error — task's measurement confirmed |
| `CREATE INDEX ix ON T (v)` twice, identical columns | **1061** `Duplicate key name` |
| `CREATE INDEX ix ON T (w)`, name taken by `(v)` | **1061** as well |
| `ALTER TABLE T ADD INDEX ix (w)`, name taken | **1061** — option 3 confirmed dead |
| `DROP INDEX IF EXISTS ix ON T` | **1064** — `DropIndexSql` has the identical defect |
| `DROP INDEX ix ON T`, index absent | **1091** |
| `CREATE UNIQUE INDEX ux ON T (code)` over violating data | **1062** `Duplicate entry`, and stays 1062 on retry |

**1062 ≠ 1061**, so tolerating 1061 cannot swallow the genuinely-unbuildable case: TASK-204 survives
intact and acceptance criterion 3 is satisfiable with no extra machinery.

### 1. Decision: option 2 — emit plain `CREATE INDEX`, tolerate 1061 at the `CreateIndexes` funnel

| # | Repo / file | Change |
|---|---|---|
| A | `Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs` (new member near `:59` `SupportsTransactionalDdl`) | `public virtual bool IsIndexAlreadyExistsException(Exception ex) => false;` |
| B | `AbstractConnector_Create.cs:96-102` + `AbstractAsyncConnector_Create.cs:86-93` | wrap each per-index `DoDdlCommand[Async]` in `catch (Exception ex) when (IsIndexAlreadyExistsException(ex)) { }` |
| C | `Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs` (beside the `CreateTable` override at `:290`) | `override CreateIndexSql` — base text minus `IF NOT EXISTS`; `override IsIndexAlreadyExistsException` — walk `InnerException`, match 1061 |
| D | same file | `override DropIndexSql` — ``DROP INDEX `n` ON `T` `` (in-scope per the ⚠ note) |
| E | `Birko.Data.SQL.MySQL/IndexManagement/MySqlIndexManager.cs` | `override CreateUniqueIndexSql` delegating to `Connector.CreateIndexSql` with `Unique = true` (⚠ note) |

**Why the seam is `CreateIndexes`, not `CreateIndexSql`.** `CreateIndexSql` returns a `string`
(`AbstractConnectorBase.cs:512`), so it can carry the *statement* half only. The execution half has exactly
one pair of sites: `AbstractConnector_Create.cs:92-104` and `AbstractAsyncConnector_Create.cs:82-95`. Both
already route through `DoDdlCommand[Async]`, so a filter there sits **above** the TASK-243 funnel and
**below** both callers — schema-ensure and every explicit caller — so the two cannot disagree.

**Why not in the schema-ensure recording path.** The task text points at
`AbstractConnector_Create.cs:57-60`. Putting it there leaves the public `CreateIndexes` throwing 1061 on
MySQL while it silently no-ops on SQLite/PostgreSQL (`IF NOT EXISTS`) and MSSql (its `sys.indexes` guard,
`MSSqlConnector.cs:290`) — a provider divergence in a public API with a live caller:
`Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:330` would throw on any re-applied migration on
MySQL alone.

**The "public `CreateIndexes` still throws" contract is preserved, not changed.** That contract
(TASK-204; `Birko.Data.SQL/CLAUDE.md:273-275`) is about an index that *cannot be built* — on MySQL that is
**1062**. 1061 is "the object you asked for is already there", which the other three providers already
report as success. Tolerating 1061 makes MySQL agree with them. Put that sentence in the code comment at
both filter sites — it is what a future reader will otherwise "fix" back.

**Why not option 1 (probe via `SqlIndexManager.IndexExistsSql`)** — four reasons, most decisive first:

1. **There is no sync probe.** `IIndexManager` is async-only (`SqlIndexManager.cs:33`, `:227`); the sync
   `CreateIndexes` would have to block or grow a parallel sync probe.
2. A probe cannot live in `CreateIndexSql` either, so option 1 lands in the same place — buying nothing
   architecturally while costing a round trip and a TOCTOU window.
3. **`IndexExistsSql` is itself defective and option 1 would consume the defect.** `SqlIndexManager.cs:153-158`
   filters by `table_name` + `index_name` with **no `TABLE_SCHEMA` filter** (already filed,
   `SPEC-HARVEST-FINDINGS-2026-07-30.md:1836`). A same-named table in another reachable schema makes it
   answer "present", the index is **skipped**, and the report is suppressed too — the exact silent absence
   this task exists to remove.
4. One extra round trip per declared index per schema-ensure, which with scoped stores is per request.

### 2. Both paths, and the error-code plumbing

**Both sync and async are required** — the two schema-ensure loops are independent code
(`AbstractConnector_Create.cs:48-61`, `AbstractAsyncConnector_Create.cs:30-47`); `MySQLStore` reaches one,
`AsyncMySQLStore` the other. A one-sided fix ships a MySQL where sync stores index and async ones do not.

Use an **exception filter**, not a `catch` body — nothing else is caught at all, so the async site's
`OperationCanceledException` re-throw (`AbstractAsyncConnector_Create.cs:39-42`) is untouched by construction.

**The exception arriving at the filter is wrapped.** `RunCommandTransaction` (`AbstractConnector.cs:490-494`)
calls `InitException` -> `MySQLConnector_OnException` (`:115-132`) -> `throw new Exception(commandText, ex)`.
So the predicate **must walk `InnerException`**, exactly as `IsMissingTableException` does (`:101-104`):

```csharp
for (var current = ex; current != null; current = current.InnerException)
    if (current is MySqlException my && (int)my.ErrorCode == 1061) return true;   // ER_DUP_KEYNAME
return false;
```

`(int)ErrorCode` rather than `.Number`: it is the spelling the file already uses in `IsTransientException`
(`:70`), and the numeric literal + named comment is immune to driver enum-name drift (MySqlConnector 2.x,
`Birko.Data.SQL.MySQL.projitems:16`). `Birko.Data.SQL` cannot reference the driver type — hence the base
virtual returning `false` and a provider override, the same seam shape as `IsTransientException` /
`IsMissingTableException` / `SupportsTransactionalDdl`.

**No `MySQLConnector_OnException` change.** 1061 and 1062 both fail `IsMissingTableException`, so both are
re-thrown wrapped and reach the filter / recorder as they should. Verify rather than assume — one live
assertion (criterion 3's code check).

### 3. Override audit — the TASK-243 lesson, executed

Grepped `override` on every method reaching the new seam, across `Birko.Data.SQL*` **and** `Birko.Data.TimescaleDB`:

```
MSSqlConnector.cs:263        override void   CreateTable(string, IEnumerable<string>)
MSSqlConnector.cs:283        override string CreateIndexSql
MSSqlConnector.cs:294        override string DropIndexSql
MySQLConnector.cs:290        override void   CreateTable(string, IEnumerable<string>)
PostgreSQLConnector.cs:313   override void   CreateTable(string, IEnumerable<string>)
TimescaleDBConnector.cs:92   override void   CreateTable(string, IEnumerable<string>)
```

**Nothing overrides `CreateIndexes`, `CreateIndexesAsync`, `DropIndexes`, `DropIndexesAsync`** — nor
`CreateTable(IEnumerable<Tables.Table>)` / `CreateTableAsync(...)`, which are not even virtual. The four
overrides are of the *column-DDL* emitter and do not sit on the index path. So unlike TASK-243 this funnel
genuinely is one, and two filter sites cover every provider. **Confirm with Revert B, not with this
paragraph** — that is what the rule asks for.

Second consumer, named deliberately: `SqlIndexManager.CreateAsync` (`:60-75`) calls
`_connector.CreateIndexSql` but executes through its **own** `ExecuteNonQueryAsync`, so it does **not** pass
the new filter. Correct: an explicit `IIndexManager.CreateAsync` failure is already wrapped in
`IndexManagementException` and its callers have `ExistsAsync`. State it in the `MySqlIndexManager` doc
comment so the divergence is deliberate rather than discovered.

### 4. Production steps, in order

**4.1** — `AbstractConnectorBase.cs`: add `IsIndexAlreadyExistsException` immediately after
`SupportsTransactionalDdl` (`:59`), same doc shape — what it is for, why the default is `false` (the other
three emit or synthesise a conditional form so the condition never arises -> zero behaviour change off
MySQL), and that it means *"already there"* and explicitly **not** *"unbuildable"* (1062 stays a failure).

**4.2** — `AbstractConnector_Create.cs:96-102`: filter around the `DoDdlCommand` call **inside** the
`foreach`, so tolerance is per index. Comment: the statement being emulated is `IF NOT EXISTS`; MySQL has no
such form; 1061 != 1062 so TASK-204 is intact; the exception arrives wrapped by `InitException`, which is
why the predicate walks the chain.

**4.3** — `AbstractAsyncConnector_Create.cs:86-93`: same, pointing at the sync file for the reasoning (the
file's existing convention, `:25-29`).

**4.4** — `MySQLConnector.cs`, new region beside `CreateTable` (`:290`):

- `override string CreateIndexSql` — copy of `AbstractConnectorBase.cs:512-519` minus `IF NOT EXISTS`.
  ~~**Keep `QuoteIdentifier` on columns**~~ — **SUPERSEDED by the grill**: the base emitter now emits columns
  bare (§ Conventions), which is what fixes PostgreSQL, and this override inherits that. It differs from the
  base only by dropping the conditional clause.
- `override string DropIndexSql` -> `$"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(tableName)}"`.
  Measured: `IF EXISTS` is 1064 and `ON` is mandatory; an absent index is 1091, **not** tolerated (a
  `DropIndexes` caller asked for a specific index, and `SqlSchemaBuilder.DropIndex` at `:67-78` is a
  migration step that should fail loudly). Comment it — the base's `IF EXISTS` did tolerate it, so this is a
  deliberate provider-local difference.
- `override bool IsIndexAlreadyExistsException` — the chain walk above.

**4.5 — DROPPED by the grill (see Grill outcome above); superseded by collapsing the `CreateUniqueIndexSql` family to one producer. Retained for the record only.** — `MySqlIndexManager.cs`: `protected override string CreateUniqueIndexSql(...)` -> clone the
definition with `Unique = true` and return `Connector.CreateIndexSql(tableName, clone)`. One producer for
the MySQL statement, mirroring what `PostgreSqlIndexManager` / `MSSqlIndexManager` already do per dialect.
**Replace the stale comment at `:16-17`** ("the base implementation is already correct") — it is what made
this invisible.

**4.6** — docs: `Birko.Data.SQL/CLAUDE.md` § "Index creation during schema-ensure" (`:262-284`) gains the
MySQL bullet; `Birko.Data.SQL.MySQL/CLAUDE.md` the provider-terms version; aggregator `CLAUDE.md`
§ Recent Updates + § Conventions one entry under the TASK-243 block (this is its spawn); and
`docs/specs/schema-index-and-ddl.md` **regenerated** — lines **897-901** and **927-930** currently pin the
defect as behaviour ("rejected by MySQL") and must not be hand-edited.

### 5. Revert-based verification split

| Revert | Lines restored | Must fail | Proves |
|---|---|---|---|
| **A** | delete `MySQLConnector.CreateIndexSql` override (back to base `IF NOT EXISTS`) | criteria 1, 2, 4 + offline emitted-SQL tests | the statement half is load-bearing; tests can see the filed 1064 |
| **B** | delete only the two `when (IsIndexAlreadyExistsException(ex))` filters, keep the SQL fix | criterion 2's second-schema-ensure test only | the tolerance seam is separately load-bearing — valid SQL alone leaves every re-initialised store recording a failure per request |
| **C** | widen the predicate to any `MySqlException` | criterion 3's test | the narrowing; 1062 still recorded, TASK-204 survives |
| **D** | `DoDdlCommand[Async]` -> `DoCommandWithTransaction[Async]` at the two sites | criterion 4's boundary test | index DDL honours the TASK-243 funnel and the test can spot a bypass |
| **E** | delete the `DropIndexSql` override | the drop test | (⚠ scope) same clause, adjacent method |
| **F** | delete the `MySqlIndexManager.CreateUniqueIndexSql` override | the index-manager unique test | (⚠ scope) |

**Run A first, before writing the fix** — that is the "prove the test can fail" gate, and A is just the
unfixed tree.

### 6. Tests — files, repos, and what each criterion actually queries

All live edits land in `C:\Source\Birko\Framework.Tests\Birko.Data.SQL.MySQL.Tests` except 6.3.
Shared probe (columns confirmed against 8.4):

```sql
SELECT COLUMN_NAME, NON_UNIQUE, SEQ_IN_INDEX
FROM information_schema.statistics
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = @i
ORDER BY SEQ_IN_INDEX
```

`TABLE_SCHEMA = DATABASE()` is not optional — without it the probe has the same cross-schema false positive
as `IndexExistsSql` and would report an index the fix never created. `NON_UNIQUE = 0` means unique;
`SEQ_IN_INDEX` is 1-based.

**6.1 — NEW `DeclaredIndexLiveTests.cs`.** Gating copied verbatim from `LazyInitInsideBoundaryLiveTests.cs:56-81`
(`BIRKO_MYSQL_HOST`/`_PORT`/`_USER`/`_PASSWORD`/`_DB`, `RequireServer()`, `BIRKO_REQUIRE_LIVE` turns skip into
throw; `Exec` helper `:101-108`; `Dispose` drops the table `:110-114`). One model, two indexes, in the
`ClassLevelCompositeIndexEndToEndTests.cs:40-45` shape:

```csharp
[Table("IdxRows")]
[CompositeIndex("ux_idxrows_docnum", nameof(TenantGuid), nameof(Number), IsUnique = true)]
[CompositeIndex("ix_idxrows_status",  nameof(Status),     nameof(Number))]
public class IdxRow : AbstractModel { }
```

One `[Table]` + two `[CompositeIndex]` covers criterion 1's "unique and non-unique" in a single
schema-ensure, and also proves the per-index loop does not stop at the first index.

- **Criterion 1** — `Declared_indexes_are_present_in_information_schema_after_schema_ensure`: drop table;
  `new MySQLConnector(Settings()).CreateTable(new[]{ typeof(IdxRow) })`; probe both names. Assert
  `ux_idxrows_docnum` -> 2 rows, `COLUMN_NAME` = `TenantGuid`,`Number` in `SEQ_IN_INDEX` order,
  `NON_UNIQUE == 0`; `ix_idxrows_status` -> 2 rows, `NON_UNIQUE == 1`. Then, separately,
  `IndexCreationFailures.Should().BeEmpty()` as the explicitly **weaker** companion
  (`Birko.Data.SQL/CLAUDE.md:283`). Add an enforcement half (`AsyncMySQLStore<IdxRow>`, duplicate
  `(tenant, number)` -> throws; same number under another tenant -> succeeds) mirroring
  `CompositeUniqueIndexEndToEndTests.cs:54-71` — for a UNIQUE index the constraint is the point, not the
  catalogue row.
- **Criterion 1, async twin** — same via `CreateTableAsync`, because the async recorder is separate code.
- **Criterion 2** — `A_second_schema_ensure_over_an_indexed_table_records_nothing_and_duplicates_nothing`:
  `CreateTable` twice on one connector with an `OnIndexCreationFailed` subscriber counting invocations.
  Assert subscriber count 0; `IndexCreationFailures` empty; `COUNT(DISTINCT INDEX_NAME) == 2`; each index
  still exactly 2 rows. **Round-trip statement (option 2): zero extra round trips** — one `CREATE INDEX`
  attempt per declared index per schema-ensure, exactly as before; put that in the test's doc comment so the
  criterion's "if option 1 is taken" branch is answered explicitly rather than by omission. Run a third
  time on a *fresh connector* over the same database — the scoped-store shape.
- **Criterion 3** — `A_unique_index_over_violating_data_is_recorded_not_thrown`: create the table by raw SQL
  **without** indexes; insert two rows with identical `(TenantGuid, Number)`; then
  `connector.CreateTable(new[]{ typeof(IdxRow) })`. Assert (a) does not throw; (b) `IndexCreationFailures`
  has exactly one entry, `TableName == "IdxRows"`, `IndexName == "ux_idxrows_docnum"`; (c) **the recorded
  error's `InnerException` chain contains a `MySqlException` with `(int)ErrorCode == 1062`** — without (c)
  the test passes against the *unfixed* tree, which also records a failure, for 1064; (d) the probe reports
  0 rows for `ux_idxrows_docnum` and 2 for `ix_idxrows_status`, i.e. the failure did not hide the index
  behind it; (e) `AsyncMySQLStore<IdxRow>.ReadAsync` returns both rows — the read surface TASK-204 exists to
  protect. Async twin of (a)-(c).
- **Criterion 4** — `Index_DDL_from_a_cold_store_inside_a_boundary_does_not_commit_it`: drop the table;
  **cold** `AsyncMySQLStore<IdxRow>` (no warm-up — the whole point, cf. `LazyInitInsideBoundaryLiveTests.cs:47-50`);
  `await using var uow = SqlUnitOfWork.FromStore(store)`; `BeginAsync`; `CreateAsync` three rows;
  `RollbackAsync`. Assert `COUNT(*) == 0`, **and** that the table exists **and** both indexes are present —
  MySQL's pinned answer that schema DDL survives the rollback (§ Conventions, TASK-243's "two providers now
  answer oppositely and both are pinned"). Plus the committed control (`CommitAsync` -> 3 rows) and the
  sync-store `AmbientSqlTransaction.Enter` variant, copying `LazyInitInsideBoundaryLiveTests.cs:244-266`.
- **Public-contract pins** — `Explicit_CreateIndexes_is_idempotent_for_an_already_present_index` (call twice
  directly, no throw) and `Explicit_CreateIndexes_still_throws_for_an_unbuildable_unique_index` (violating
  data -> throws; `IndexCreationFailures` stays empty because nothing records outside schema-ensure). This
  pair **is** the record of what changed and what did not in the public path.
- **⚠ scope** — `DropIndexes_removes_a_declared_index_on_mysql` and `DropIndexes_for_an_absent_index_throws`
  (1091 is not tolerated).

**6.2 — EDIT `MySQLConnectorTests.cs`** (offline — these run in CI with no server, so they carry the
statement shape):

- `CreateIndexSql` for a unique 2-column definition: `NotContain("IF NOT EXISTS")`,
  `StartWith("CREATE UNIQUE INDEX ")`, contains the quoted index name, the quoted `ON` table, the column
  list in order with backticks; non-unique -> `NotContain("UNIQUE")`; an `IsDescending` column -> ` DESC`.
- `DropIndexSql`: equals ``DROP INDEX `ix_x` ON `T` `` — no `IF EXISTS`, `ON` present.
- `IsIndexAlreadyExistsException`: false for `new Exception("Duplicate key name 'x'")` (message-only must
  not match — the task's "check the code, not the message" made testable) and false for a wrapped plain
  exception. **The `true` branch cannot be tested offline**: `MySqlException`'s constructors are internal,
  so no 1061 instance can be fabricated. Say that in a comment and name the live test that pins it
  (criterion 2's) — an untestable positive branch silently claimed as covered is how a predicate ships
  inverted.
- `MySqlIndexManager.CreateUniqueIndexSql` (⚠ scope) -> equals `CreateIndexSql` with `UNIQUE`, no
  `IF NOT EXISTS`. It is `protected`; expose via a small test subclass, or assert through the existing
  `SqlIndexManagerTests` pattern.

**6.3 — EDIT `C:\Source\Birko\Framework.Tests\Birko.Data.SQL.Tests\IndexManagement\CompositeUniqueIndexTests.cs`**
(**different repo**, so the tests side is **two** repos): two contract pins on the existing `TestConnector`
(`IndexManagement/SqlIndexManagerTests.cs:96`) — the base `CreateIndexSql` **still** emits `IF NOT EXISTS`
(so nobody "unifies" the providers from symmetry after reading the MySQL override), and the base
`IsIndexAlreadyExistsException` returns `false` for any exception (the no-behaviour-change-off-MySQL claim,
asserted rather than argued).

**Do not touch** `ClassLevelCompositeIndexEndToEndTests.cs` / `CompositeUniqueIndexEndToEndTests.cs` — they
are the "clause still emitted where it works" evidence and must stay green untouched.

### 7. Commits — four, not three

`affects:` names two production repos, so this is the TASK-243 shape, not the three-row default:

1. `Framework/Birko.Data.SQL` — `fix(TASK-245): index creation tolerates "already there" where the provider has no conditional form`
2. `Framework/Birko.Data.SQL.MySQL` — `fix(TASK-245): MySQL emits index DDL MySQL accepts, and reports 1061 as already-present`
3. `Framework.Tests/Birko.Data.SQL.MySQL.Tests` — `test(TASK-245): declared indexes exist on live MySQL 8.4, and an unbuildable one is still only recorded`
   3b. `Framework.Tests/Birko.Data.SQL.Tests` — `test(TASK-245): pin the base conditional-DDL emitter and the default already-exists predicate`
4. `Framework/Birko.Framework` (here) — `tasks(TASK-245): MySQL can create declared indexes; 1061 tolerated, 1062 still recorded`

Order 1 -> 2 (2 compiles against 1's new virtual) -> 3/3b -> 4, so `pr:` references real SHAs. Stage
explicitly, never `git add -A`; no `Co-Authored-By:` trailer.

### 8. Risks, and what detects each

- **R1 — the wrapping layer changes the exception shape.** The chain walk survives `InitException`'s
  re-wrap, but a path that *swallows* would break the filter. Detected by criterion 2's test; if it fails,
  instrument `MySQLConnector_OnException` before touching the predicate.
- **R2 — `transaction.Rollback()` after the implicit commit.** `AbstractConnector.cs:492` rolls back before
  `InitException`. MySQL implicitly commits *before* the DDL, so the driver's `ROLLBACK` should be a
  server-side no-op and the original exception should survive; if `Rollback` itself throws, a *different*
  exception escapes, the filter misses, and a failure gets recorded. Criterion 2's test is the detector.
  Fallback: pass `inOwnTransaction: false` for the MySQL index path (the wrapper transaction is a fiction on
  MySQL anyway — `AbstractConnector.cs:276-283`), which would need its own `CreateIndexes[Async]` override
  and a re-run of the step-3 override audit.
- **R3 — schema-ensure re-entrancy inside `DoInit`.** `IsInitializing` gates `MySQLConnector_OnException`
  (`:124`); an index failure during auto-init already behaves this way and the routing is unchanged. One
  live pass with the table absent (criterion 4 covers it).
- **R4 — `CreateIndexSql` quotes column identifiers, against § Conventions.** `AbstractConnectorBase.cs:515`
  emits `QuoteIdentifier(c.ColumnName)` while base-table DDL emits columns bare. Harmless on MySQL. On
  **PostgreSQL** it is not: a bare DDL column folds to lower case, so a quoted `"Name"` in the index DDL
  cannot resolve it (42703) — meaning **PostgreSQL very likely cannot create any declared PascalCase index
  either**, silently, since TASK-204, with every index end-to-end test running on case-insensitive SQLite.
  Seventh instance of the identifier family and a second provider with this task's exact symptom.

  **MEASURED against PostgreSQL 16 (docker `postgres:16`), 2026-08-18 — confirmed, not suspected.**
  Replicating `CreateTable` exactly (quoted table, bare column definitions) then the shipped
  `CreateIndexSql`:

  ```
  columns as stored          : guid, tenantguid, number, status      (folded, as predicted)
  CREATE INDEX ... ("Status", "Number")            -> ERROR: column "Status" does not exist
  CREATE INDEX ... (Status, Number)                -> CREATE INDEX            (works)
  CREATE UNIQUE INDEX ... ("TenantGuid", "Number") -> ERROR: column "TenantGuid" does not exist
  pg_indexes after           : ix_idxrows_bare only
  ```

  So PostgreSQL cannot create **any** declared PascalCase index, unique or not — this task's exact
  user-visible symptom, by a different mechanism. Out of scope here (this task is MySQL) and spawned
  as its own task. Note the consequence for step 4.4: the MySQL override deliberately keeps
  `QuoteIdentifier` on columns so it takes no position the PostgreSQL fix would have to undo — but if
  that fix unquotes the **base** emitter per § Conventions, the MySQL override's column quoting
  becomes gratuitous divergence. Grill this before writing 4.4.
- **R5 — the spec pins the defect.** `docs/specs/schema-index-and-ddl.md:897-901` and `:927-930` assert the
  MySQL breakage as behaviour. Regenerate, review the spec diff as the intended-vs-unintended check.
- **R6 — the live suite skips by default.** Every criterion needs `BIRKO_MYSQL_HOST`; a CI run without it is
  green and proves nothing. Hence 6.2/6.3 carrying the statement shape offline, and the close gate must
  record the live run with `BIRKO_REQUIRE_LIVE` set plus the revert splits with counts.

### 9. Tradeoffs accepted, written down

- **A same-name/different-columns index is silently accepted.** Faithful to `IF NOT EXISTS` on the other
  three providers. Recorded, not fixed.
- **The public `CreateIndexes` becomes idempotent on MySQL.** Deliberate: it already is on the other three,
  and the "still throws" contract is about unbuildable (1062), unchanged. Pinned by two tests either way.
- **`DROP INDEX` on MySQL becomes strict** (1091 throws) where the base's `IF EXISTS` tolerated an absent
  index. Provider-local, deliberate, commented, tested — flagged because no criterion covers it.
- **Zero probe cost**, at the price of one error code per provider. The code is measured, and the base
  default `false` means no other provider is affected.

## Human test plan

**N/A — covered by automated tests.** Every claim here is a statement the server either accepts or rejects
and a catalogue row that either exists or does not, so there is nothing a human adds by looking. The
assertions query `information_schema.statistics` / `pg_indexes` / `sys.indexes` and count committed rows
after a rollback; "nothing threw" was deliberately never used as evidence, because this layer swallows.
Verified against live MySQL 8.4, PostgreSQL 16, SQL Server 2022 and on-disk SQLite.

## Outcome

**Two independent defects, one symptom, on the two providers most likely to be in production.** A declared
`[IndexedField]` / `[CompositeIndex]` produced **no index — and for a UNIQUE one, no constraint** — on MySQL
(the statement was a syntax error) and on PostgreSQL (the columns could not resolve). Both silent since
TASK-204 made schema-ensure record rather than throw, and invisible to the suite because every index
end-to-end test ran on case-insensitive SQLite.

### Measured, on four live servers

| provider | before | cause |
|---|---|---|
| MySQL 8.4 | no index, ever | `CREATE INDEX IF NOT EXISTS` → `ERROR 1064` (syntax) |
| PostgreSQL 16 | no index on any PascalCase entity | quoted `"Status"` vs the folded `status` that bare-column `CREATE TABLE` creates → `42703` |
| SQL Server 2022 | worked | already overrode the emitter with a `sys.indexes` guard |
| SQLite | worked | supports the clause; case-insensitive |

MySQL `DROP INDEX` was wrong twice over: `IF EXISTS` rejected **and** the mandatory `ON <table>` missing.

### What changed

| repo | change |
|---|---|
| `Birko.Data.SQL` | `CreateIndexSql(…, bool conditional = true)` — **columns bare**, table quoted; new `virtual IsIndexAlreadyExistsException` (default `false`); `CreateIndexes[Async](…, bool throwIfExists = false)` with a per-index tolerance filter; `ToSqlIndexDefinition` carries `Unique` (now `protected`); base `CreateUniqueIndexSql` **deleted**, `CreateAsync` ternary collapsed |
| `Birko.Data.SQL.MySQL` | `CreateIndexSql` (no conditional clause), `DropIndexSql` (`ON <table>`, no `IF EXISTS`), `IsIndexAlreadyExistsException` (1061, code-matched, chain-walked); stale "base is already correct" comment corrected |
| `Birko.Data.SQL.PostgreSQL` | `CreateUniqueIndexSql` override **deleted** — it carried its own quoted-column copy, so the index manager could never build a unique index on a PascalCase entity either |
| `Birko.Data.SQL.MSSql` | `CreateIndexSql` honours `conditional` (drops its guard); `CreateUniqueIndexSql` override **deleted** |

Error codes, all measured: **1061** `Duplicate key name` = already there → tolerated · **1062**
`Duplicate entry` = unbuildable → still recorded/thrown, TASK-204 intact · **1091** `Can't DROP` = absent
index, deliberately not tolerated · **1170** BLOB/TEXT without key length = **still broken, TASK-248**.

### Verification

**1,086 tests green across 14 suites** (`Birko.Data.SQL` 537, SqLite 220, MySQL 61, PostgreSQL 61, MSSql 53,
four View suites 57, Migrations 34, Caching 7, BackgroundJobs.SQL 25, Workflow.SQL 12, TimescaleDB 19), with
MySQL 8.4 / PostgreSQL 16 / SQL Server 2022 live and `BIRKO_REQUIRE_LIVE` set so a skip is a failure.
**61 new tests** — 23 in `Birko.Data.SQL.Tests` (9 emitter pins + 14 injection), 23 MySQL, 9 MSSql, 6
PostgreSQL. Also cleared against the whole-solution build: **0 errors, and 0 warnings in any file this task
touched** (the 55 pre-existing CS8602s are all in files it did not).

**Written test-first: the new MySQL suite failed 12 of 14 against the unfixed tree before any production
line changed.** The 2 that passed were "still throws" pins passing for the *wrong* reason (1064 rather than
1062/1091); both were strengthened to assert the error code and now discriminate.

| revert | result | proves |
|---|---|---|
| **A** drop MySQL `CreateIndexSql` override | **13 of 14** fail | the statement half is load-bearing |
| **A-PG** restore quoted columns in the base | **6 of 6** fail | the identifier half, on the provider where case matters |
| **B** neuter both tolerance filters | **3 of 14** fail | the seam is *separately* load-bearing — valid SQL alone leaves every re-initialised store recording a failure per request |
| **C** widen the predicate to any `MySqlException` | **4 of 14** fail | the narrowing; 1062 still recorded |
| **D** index DDL off `DoDdlCommand`, **sync** site | **1 of 14** fail | the TASK-243 funnel, and that the boundary test discriminates |
| **D** same revert, **async** site only | **0 of 14** | see below |
| **E** drop MySQL `DropIndexSql` override | **2 of 14** fail | same clause, adjacent method |
| **F′** drop the `Unique` hand-off | **1 of 6** fail | the emitter collapse |

Revert **F** as originally planned (restore the base `CreateUniqueIndexSql`) is not measurable: deleting the
base virtual makes an override uncompilable (`CS0115`). F′ measures the same claim from the root instead.

### Things worth carrying

- **The async path I patched was not the one anything calls.** `AsyncDataBaseStore.InitCoreAsync:137` invokes
  the **sync** `Connector.CreateTable` inside a `Task.Run`, so an async store's schema-ensure runs the sync
  index loop and `CreateIndexesAsync` has **no store-level caller** — reachable only via an explicit
  `CreateTableAsync`. Revert D against the async site alone fails 0 of 14 and reads as "the funnel doesn't
  matter". Fourth instance of TASK-243's "a funnel with four overrides is not a funnel". Both loops are
  wired and tested; measure against the sync site.
- **Three duplicate emitters existed because one property was dropped upstream.** `ToSqlIndexDefinition`
  never copied `Unique`, which is the sole reason `CreateUniqueIndexSql` existed on the base plus two
  managers — one carrying the quoted-column defect independently. **This task's own filed plan proposed
  adding a fourth override.** Copying one field collapsed all four into one producer and fixed a second
  PostgreSQL path for free.
- **An opt-out only one provider can honour is a silent no-op.** `throwIfExists` on `CreateIndexes` alone
  would have been meaningful on MySQL and ignored on the three providers whose conditional DDL cannot raise,
  so `CreateIndexSql` gained `conditional` too. Pinned per provider, including that PostgreSQL and MSSql now
  genuinely raise.
- **The contract it "preserved" was unasserted.** Nothing in the tree called `CreateIndexes` directly.
  "Still throws" (TASK-204) is about *unbuildable* (1062), never about "already present" (1061) — which the
  other three report as success. Narrowed to what it meant; both halves now pinned.
- **Choosing the test model found the third defect.** A plain `string` is `LONGTEXT` on MySQL and cannot be
  indexed at all (**1170**) — and that is the shape the canonical `CompositeUniqueIndexEndToEndTests` example
  declares. TASK-248, with a test asserting 1170 so the boundary cannot move silently. The criterion tests
  use `[MaxLengthField]` to isolate the 1064 defect rather than shaping the model until something passed.
- **Two test-side slips worth naming**, both mine and both caught by a red run rather than by reading:
  `Should().Equal(params string[])` swallowed a "because" string as a third expected element; and plain
  `AbstractLogModel` leaves `CreatedAt` at `0001-01-01`, which SQL Server's `datetime` cannot store (floor
  1753) while MySQL and PostgreSQL accept it — the SQL-specific `AbstractDatabaseLogModel` exists for that.

### A security regression the fix itself introduced, caught at the close gate

Making index **columns** bare is what fixes PostgreSQL — and it removed an *accidental* containment on a
second sink. The two paths that build index DDL differ in where the column name comes from:

| path | column name from | safe bare? |
|---|---|---|
| schema-ensure (`CreateTable`) | `[IndexedField]` / `[CompositeIndex]`, resolved against mapped properties | yes |
| `IIndexManager.CreateAsync` | `definition.Fields[].Name` — **caller text**, interpolated into `CommandText` | **no** |

`QuoteIdentifier` had been incidentally neutralising a payload on the second path. Measured after the
unquote: **9 of 14** new tests failed, with `Rank); CREATE TABLE Pwned (x INTEGER); --` reaching the DDL —
the same shape as SH-H023, where a rule field created a table.

Fixed with `DataBase.ValidateIndexFieldIdentifier`, which **shares `_bareIdentifier`** with
`ValidateRuleFieldIdentifier` so the two sinks cannot drift about what an acceptable identifier is, wired
into `ToSqlIndexDefinition`. It is the sanctioned weaker fallback (§ Conventions): `SqlIndexManager` has a
table name and no entity type, so metadata resolution is unavailable — it cannot fix a `[NamedField]`
remapping but it refuses every payload. Anchored `\A…\z`, with its own test, because .NET's `$` also
matches before a trailing newline. 14 tests in `IndexIdentifierInjectionTests`, including that the
legitimate plain and table-qualified forms still pass.

**The generalisable lesson** (now in § Conventions): when you remove quoting from an interpolated
identifier, enumerate that sink's callers **by provenance** — metadata-derived needs nothing, caller-derived
needs the check — and do not assume the quoting you deleted was decorative.

### Spec regen — and a map finding

`docs/specs/schema-index-and-ddl.md` had **five** scenarios pinning the defect as behaviour ("Unique path
bypasses the connector", "Translation drops the Unique flag", "MySQL adds no behaviour", the MSSql manager
guard, and "every column name passes through `QuoteIdentifier`"). All five rewritten per the stable-wording
rule; nothing else touched. **All five changes are intended** — each corresponds directly to a change above.

⚠ **The area's `sources:` globs omit `../Birko.Data.SQL/SQL/Connectors/*.cs`** — the files where the core of
this fix lives (`AbstractConnectorBase.CreateIndexSql`, `AbstractConnector[Async]_Create.cs`,
`MySQLConnector`, `MSSqlConnector`). So a regen of the area whose whole subject is index DDL **cannot see
the emitters that produce it**, and would have reported a clean diff for the connector half. Exactly the
step-6 hazard the regen doc names ("a clean diff reads as *nothing changed* when it means *nothing was
looked at*"). `.map.yml` is human-owned, so this is reported rather than edited:

```yaml
      # index DDL emitters — the statement shapes this area specs
      - ../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs
      - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Create.cs
      - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Create.cs
      - ../Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs
      - ../Birko.Data.SQL.MSSql/Database/Connector/MSSqlConnector.cs
```

### Spawned

- [[TASK-246]] — `SqlIndexBuilder.Build()` never copies `_unique`, so a **migration's `.Unique()` builds a
  non-unique index on all four providers**. Same lost-flag root cause as the emitter collapse above; the
  raw-SQL fallback three lines below *does* honour it, which is what hid it. P1.
- [[TASK-247]] — the same builder's raw-SQL fallbacks carry a third and fourth copy of the broken clause
  (`IF NOT EXISTS` + quoted columns; `DROP INDEX IF EXISTS … ON …`, wrong on MySQL and PostgreSQL in
  opposite directions). Depends on this task so it can reuse corrected emitters.
- [[TASK-248]] — the 1170 unbounded-string ceiling above. P1.
