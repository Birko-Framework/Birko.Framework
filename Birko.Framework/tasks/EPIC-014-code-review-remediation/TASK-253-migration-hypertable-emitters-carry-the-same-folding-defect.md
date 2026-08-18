---
id: TASK-253
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: in-progress
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-209, TASK-211, TASK-245, TASK-249, TASK-255, TASK-259, TASK-260, TASK-261, TASK-472]
findings: []
pr: "Birko.Data.SQL 92f076f · Birko.Data.SQL.PostgreSQL 086602a · Birko.Data.SQL.Tests cf3c0a5 · Birko.Data.SQL.PostgreSQL.Tests 8c361bb · Birko.Data.TimescaleDB 48f572c · Birko.Data.TimescaleDB.Tests 4b14898 · Birko.Data.Migrations.SQL dc7806c · Birko.Data.Migrations.TimescaleDB c231ed2 · Birko.Data.Migrations.TimescaleDB.Tests 5a84948+17df5e8 · Birko.Data.TimescaleDB f88c796 · Birko.Data.TimescaleDB.Tests 0b3eb45 · [step 7] Birko.Data.SQL 85f8576 · Birko.Data.SQL.MSSql 2bde57f · Birko.Data.SQL.PostgreSQL f4de483 · Birko.Data.SQL.SqLite fd067a2 · Birko.Data.SQL.View fb4ebb2 · Birko.Data.SQL.Tests ec1c514"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB, Birko.Data.TimescaleDB]
---

# The migration hypertable emitters carry the same identifier defect — and one bypasses the DDL funnel

Spawned by Symbio **TASK-472**, which fixed `TimescaleDBConnector.BuildCreateHypertableSql` after measuring
that **no hypertable had ever been created for a PascalCase-named entity** on TimescaleDB. That fix was scoped
to the store path, because that is what TASK-472 needed. The same defect exists in at least three other
emitters that the task deliberately did not touch.

## What is wrong

### 1. `Birko.Data.Migrations.TimescaleDB` duplicates the broken statement, without even the escaping

`TimescaleDBMigration` (around lines 40–56) builds the same call by raw interpolation:

```csharp
var sql = $"SELECT create_hypertable('{tableName}', '{timeColumnName}'{chunkIntervalSql});";
```

- **Bare table** → the regclass folds, so a PascalCase table raises `42P01` — the exact defect TASK-472 fixed
  one layer over. Whether it is swallowed here depends on the migration context's error handling rather than on
  `PostgreSQLConnector.OnException`, so re-measure rather than assume it is silent.
- **Unfolded column** → `42703` against the bare-emitted, case-folded stored column.
- **No quote escaping at all**, unlike the connector, which at least doubled single quotes. `tableName` and
  `timeColumnName` are free text from the migration author.

`CreateHypertableWithSpace` has the identical shape plus a third interpolated identifier
(`spaceColumnName`), and `AddCompressionPolicy` / `BuildCompressionPolicySql` take a `tableName` and an
`orderByColumn` / `segmentByColumn` the same way — **audit the whole file, not just the two methods named
here.** This is the "enumerate that sink's callers by provenance" rule from TASK-245, which TASK-249 then had
to re-apply because the first pass found only one of two caller-derived sinks.

### 2. `TimescaleDBConnector.CreateHypertableAsync` bypasses the DDL funnel

The sync `CreateHypertable` goes through `DoDdlCommand` (TASK-243's funnel, with the
`SupportsTransactionalDdl` decision inside it). The async twin instead does its own
`CreateConnection(_settings)` + `OpenAsync`, so it neither joins an ambient boundary nor is suppressed off one
— it simply opens a second connection, which on PostgreSQL is legal and therefore silent. It is reachable
public API: `AsyncTimescaleDBStore.CreateHypertableAsync`,
`AsyncTimescaleDBModelRepository.CreateHypertableAsync` and
`Birko.Data.TimescaleDB.ViewModel`'s `AsyncTimescaleDBRepository.CreateHypertableAsync` all forward to it.

Note the shape TASK-245 hit twice: **check which twin the production path actually runs before costing the
fix.** Schema-ensure reaches the *sync* emitter even from an async store (`AsyncDataBaseStore.InitCoreAsync`
calls the sync `Connector.CreateTable` inside a `Task.Run`), so the async method's only callers are the three
explicit public ones above.

## Why it was left out of TASK-472

Different repo, different reachability, and a different fix shape — the migration emitters have no connector to
delegate to, so they need either their own quoting/folding or a shared helper. Bundling them would have widened
a verification task into a multi-repo refactor. Grouped here as one task rather than three, per the aggregator's
"several small ones from the same thread → one grouped task" rule.

## Acceptance criteria

- [x] Every `create_hypertable` / policy emitter in `Birko.Data.Migrations.TimescaleDB` quotes its table as an
      identifier inside the literal and pre-folds any column name, with single **and** double quotes escaped.
      One producer if at all possible — three copies of this statement is how the defect survived in the first
      place (TASK-245's "when you find the same statement written three times, look for what got lost upstream").
- [x] `CreateHypertableAsync` routed through `DoDdlCommandAsync`, or an explicit written reason why it must not
      be — and if it stays outside, say what that means for a caller inside a boundary.
- [x] Verified against **live TimescaleDB**, asserting against `timescaledb_information.hypertables` and not
      against "the call did not throw". TASK-209's lesson: this layer swallows, so a no-op is indistinguishable
      from success. `docker run -d -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=birkoview -p 5433:5432
      timescale/timescaledb:latest-pg16` is enough; TASK-472's suites gate on `BIRKO_TS_HOST`.
- [x] Proven able to fail: revert each substitution and watch the matching test go red. **Measured, per substitution:** bare regclass **15 of 34** offline + **11 of 11** live · unfolded column **5 of 34** · `FoldsUnquotedIdentifiers` forced false **1 of 555** · dropped PostgreSQL override **2 of 67** · async funnel un-routed **1 of 42** live · emitter delegation **0 of 39** and quote/escape order swap **0 of 555**, both zeros recorded rather than dressed up.
- [x] An injection test per caller-derived sink, matching `IndexIdentifierInjectionTests` — these take free
      text from a migration author and currently interpolate it with no escaping whatsoever.
- [x] **The hand-rolled `Replace("'", "''")` sites converge onto `EscapeLiteral`** — **18 call sites in 8
      files across 6 repos**, done 2026-08-18. One copy deliberately left (`CosmosDBDataMigrator`, a project
      that does not import `Birko.Data.SQL` and does not speak this dialect), recorded in the helper's doc so a
      later audit can tell a decision from an oversight. Revert fails **0 of 555 / 0 of 223 / 0 of 59**, the
      expected zero for a one-producer refactor. Verified with **all four providers live** — PostgreSQL 16,
      MySQL 8.4, SQL Server 2022, on-disk SQLite — because four of the sites are the index managers
      TASK-245/249 fixed days ago. Added 2026-08-18 by an explicit scope decision at the plan grill, recorded rather
      than absorbed silently. It is a behaviour-preserving refactor with no defect behind it, and my
      recommendation was to defer it — overridden, so it is a criterion rather than a plan aside. **Four of
      the sites are the `safeIndex`/`safeTable` pairs in the four index managers that TASK-245/TASK-249 fixed
      days ago, so the four provider live suites must be RUN, not merely built**, and the convergence commit
      is kept separate from the defect fix so a reviewer can read either alone.

## Out of scope

- **The hardcoded `time` column in `BuildContinuousAggregateSql` (line 139) — deferred to [[TASK-255]].**
  Found while auditing this file for the identifier defect, and it is a *different* class: CR-H070's
  hardcoded-column finding left unfixed in the method immediately below the one it was filed against.
  This task still quotes and escapes that method's identifiers; it does not make the bucketing column
  a parameter.
- **`selectClause` / `groupByClause` being raw SQL that cannot be contained — the redesign is
  [[TASK-260]].** This task still owes the interim: an XML doc on both parameters saying they are
  interpolated as SQL and must not be built from untrusted input, and a test pinning that they are *not*
  identifier-validated — otherwise the boundary is undocumented between now and whenever the redesign
  lands, and the next reader "hardens" them and breaks legitimate aggregate expressions.
  `compress_orderby` / `compress_segmentby` are **not** in this bullet: they sit inside a `'…'` literal, so
  `''` doubling contains them completely and this task contains them.
- **Routing the emitters through `connector.CreateHypertable` via `SetExternalTransaction` — and the defect
  found while rejecting it, [[TASK-259]].** That call publishes one caller's connection onto a connector
  cached process-wide and `SqlSchemaBuilder` never clears it; TASK-259 owns it. This task deliberately
  keeps the migration emitters on the migration's own connection.
- **`GetChunkInterval` reading a catalogue column TimescaleDB removed in 2.0 — [[TASK-261]].** Found by this
  task's own live suite (my first draft of a chunk-interval assertion failed for the same reason the product
  code does). It is catalogue drift rather than identifier handling, so a different cause and a different fix;
  latent, since nothing calls it. **Pinned here as current behaviour** so TASK-261 starts from a test that
  fails in the right direction.
- `IsHypertable` (line 163). Already correct — they parameterise, and
  `timescaledb_information.hypertables.hypertable_name` keeps its case, so the raw PascalCase name is
  the right thing to pass. This task adds live tests pinning it in both directions — the exact name is found,
  the folded one is not — so nobody folds it for symmetry with the emitters; it changes no code there.
- The other three providers' migration projects. This defect is PostgreSQL identifier folding, which
  only TimescaleDB/PostgreSQL has.
## Implementation plan

Drafted 2026-08-18, then grilled. The grill changed three decisions and produced two spawns
([[TASK-259]], [[TASK-260]]); what follows is the resolved version, with the rejected alternatives kept
because the reasons are the reusable part.

> ⚠ **Acceptance criteria question (unresolved, for the human):** criterion 1 says "quotes its table as an
> identifier inside the literal and pre-folds any column name". That is **two** treatments; this file needs
> **four**, because not every interpolated name sits inside a literal — see the classification below. Nothing
> contradicts the criterion; it is narrower than the work. **No edit is proposed** — flagged so you can decide
> whether to spell the split out in criterion 1.

### Classification — four treatments, and a containment axis the first draft lacked

| Position | Where it occurs | Treatment |
|---|---|---|
| `regclass` inside a literal | `create_hypertable('T',…)`, `add_compression_policy('T',…)`, `add_retention_policy`, `remove_*_policy`, `refresh_continuous_aggregate('V',…)` | quote as identifier, **then** escape for the literal (TASK-472's rule) |
| `name` inside a literal, compared to a catalogue | `create_hypertable`'s time column and space column | **pre-fold** + escape — the opposite of quoting |
| real identifier position in DDL | `ALTER TABLE {tableName}`, `CREATE MATERIALIZED VIEW {viewName}`, `FROM {sourceTable}` | `QuoteIdentifier` only — no literal escaping, no folding |
| expression fragment, not an identifier | `compress_orderby`, `compress_segmentby`, `timeBucket`, INTERVAL strings | escape only; **never** folded or identifier-validated (`ts DESC` is legal and is parsed, so the parser's own folding already applies) |

`BuildCompressionPolicySql` needs the table in rows 1 **and** 3 of that table — `ALTER TABLE {tableName}`
and `add_compression_policy('{tableName}')` — so TASK-472's "two identifiers, opposite treatments" shape
arrives inside a single method. Quoting the `ALTER TABLE` name is § Conventions (*quote table identifiers,
never quote column identifiers*), not an exception to it.

**Containment, which is a different axis from treatment.** Anything inside a `'…'` literal is *completely*
contained by `''` doubling — you cannot leave a literal. So rows 1, 2 and 4 are all fully contained by this
task. Only `selectClause` / `groupByClause` are uncontainable, because they are raw SQL in statement
position; that is inherent to the parameters and is [[TASK-260]]'s redesign, with the interim doc + pinning
test owed here.

**Premise to state once, where `EscapeLiteral` lives:** `''` doubling is complete containment only while
`standard_conforming_strings` is `on` (PostgreSQL's default since 9.1). With it off, backslash escapes
revive and `\'` breaks out. `BuildCreateHypertableSql` already rests on this, so it is a framework-wide
premise to write down, not a regression this task introduces.

### The audit: nine emitters, not the three the Context names

| Line | Method | Interpolated | Currently |
|---|---|---|---|
| 44 | `CreateHypertable` | table, time col, chunk interval | bare, unfolded, **no escaping** |
| 55 | `CreateHypertableWithSpace` | table, time col, **space col**, chunk interval | same |
| 80 | `BuildCompressionPolicySql` | table (identifier position) | unquoted |
| 78/82 | `BuildCompressionPolicySql` | segmentBy col, orderBy col | unescaped literal |
| 84 | `BuildCompressionPolicySql` | table (regclass literal), interval | bare, unescaped |
| 94 | `AddRetentionPolicy` | table, interval | bare, unescaped |
| 104 | `RemoveCompressionPolicy` | table | bare, unescaped |
| 114 | `RemoveRetentionPolicy` | table | bare, unescaped |
| 136/139/141 | `BuildContinuousAggregateSql` | viewName, sourceTable, timeBucket, selectClause, groupByClause | unquoted, unescaped — plus a hardcoded `time` bucketing column → **deferred to [[TASK-255]]**, and raw-SQL fragments → **[[TASK-260]]** |
| 152 | `RefreshContinuousAggregate` | viewName | bare, unescaped |

`IsHypertable` (163) and `GetChunkInterval` (176) are **already correct and must not change**: they
parameterise, and `timescaledb_information.hypertables.hypertable_name` keeps its case, so the raw
PascalCase name is right. Folding them would break them — the trap TASK-472 hit from the other side. Pin
both with a live test so nobody "fixes" them for symmetry with the emitters.

### Step 1 — one producer, split by what is genuinely provider-neutral

The first draft put all three helpers in a static class in `Birko.Data.SQL`, justified by "no new project
dependency". The grill rejected that on two counts: `ToLowerInvariant` folding is **PostgreSQL semantics**
and a neutral project must not assert it, and the dependency argument is nearly worthless —
`Birko.Data.Migrations.TimescaleDB` has exactly **one** importer in the family
(`Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`) and it already imports both
`Birko.Data.SQL.PostgreSQL` and `Birko.Data.TimescaleDB`.

The resolved shape follows the four precedents already on the base — `SupportsTransactionalDdl` (:59),
`IsMissingTableException` (:80), `IsIndexAlreadyExistsException` (:558), `IsIndexMissingException` (:578):
a provider capability declared `virtual` on the neutral base, overridden per provider, consulted by one
producer.

```
Birko.Data.SQL/SQL/SqlLiteral.cs                    (static, ANSI, provider-neutral)
    EscapeLiteral(s) => s.Replace("'", "''")

Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs   (public, virtual capability + producers)
    public virtual bool FoldsUnquotedIdentifiers => false;      // PostgreSQLConnector overrides => true
    public string RegclassLiteral(string name)
        => SqlLiteral.EscapeLiteral(QuoteIdentifier(name));      // quote, THEN escape
    public string CatalogueNameLiteral(string name)
        => SqlLiteral.EscapeLiteral(FoldsUnquotedIdentifiers ? name.ToLowerInvariant() : name);
```

Public, matching `QuoteIdentifier` (:211) — and `SqlSchemaBuilder:143` already consumes that publicly from
the migrations side, so this is the established direction.

`FoldsUnquotedIdentifiers` reads `true` at every site this task touches (only the PostgreSQL family reaches
`create_hypertable`), so it is not exercised in both directions here. That is acceptable and is **not** the
"opt-out only one provider can honour" defect of TASK-245: every provider can give a truthful answer, and
the flag's job is to stop the next sink folding on a provider that does not fold. Assert the `false` side on
one non-PostgreSQL connector so the flag is not decorative.

### Step 2 — `BuildCreateHypertableSql` becomes an instance method

It must, to reach `RegclassLiteral`. Consequences, accepted:

- The **four TASK-472 offline tests call it statically** (`TimescaleDBHypertableAndSettingsTests.cs:35, 49,
  57, 70`), so they need a fixture line changed. Their **assertions stay byte-identical**, and the change
  goes in its **own commit ahead of the refactor**, so the diff shows plainly that the refactor's safety net
  was adapted, not rewritten. The constructor stores settings and opens no connection, so
  `new TimescaleDBConnector(new TimescaleDBSettings("h","db","u","p",5432,"ts","1 day"))` is free offline.
- **Rejected: an optional `AbstractConnectorBase? connector = null` parameter** defaulting to ANSI+fold,
  which would keep the four tests untouched. That is exactly the shape **TASK-247 deleted from
  `SqlSchemaBuilder` today**, having found that every test in that project took the null branch — a
  fallback nobody reaches is a second implementation that drifts, and it would make these four tests
  assert about code production never runs.

### Step 3 — apply the four treatments to all nine emitters

Mechanical once step 1 exists. The two judgement calls to record in comments are the `ALTER TABLE`
quoting (row 3, § Conventions) and the fragment bucket being escape-only (row 4, with the reason: they are
parsed, so folding them would be wrong, and identifier-validating them would refuse `ts DESC`).

### Step 4 — `CreateHypertableAsync` through the DDL funnel

Replace the hand-rolled `CreateConnection(_settings)` + `OpenAsync` with
`DoDdlCommandAsync(…, isLock: true, ct, inOwnTransaction: false)` — the exact mirror of the sync twin at
`TimescaleDBConnector.cs:113`. `PostgreSQLConnector.SupportsTransactionalDdl` is `true`, so nothing is
suppressed and the statement **joins** an ambient boundary instead of opening a second connection beside it.
Keep the `InitException` wrapping so the error surface is unchanged.

Verify while doing it that `DoCommandAsync` honours `ExternalConnection`/`ExternalTransaction` the way sync
`DoCommand` does (`AbstractConnector.cs:186`); say so if it does not rather than assuming.

### Step 5 — the migration emitters stay on the migration's own connection, and now for the right reason

`TimescaleDBMigration.ExecuteScript` runs its own `DbCommand` on the context's connection + transaction.
That stays. The first draft justified it as "delegating buys 1 of 9 emitters and adds a dependency"; the
grill found the real reason:

- Delegating to `connector.CreateHypertable` requires the migration to call
  `SetExternalTransaction(connection, transaction)` so `DoCommand` can see its connection — and that call
  **publishes one caller's connection onto a connector cached process-wide per (type, settings id)**. Both
  stores carry comments saying they *deliberately stopped* doing this (`DataBaseStore.cs:45`,
  `AsyncDataBaseStore.cs:48`); TASK-240 replaced it with `AmbientSqlTransaction`. Reviving it here would
  re-open what that task closed.
- `SqlSchemaBuilder` is its last caller and **never clears it** → **[[TASK-259]]**, spawned P1,
  measurement-first.

Write into the migration class doc that these statements run on the migration's own connection by design,
and that PostgreSQL's DDL is transactional so a failed migration rolls the hypertable conversion back with
it. That answers criterion 2's *"if it stays outside, say what that means for a caller inside a boundary"*
for the migration half.

### Step 6 — measure the unknown the Context flags

Run the **unfixed** `CreateHypertable` against live TimescaleDB with a PascalCase table and record whether
(a) `42P01` propagates out of `ExecuteScript` and fails the migration, or (b) something upstream swallows
it. `ExecuteScript` has no `try`/`catch` and `TimescaleDBMigrationRunner.ExecuteSingleMigration` adds none,
so **(a) is the expectation** — making the migration half *loud* where the store half was silent. Confirm
rather than assume: it changes how the commit describes blast radius, since a failing migration is visible
and an unconverted hypertable is not.

### Step 7 — converge the `EscapeLiteral` duplicates

The sixth acceptance criterion, added by explicit scope decision at the grill (my recommendation was to defer; overridden). Called "criterion 7" in the step-7 commit messages, after the plan step rather than the list position — there are six criteria, not seven.
~20 hand-rolled `Replace("'", "''")` sites across 8 projects, including four `safeIndex`/`safeTable` pairs
in `SqlIndexManager:182-193`, `MSSqlIndexManager:19-26`, `PostgreSqlIndexManager:19-26` and
`SqLiteIndexManager:23-47`.

- **Separate commit** from the defect fix, so either can be read alone.
- **Behaviour-identical by construction** — it is the same expression behind a name. Its revert therefore
  fails nothing, which is expected and must be *recorded as a zero* rather than reported as coverage.
- **Those four index managers are code TASK-245/TASK-249 fixed days ago**, so the four provider live suites
  must be **run**, not merely built. This is the one real risk the convergence carries.
- Leave `SqlBuilderContext.cs:64` and `DataBase.cs:1322` alone if they are escaping *values* rather than
  identifiers — check before converging; a value belongs in a parameter, and folding it into an
  identifier-flavoured helper would obscure that.

### Step 8 — tests

**`Birko.Data.Migrations.TimescaleDB.Tests`** — today one 62-line offline file, and its csproj imports
neither `Birko.Data.SQL.PostgreSQL` nor `Birko.Data.TimescaleDB`, so it has **no Npgsql and no live
capability**. Add both `Import`s + the Npgsql `PackageReference`. Tests live beside their project per
CLAUDE.md § Testing, and each existing live suite defines its own `BIRKO_*_HOST` gating rather than sharing
a helper — so duplicating ~30 lines of `RequireServer()` is the established pattern, not a smell.

- **Offline composition tests, PascalCase.** The four existing tests use `"metrics"` — already lowercase, so
  *they cannot distinguish the fix from the defect*, exactly TASK-472's finding about its own fixture. Keep
  them as the **discrimination control** (a lowercase name must survive every revert) and add a PascalCase
  counterpart per emitter.
- **Injection tests per caller-derived sink**, modelled on
  `Birko.Data.SQL.Tests/IndexManagement/IndexIdentifierInjectionTests.cs`, with a payload that tries to
  close the literal (`me'tric'); DROP TABLE x; --`) rather than a paren. Plus a test pinning that
  `selectClause` / `groupByClause` are **not** identifier-validated, and the doc comment saying why.
- **Live tests** gated on `BIRKO_TS_HOST`, reusing `HypertableSchemaLiveTests`'s `RequireServer()` /
  `BIRKO_REQUIRE_LIVE` shape. Assert against **`timescaledb_information.hypertables`**,
  `timescaledb_information.jobs` (compression + retention) and `continuous_aggregates`, plus chunk counts —
  never "the call did not throw" (TASK-209). Include the `IsHypertable` / `GetChunkInterval` case-intact
  pins.

**`Birko.Data.TimescaleDB.Tests`** — one live test that `CreateHypertableAsync` inside an
`EnterTransactionScope` boundary is rolled back with it, mirroring `BulkTransactionBoundaryLiveTests`. The
assertion is `timescaledb_information.hypertables` counted **on a separate connection after the rollback**,
not "no exception": on PostgreSQL the escaping call succeeds either way, which is why the defect is silent.
Plus one asserting `FoldsUnquotedIdentifiers` is `false` on a non-PostgreSQL connector.

### Step 9 — prove each substitution can fail

| Revert | Expected red |
|---|---|
| table quoting in `RegclassLiteral` | every PascalCase live test; the lowercase controls stay green |
| folding in `CatalogueNameLiteral` | `create_hypertable` time/space column tests only |
| `FoldsUnquotedIdentifiers` forced `false` | the same set as above — proves the flag is load-bearing, not decorative |
| literal escaping | the injection tests only |
| `ALTER TABLE` quoting | the compression-policy live test only |
| `CreateHypertableAsync` funnel routing | the boundary test only |
| `BuildCreateHypertableSql` delegating to the base helpers | **nothing** — a refactor whose net is TASK-472's existing suite; record the zero, do not claim coverage |
| swapping `RegclassLiteral`'s quote/escape order | **nothing — measured 0 of 555.** The two operations *commute* (each touches only its own metacharacter), so the draft comment's "this order is the safe one" was false and has been rewritten. Recorded rather than papered over with a test that cannot fail |
| the `EscapeLiteral` convergence | **nothing** — same reason; record the zero |

A revert that fails nothing *elsewhere* is a missing test (TASK-245, TASK-248).

### Step 10 — commits (polyrepo)

Order matters — production SHAs go into `pr:` before the aggregator commit:

1. `Birko.Data.SQL` — `fix(TASK-253): one producer for the PostgreSQL literal-identifier treatments`
2. `Birko.Data.TimescaleDB` — `fix(TASK-253): CreateHypertableAsync joins the DDL funnel; emitter reuses the base producers`
3. `Birko.Data.TimescaleDB.Tests` — `test(TASK-253): adapt the four static emitter fixtures to an instance call`

   > **Corrected 2026-08-18 while doing step 1.** The first draft ordered the test adaptation *ahead* of the
   > connector change, reasoning that it would make the net's adaptation legible. That is unbuildable: a
   > fixture calling `conn.BuildCreateHypertableSql(…)` does not compile until the method is an instance
   > method, so the intermediate state is broken in the tests repo. Production first, per the aggregator's
   > own polyrepo rule — legibility comes from the commit message and the untouched assertions, not from
   > the order.
4. `Birko.Data.Migrations.TimescaleDB` — `fix(TASK-253): nine hypertable/policy emitters quote, fold and escape`
5. the 8 projects touched by the convergence — `refactor(TASK-253): converge the hand-rolled literal escaping`
6. `Birko.Data.Migrations.TimescaleDB.Tests` + `Birko.Data.TimescaleDB.Tests` — `test(TASK-253): …`
7. `Birko.Framework` — `tasks(TASK-253): …`

### Risks

- **`create_hypertable`'s positional space-partition signature is deprecated in TimescaleDB 2.13+** in favour
  of `by_range` / `by_hash`. If the live `CreateHypertableWithSpace` test fails on
  `timescale/timescaledb:latest-pg16`, check the *signature* before concluding the fix is wrong — TASK-472's
  "check whether a reproduction failed for the reason you think". If it is genuinely removed, the method
  cannot work at all on a current server, which is a separate defect and gets its own task.
- **`ModelMapRegistry` accumulates into process-wide state.** Map each test model exactly once, in a static
  constructor, per `HypertableSchemaLiveTests`' own warning — two mappings for one type merge into
  `42P16 multiple primary keys`, which reads like a product defect.
- **Adding `Birko.Data.TimescaleDB` to the migrations test csproj** pulls `TimescaleDBSettings` (namespace
  `Birko.Data.SQL.TimescaleDB.Stores`) in alongside `Birko.Data.Migrations.SQL`'s `SqlMigrationSettings`.
  Check for a collision before assuming the import is free.
- **The convergence is the largest source of unrelated churn in this task** and touches recently-fixed index
  DDL. If the four provider live suites cannot be run, the convergence should be dropped back out rather
  than landed on a build-only check.

## Progress log

- **2026-08-18 — step 1 done: the producers exist, on the shape the grill chose.** New
  `Birko.Data.SQL/SQL/SqlLiteral.cs` (static `EscapeLiteral`, registered in the projitems);
  `AbstractConnectorBase` gained `FoldsUnquotedIdentifiers` (virtual, default `false`, documented beside
  `SupportsTransactionalDdl`) plus `RegclassLiteral` / `CatalogueNameLiteral`; `PostgreSQLConnector` overrides
  the flag to `true`. Build clean, **0 warnings**.
- **2026-08-18 — step 1 tests: 13 new, all green.** `Birko.Data.SQL.Tests/LiteralIdentifierProducerTests.cs`
  (555 total, was 542) with three `FakeConnector` variants — non-folding, folding, backtick-quoting — and
  `PostgreSQLConnectorTests` gained the two capability assertions (67 total, was 65). Sibling suites
  unaffected: SqLite 223, MySQL 68, MSSql 57, TimescaleDB 39, all green.
- **2026-08-18 — step 1 reverts: A 2 of 67 · B 1 of 555 · C 0 of 555.**
  A = drop the PostgreSQL override, hitting exactly the two PostgreSQL capability tests. B = fold
  unconditionally, ignoring the flag, hitting `CatalogueNameLiteral_PreservesCaseOnANonFoldingProvider` —
  which is what proves the capability is load-bearing rather than an unconditional `ToLowerInvariant()` in
  disguise, since every sink this task wires reads it as `true`.
  **C is the interesting one: it measured zero, and that was a defect in the doc comment rather than a gap in
  the tests.** Quote-then-escape and escape-then-quote produce the same string, because `QuoteIdentifier`
  touches only `"` and `EscapeLiteral` only `'`, so neither can introduce the other's metacharacter and the
  two commute. The draft comment said "the order is safe in one direction", implying a wrong order exists;
  it now states the commutativity and cites the measured zero, so a future reader who reorders it knows
  nothing depends on it. No test was added for a property that cannot break.
- **2026-08-18 — step 1 committed, 4 repos.** `Birko.Data.SQL` 92f076f · `Birko.Data.SQL.PostgreSQL`
  086602a · `Birko.Data.SQL.Tests` cf3c0a5 · `Birko.Data.SQL.PostgreSQL.Tests` 8c361bb. Production before
  tests, per the corrected order in step 10. Note `Birko.Data.SQL`'s default branch is **`master`**, not
  `main` like its siblings — worth knowing before any later scripted sweep across these repos assumes one
  name.
- **2026-08-18 — step 2 done + committed: the emitter reuses the producers.**
  `TimescaleDBConnector.BuildCreateHypertableSql` is now an instance method delegating to
  `RegclassLiteral` / `CatalogueNameLiteral` / `EscapeLiteral`. Behaviour-preserving, and **measured as
  such**: reverting the body to the hand-rolled escaping fails **0 of 39**, because TASK-472's four
  exact-string assertions still hold byte-for-byte. Four fixtures adapted (`Emitter()` helper), assertions
  untouched — verified by reading the diff, which is 4 call lines plus the helper and nothing else.
  `Birko.Data.TimescaleDB` 48f572c · `Birko.Data.TimescaleDB.Tests` 4b14898.
- **2026-08-18 — step 3 done + committed: all nine migration emitters, and there were nine.**
  The Context named three; the audit found **nine** interpolating emitters and **four** treatments, not two —
  the fourth being expression fragments (`compress_orderby`, the time bucket, the INTERVALs) which must be
  escaped but neither folded nor identifier-validated, since `ts DESC` is legal. `BuildCompressionPolicySql`
  needs the regclass *and* the plain-identifier treatment for the **same table in one statement**, which makes
  it the clearest instance of the whole class.
  **Four emitters had no extractable builder at all**, so their SQL was untestable offline; each got one,
  matching the four that did.
  `Birko.Data.Migrations.SQL` dc7806c (expose `SqlMigrationContext.Connector` — already required since
  TASK-247, just unreachable) · `Birko.Data.Migrations.TimescaleDB` c231ed2 ·
  `Birko.Data.Migrations.TimescaleDB.Tests` 5a84948.
  **Reverts: bare regclass 15 of 34 · unfolded column 5 of 34.** Tests 4 → 34.
- **2026-08-18 — two things the doing corrected, both about tests rather than code.**
  1. **The injection helper's first draft failed 16 of 16 against correct code.** It asserted
     `NotContain(";")` / `NotContain("--")` — but a *contained* payload still contains those characters, as
     inert text inside a literal, which is exactly what containment means. Rewritten to assert the payload
     appears in precisely the escaped form its treatment prescribes, plus a structural invariant (after
     collapsing doubled quotes, the remaining delimiters must be even). **A containment test that demands the
     payload's characters vanish is testing deletion, not containment.**
  2. **The "discrimination control" claim was too strong and the measurement bounded it.** The lowercase
     fixture survives the *folding* revert (5 of 34) and dies with the *quoting* revert (15 of 34) — correctly,
     since a table is quoted whatever its case. The comment said it "must survive every revert"; it now says
     which one it discriminates. A control is only a control against a named change.
  Also fixed in passing, compiler-forced: `TimescaleDBMigrationContext`'s connector was still
  `AbstractConnector? = null`, wrong since TASK-247 made the base's required (it emitted CS8604), and now
  load-bearing because the emitters resolve identifiers through it.
- **2026-08-18 — step 4 done + committed: `CreateHypertableAsync` joins the funnel.**
  It opened its own connection, so it escaped every boundary — TASK-242's defect in a method that sweep never
  reached, because it is not a bulk path. Now `DoDdlCommandAsync(..., inOwnTransaction: false)`, mirroring the
  sync twin. **The cancellation guard was kept deliberately**: the funnel routes failures through
  `InitException`, and PostgreSQL's `OnException` re-throws as a bare `Exception`, so a cancellation would stop
  being catchable as one. Every other async DDL path on this provider already behaves that way, so this method
  was the outlier — keeping its contract is a choice, recorded, because its three public callers take a token.
  Verified in passing that the async connector honours `AmbientTransaction` but **not** the legacy
  `ExternalConnection` pair the sync path checks; noted for [[TASK-259]], harmless here.
  `Birko.Data.TimescaleDB` f88c796.
- **2026-08-18 — live verification done, on TimescaleDB 2.29.2 / PostgreSQL 16 in Docker.**
  **`Birko.Data.Migrations.TimescaleDB.Tests` 45 (was 4)** · **`Birko.Data.TimescaleDB.Tests` 42 (was 39)**,
  both fully green live. New `MigrationEmitterLiveTests` (11) + 3 boundary tests, all asserting against
  `timescaledb_information` rather than "it did not throw".
  **The live reverts are what make this real: bare regclass fails 11 of 11 live**, and un-routing the async
  funnel fails **exactly 1 of 42** — the rolled-back-boundary test alone, with the committed and
  column-folding cases still passing. A suite that could not distinguish the fix from the defect would have
  reported success over a plain table, which is precisely what TASK-472 warned about.
- **2026-08-18 — three assumptions measured; one was a defect and one was a false alarm.**
  1. **The deprecated-signature risk did not materialise.** The positional
     `create_hypertable(relation, time_column_name, partitioning_column, number_partitions, …)` overload is
     still present on 2.29 beside the newer dimension-builder form, so the space-partitioning emitter works.
     Recorded as measured, since the plan had flagged it as a possible separate defect.
  2. **`GetChunkInterval` is broken on every TimescaleDB 2.x server** — it reads `chunk_time_interval` from
     `timescaledb_information.hypertables`, a column moved to `.dimensions` and renamed `time_interval` in 2.0.
     Raises `42703`. Spawned as [[TASK-261]] and pinned here; latent, nothing calls it. **Found because my own
     first assertion used the same stale column name** — the test failing taught me the product was wrong.
  3. **TASK-255's hardcoded `time` is now demonstrated rather than described**: the aggregate cannot be built at
     all over a normally-named time column (`42703`), asserted alongside the working case over a table that does
     have a literal `time` column.
- **2026-08-18 — step 7 done + committed: the convergence, and it changed my mind twice.**
  18 call sites, 8 files, 6 repos. `Birko.Data.SQL` 85f8576 · `.MSSql` 2bde57f · `.PostgreSQL` f4de483 ·
  `.SqLite` fd067a2 · `.View` fb4ebb2 · `.SQL.Tests` ec1c514.
  1. **The plan said to leave the four VALUE sites alone; reading them inverted that.** Its reasoning was
     "a value belongs in a parameter" — but three of the four *document that parameters are unavailable to
     them* (`"CREATE VIEW cannot be parameterized"`, `"fallback when parameters can't be used"`, and
     `InlineConstant` renders a constant into DDL). The shared rule is *embedding text in a single-quoted
     literal*, identical for an identifier and a constant, so two producers for it was the very thing this
     task exists to remove. They converged, and `SqlLiteral`'s doc now states both cases instead of framing
     itself as identifier-only — which would have made these sites read as misuse.
  2. **The convergence exposed a defect in what step 1 shipped.** `EscapeLiteral` returned
     `string.Empty` for null, which looked accommodating; converged onto 18 sites it would have turned a null
     identifier into an **empty** one, where the hand-written `Replace` threw — a silently malformed statement
     replacing a loud failure, § SH-H037 arriving through a helper's null handling. **It is reachable:**
     `Tables.IndexDefinition.Name` is declared `= null!`, so a null genuinely can arrive at MSSql's index
     emitters; it merely used to fail on the neighbouring `QuoteIdentifier` one line later. Now throws
     `ArgumentNullException` naming what the quiet alternative would have emitted, with its test asserting the
     message. Nothing passes null legitimately — both value sinks answer `NULL` before reaching here.
- **2026-08-18 — "run, not built" needed its own proof, and the pass count could not give it.**
  These provider suites `return` early rather than skip, so the count is **identical with and without a
  server** — exactly the shape that makes a green run meaningless. Pointed at a dead port:
  **PostgreSQL fails 34 of 67 · MySQL 40 of 68 · MSSql 23 of 57**, which is the measurement that says those
  tests reached a real server. Live totals with the convergence in: PostgreSQL 67 + View **22** (was 7
  passed / 15 skipped offline) · MySQL 68 · MSSql 57 + View 19 · SQLite 223, plus SQL 555, Views 59,
  ViewModel 18, Migrations.SQL 47, Migrations.TimescaleDB 45, TimescaleDB 42. All green, 0 warnings.
- **All seven acceptance criteria are now met.** Remaining before close: step 6's swallow measurement is
  answered (the live suite shows a bad statement **fails the migration loudly** — `ExecuteScript` has no
  `try`/`catch` and the runner adds none, so the migration half is loud where the store half was silent), and
  the `## Human test plan` is `N/A`, so this is ready for `/tasks close`.

## Human test plan

- [ ] N/A — mechanical; the proof is a row in `timescaledb_information.hypertables` and a chunk count > 1.
