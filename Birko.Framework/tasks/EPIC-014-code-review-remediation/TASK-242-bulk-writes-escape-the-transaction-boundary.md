---
id: TASK-242
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-18
depends-on: [TASK-240]
blocks: []
related: [TASK-240, TASK-243, TASK-244]
findings: []
pr: "Birko.Data.SQL e62f2bf · Birko.Data.SQL.SqLite 977506d · Birko.Data.SQL.PostgreSQL 717cfdd · Birko.Data.SQL.MySQL 735fde4 · Birko.Data.SQL.MSSql 393a716 · Birko.Data.SQL.SqLite.Tests 92f737f · Birko.Data.SQL.PostgreSQL.Tests cc3162c · Birko.Data.SQL.MySQL.Tests 7f13a62 · Birko.Data.SQL.MSSql.Tests fa0f581"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.SqLite, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.MSSql]
---

# Every bulk write escaped the transaction boundary, and on three providers it did so silently

## Context

[[TASK-240]] wired `AmbientSqlTransaction` into the **single-command** paths and left the **bulk** paths
behind. `BulkInsert` / `BulkUpdate` / `BulkDelete` and their async twins opened their own connection and
their own transaction unconditionally and never consulted the ambient. Every collection-shaped repository
write routes through them, so create-many, update-many, delete-many, delete-where and delete-all all
happened outside whatever boundary the caller had drawn.

Filed from the consumer side: Symbio (TASK-442) put all 158 of its multi-write service operations inside
`ITransactionBoundary`, which runs each one inside Birko's `SqlUnitOfWork`. **20 of those 158 then broke**,
and the cause was in the framework, not in Symbio.

**The symptom differs by provider and the quiet one is the dangerous one.**

| Provider | What the escaping bulk write did |
|---|---|
| SQLite | Second connection cannot take the write lock the boundary holds → blocks for the command timeout, then fails. Measured in Symbio: `DELETE /api/crop/plans/{id}` → 500 in 30143 ms, `SQLite Error 5: 'database is locked'`. **Loud, therefore survivable.** |
| PostgreSQL / MySQL / MSSql | Two connections are perfectly legal → the bulk write **commits independently and survives the owner's rollback**, with no error anywhere. **The boundary reads as working and is not.** |

The SQLite **async** half was fixed and verified in Symbio ahead of this task; everything else was filed
rather than guessed at, because the three server providers' shapes differ, none can be verified without a
live server, and a compile-clean but behaviourally-unverified change to another product's write path is
not a safe trade.

## What was done

Verified against **PostgreSQL 16**, **MySQL 8** and **SQL Server 2022 (16.0.4265.3)** in containers, plus
real on-disk SQLite. Nothing here is inferred from a compile.

### 1. One producer for the decision — `RunBulk` / `RunBulkAsync` (`Birko.Data.SQL`)

`AbstractAsyncConnector.RunBulkAsync` already existed from the SQLite work. Added:

- **`AbstractConnector.RunBulk`** — the sync twin. The sync half is not optional:
  `DataBaseStore.EnterTransactionScope` publishes a sync store's context into `AmbientSqlTransaction`
  exactly as the async store does, so sync single-row writes already honoured a boundary while sync bulk
  writes did not, **on the same store**. It also honours the legacy
  `ExternalConnection`/`ExternalTransaction` pair second, exactly as `DoCommand` does, so the two doors
  into "participate in somebody else's transaction" cannot disagree for bulk writes when they already
  agree for single ones. (The async twin does not, because `DoCommandAsync` does not either — the
  asymmetry is pre-existing and deliberate.)
- **`RunBulkOnConnection` / `RunBulkOnConnectionAsync`** — for a bulk write that carries its *own*
  atomicity and wants a connection but **no** transaction of its own: PostgreSQL's binary `COPY` and
  `SqlBulkCopy`. Both shapes share one private core, so a COPY-shaped path and a statement-shaped path
  cannot disagree about what participating means. Wrapping those two in an owned transaction instead would
  have been the smaller diff and a real behaviour change — they run unwrapped today and this fix is about
  the boundary, not about their standalone atomicity.
- **`retryWhenOwned`** — each provider's shipped own-connection retry policy is preserved exactly. SQLite
  retries (CR-M144); PostgreSQL, MySQL and MSSql never did and still do not. Turning the fix into a
  silent retry change on three production write paths was not on the table.

⚠ **The participating path is never wrapped in retry.** A retry would re-run statements inside a
transaction whose earlier statements already succeeded, and on most providers the first failure has already
aborted it, so the retry can only fail differently. Retrying is the boundary owner's decision — the same
reasoning `RunCommandOn` / `RunCommandOnAsync` already apply to single commands.

### 2. Twenty-one connector methods, and they are not uniform

- **SQLite** (3 sync) — the async three were already done; the sync three now match.
- **PostgreSQL** (2 + 4) — `BulkInsert`/`BulkInsertAsync` are binary `COPY … FROM STDIN (FORMAT BINARY)`
  with **no transaction at all**, so participating means running the COPY on the ambient connection; there
  is no commit to gate. The other four had connection + transaction at method level.
- **MySQL** (6) — the most uniform; all six had connection + transaction at method level.
- **MSSql** (2 + 4) — `SqlBulkCopy` enlists in an external transaction **only** through the
  `SqlBulkCopy(SqlConnection, SqlBulkCopyOptions, SqlTransaction)` overload; the third argument was `null`,
  which is precisely why it escaped.
- **TimescaleDB** — `TimescaleDBConnector : PostgreSQLConnector` declares no bulk methods of its own and
  inherits the fix. Verified, not assumed.

**`SqlBulkCopyOptions.TableLock` is kept when owning and dropped when participating.** A bulk-update (BU)
table lock taken by a standalone copy is released when that copy ends; taken inside somebody else's
boundary it is held until *their* commit, serialising every other writer against the table for the whole
life of a transaction that never asked for it. The standalone fast path is unchanged.

### 3. The store-level door had to publish the boundary too

The eight provider stores override the bulk `*Core` methods and call `Connector.Bulk*` directly, bypassing
the base's per-item write — **and the base was the only place that entered the scope.** So
`SetTransactionContext` was inert for every bulk write on every provider, which is the only door the sync
store has (`SqlUnitOfWork.FromStore` takes an `AsyncDataBaseStore`). Twenty-four one-line
`EnterTransactionScope()` calls, one per bulk `*Core` override. Costs nothing when no context is set.

Measured as load-bearing, not decoration: reverting just these lines fails **4 of 10** (SQLite) and
**3 of 11** (PostgreSQL, MySQL, MSSql each).

### 4. Two pre-existing defects the regression suites found, both blocking the proof

Neither was in scope; both had to be fixed because without them the boundary behaviour they sit in front of
is not reachable, so the tests could not distinguish a fix from a no-op.

- **PostgreSQL's COPY column list was quoted, so `BulkInsert` had never worked for a PascalCase column.**
  Sixth instance of the identifier family in § Conventions. `CreateTable` quotes the table name and emits
  column definitions **bare**, so PostgreSQL stores every base column case-folded while the table keeps its
  case; a quoted `"Name"` in the COPY column list cannot resolve. Measured against 16:
  `42703: column "Name" of relation "BulkTxRows" does not exist`, and directly:
  `CREATE TABLE "T" (Guid text, Name varchar(100))` yields `guid`, `name`. Fixed by emitting the column
  list bare, matching every other sink. The reserved-word objection does not apply — a column needing
  quotes could not have had its table created in the first place. **MySQL's identical spelling is left
  alone deliberately**: column names there are case-insensitive, so nothing is broken and changing it is
  risk for nothing.
- **MSSql's `command.Prepare()` threw on every bulk update and delete.** `SqlCommand.Prepare` requires
  every parameter to have an explicitly set type, and these are `new SqlParameter(name, DBNull.Value)`
  placeholders whose type is only implied by the value assigned per row. Measured against SQL Server 2022:
  `SqlCommand.Prepare method requires all parameters to have an explicitly set type` on the first row —
  meaning **`BulkUpdate` and `BulkDelete` have never worked on MSSql at all, in either half**. Dropping the
  call is the whole repair: SQL Server caches the plan for a repeated parameterised statement on its own.
  Npgsql and MySqlConnector infer the missing types, which is why only this provider broke.

## Proof

Four suites, 43 new tests, all counting **committed rows after a rollback** — a suite that asserted "no
exception was thrown" would have passed against the broken code on all four providers.

| Suite | Tests | Server |
|---|---|---|
| `Birko.Data.SQL.SqLite.Tests.BulkTransactionBoundaryEndToEndTests` | 10 | on-disk SQLite (no gate) |
| `Birko.Data.SQL.PostgreSQL.Tests.BulkTransactionBoundaryLiveTests` | 11 | PostgreSQL 16 |
| `Birko.Data.SQL.MySQL.Tests.BulkTransactionBoundaryLiveTests` | 11 | MySQL 8 — **first live MySQL suite in the tree** |
| `Birko.Data.SQL.MSSql.Tests.BulkTransactionBoundaryLiveTests` | 11 | SQL Server 2022 — **first live MSSql suite in the tree** |

The three live suites gate on `BIRKO_{PG,MYSQL,MSSQL}_HOST` and **say so out loud when skipped**, failing
instead when `BIRKO_REQUIRE_LIVE` is set — the TASK-214 lesson, so a CI job that is supposed to have a
database cannot report green having exercised nothing.

### Reverts — every one of them measured

| Revert | Effect | Result |
|---|---|---|
| A: whole `SqLiteConnector` bulk region (sync + async) | connector never participates | **8 of 10** fail |
| B: SQLite store `EnterTransactionScope` lines | boundary never published | **4 of 10** fail |
| C: sync `RunBulkCore`'s ambient branch only | async unaffected, isolates the sync half | **4 of 10** fail |
| P0: re-quote the PostgreSQL COPY column list | `BulkInsert` unreachable | **11 of 11** fail |
| P1: ambient branch in both cores | helper never participates | **7 of 11** on each of PG / MySQL / MSSql |
| P2: provider store `EnterTransactionScope` lines | sync door only | **3 of 11** on each of PG / MySQL / MSSql |

P1 leaves 4 passing on each server provider, and that is correct rather than a gap: the committed-boundary
and no-boundary tests pass either way by construction, which is what makes them the contract pins.

### Full green

19 SQL-touching suites, ~1,105 tests, with all three servers live: SqLite 217, PostgreSQL 48, MySQL 31,
MSSql 37, Data.SQL 514, Caching 7, Views 59, View.Migrations 14, ViewModel 18, Providers 8,
Migrations.SQL 34, Sync.Sql 7, BackgroundJobs.SQL 25, Workflow.SQL 12, EventBus.Outbox.SQL 17, and the
four `*.View.Tests` (9 / 22 / 7 / 19). Whole-solution build: 0 errors, no new warnings.

## Deliberate behaviour changes, stated rather than buried

- **MSSql loses CR-L179's in-`try` acquisition.** `Open()` / `BeginTransaction()` were inside the `try` so
  an open failure routed through `InitException`; acquiring the connection is now the shared helper's job,
  which is what gives "am I inside a boundary" a single producer. An open failure therefore propagates raw
  instead of wrapped in `Exception(commandText, ex)`. **Nothing is swallowed either way** —
  `MSSqlConnector_OnException` rethrows for anything but "Invalid object name", which an open failure is
  not — and it puts the bulk path in step with the framework's single-command path, whose
  `RunCommandTransaction` has always opened outside its `try`.
- **PostgreSQL's COPY column identifiers are now bare.** See above; this is the framework's documented
  convention, and the sink that disagreed with it could not work.
- **MSSql `Prepare()` is gone from the four bulk statement paths.** It could only throw.

## Spawned

- **[[TASK-243]]** — on MySQL, a store whose *first* operation happens inside a boundary silently commits
  that boundary: `EnsureInitialized` issues `CREATE TABLE` through the ambient connection and **MySQL
  implicitly commits an open transaction on any DDL**. Measured: without a warm-up read the bulk-create
  test reported 3 surviving rows against the *fixed* connector. Orthogonal to this task and provider-
  specific (PostgreSQL and SQL Server have transactional DDL), so the MySQL suite warms up and says why.
- **[[TASK-244]]** — the same lazy-init ordering, provider-independent: `EnsureInitialized` runs in the
  public wrapper *before* the `*Core` override publishes the boundary, so schema-ensure always runs outside
  it. Harmless on the providers with transactional DDL and the direct cause of TASK-243 on MySQL.

## What generalises

- **A rule wired into one layer is not wired.** TASK-240 taught the *connectors* to join a boundary and the
  eight provider stores' bulk overrides silently opted out of publishing one. Same shape as TASK-215's
  "wire it per backend does not mean wire it only in backends" — check the layer every implementation
  inherits, and check the layer that *feeds* it, not just the one the finding names.
- **Fix the quiet provider even when the loud one is the one that was reported.** SQLite is where the
  defect was visible and PostgreSQL/MySQL/MSSql are where it silently corrupted a boundary. Severity
  follows the failure mode, not the report.
- **A regression test that cannot reach the behaviour is not a regression test.** Two provider paths were
  dead on arrival — PostgreSQL's COPY for any PascalCase column, MSSql's `Prepare` for every bulk update
  and delete — and both were found only by running against a real server. Neither had ever been exercised;
  both had been shipping for the life of the framework.
- **Preserve each provider's own policy rather than unifying by accident.** The retry difference between
  SQLite and the three servers is real and pre-existing; a shared helper is the moment where such a
  difference gets silently flattened, so it became an explicit parameter instead.
