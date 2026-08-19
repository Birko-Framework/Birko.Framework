# Birko Framework — Changelog

Newest-first record of architectural and behavioral changes that preserve design context. New notes may land first in the short `Recent Updates` section of [CLAUDE.md](CLAUDE.md) and roll here once that section grows (via the project-local `/roll-changelog` skill). The definitive change history is `git log`; this file is a summarized narrative for architecture-level decisions that would be hard to reconstruct from commit-level diffs.

---

## 2026-08-18 — The migration emitters wrote the same statement without the escaping

TASK-253, spawned by TASK-472. That task fixed `create_hypertable` in `TimescaleDBConnector` after measuring
that **no hypertable had ever existed for a PascalCase entity**; this is the copy it deliberately left behind.
`Birko.Data.Migrations.TimescaleDB` had built the same call by raw interpolation with **no escaping at all** —
not even the single-quote doubling the connector had — and the audit found **nine** interpolating emitters, not
the three the finding named, needing **four** treatments, not the two it described. Verified against live
**TimescaleDB 2.29.2 / PostgreSQL 16**, **PostgreSQL 16**, **MySQL 8.4** and **SQL Server 2022**:
Migrations.TimescaleDB **46** tests (was 4), TimescaleDB **42** (was 39), SQL **555**, plus 10 further SQL
suites — all green, 0 nullable warnings. The standing rules are in § Conventions. Six things worth carrying:

- **A rule stated in one method and implemented in two places will be got wrong in the second.** The
  quoting/folding rule was written down correctly and only in `BuildCreateHypertableSql`; a different repo
  re-derived the statement and got the escaping, the quoting and the folding all wrong. It now has one producer
  each on `AbstractConnectorBase`, plus `SqlLiteral.EscapeLiteral` for the `''` doubling that was hand-written
  **21 times**.
- **`BuildCompressionPolicySql` needs the same table BOTH ways in one statement** — `ALTER TABLE "T"` takes a
  real identifier, `add_compression_policy('"T"')` takes a regclass in a literal. TASK-472's "two identifiers,
  opposite treatments" arriving inside a single method, and reasoning from either position alone leaves the
  statement broken at the other.
- **A containment classification, not a quoting one, is what separated the fixable from the unfixable.**
  Everything inside a `'…'` literal is completely contained by escaping; exactly two arguments
  (`selectClause`, `groupByClause`) are raw SQL in statement position and cannot be contained at all. That is a
  property of the parameters, so the answer is an API change ([[TASK-260]]) rather than a validator — and
  validating them was measured and rejected, since `date_trunc('day', x)` is a legitimate `GROUP BY`.
- **The plan was wrong three times and the doing corrected it.** Its commit order was unbuildable (tests before
  the production signature change); its instruction to leave the four *value* escaping sites alone inverted
  once they were read (three document that parameters are unavailable to them); and its claim that a lowercase
  fixture is "a discrimination control that must survive every revert" was too strong — measured, it survives
  the folding revert (5 of 34) and dies with the quoting revert (15 of 34).
- **A containment test that demands the payload's characters vanish is testing deletion.** The first injection
  helper asserted `NotContain(";")` / `NotContain("--")` and failed **16 of 16 against correct code** — a
  contained `--` is still a `--`, sitting inertly inside a literal. Assert the payload appears in exactly the
  escaped form its position prescribes, plus a structural invariant (after collapsing doubled quotes, the
  remaining delimiters must be even).
- **"Run the live suites, don't just build them" needs its own proof, because the pass count cannot give it.**
  These suites `return` early rather than skip, so the total is identical with and without a server. Pointed at
  a dead port: PostgreSQL fails **34 of 67**, MySQL **40 of 68**, MSSql **23 of 57** — that is the measurement
  which says those tests reached a server. Spawned **TASK-255** (the aggregate's hardcoded `time` column,
  CR-H070 unfixed in the method next door — now demonstrated live), **TASK-259** (P1: `SqlSchemaBuilder` never
  clears `SetExternalTransaction`, publishing a migration's connection onto a process-wide cached connector —
  found by evaluating a *rejected* option), **TASK-260** and **TASK-261** (`GetChunkInterval` reads a catalogue
  column TimescaleDB moved in 2.0, so `42703` on every 2.x server — found because my own first assertion used
  the same stale name).

---

## 2026-08-18 — No hypertable was ever created, and TimescaleDB never published the boundary

Consumer Symbio TASK-472 asked for the bulk-write boundary to be verified on TimescaleDB "separately, over a real
hypertable", because `TimescaleDBConnector : PostgreSQLConnector` overrides no bulk method and therefore
*inherits* TASK-242's fix — and *"it inherits, so it is covered"* was named in the task as the claim most worth
disproving. It disproved, twice, and both defects were silent. Verified against live **TimescaleDB 2 /
PostgreSQL 16**: **39 tests green in `Birko.Data.TimescaleDB.Tests`, up from 19**; 20 new. The standing rules are
in § Conventions. Five things worth carrying:

- **Establishing the premise is what found the bigger defect.** `create_hypertable` emitted its table **bare**
  inside a string literal, so the regclass folded to `tsschemarows` against the `"TsSchemaRows"` that
  `CreateTable` had created → `42P01` → which `IsMissingTableException` classifies as a missing table, so
  `OnException` ran `DoInit()` and **returned**. `CreateTable` reported success and **no hypertable existed** —
  for every PascalCase entity, which is all of them. Chunk routing, compression and retention silently absent
  while a plain PostgreSQL table served reads and writes and made the store look correct. **A suite written
  without checking the premise would have "verified the boundary over a hypertable" against a plain table.**
- **The two identifiers in one call needed opposite treatments**, which is the generalisable half: the table is
  a `regclass` and must carry its **own quotes**; the time column is a `name` compared literally against
  `pg_attribute.attname` and must be **pre-folded**, because column definitions are emitted bare (TASK-209).
  Neither travels as an identifier, so the parser's folding never runs — see § Conventions. The column half was
  hidden by the shipped default `TimeColumn = "timestamp"` matching a folded `Timestamp` property **by luck**.
- **TASK-242's own lesson had a ninth store nobody wired.** It taught that joining a boundary is only half of
  it — something must **publish** it — and put `EnterTransactionScope()` into the eight provider stores' bulk
  `*Core` overrides. TimescaleDB got none, so the inherited connector fix was **unreachable from this store**
  and `SetTransactionContext` was inert for every bulk write, which is the *only* door a sync store has.
  Fifth instance of "a funnel with four overrides is not a funnel". **And the broken door was the quiet one:**
  `SqlUnitOfWork` publishes the ambient itself, so it worked all along — a test written against the unit of
  work alone reports success either way.
- **Two traps that both read as product defects.** `timescaledb_information.hypertables` holds the name with its
  **case intact**, so a lowercased lookup returned 0 and briefly made a working fix look broken (*check whether
  a reproduction failed for the reason you think*). And `ModelMapRegistry` applies into process-wide state and
  **accumulates** — one model type mapped two ways merged both mappings into `42P16 multiple primary keys`.
- **A test that pinned the defect did so through its fixture's choice of name.**
  `BuildCreateHypertableSql_ComposesQuotedArgsAndInterval` asserted `create_hypertable('metrics', …)`, and for an
  already-lowercase name the missing quotes make no difference at all. Same family as TASK-246's fallback branch:
  **a fixture that cannot distinguish the fix from the defect is not coverage.** Reverts: bare table **5 of 5**,
  dropped fold **4 of 5** (survivor = the lowercase case, the correct discrimination), removed publications
  **4 of 13** (exactly the per-store-door tests). Spawned **TASK-253** (the migrations project duplicates the
  broken statement in three emitters with *no* escaping, and `CreateHypertableAsync` bypasses the DDL funnel) and
  **TASK-254** (the fix turns a silent no-op into a throw out of lazy schema-ensure, against TASK-204 — shipped
  only because the blast radius measured **0** entities today).

---

## 2026-08-18 — MySQL could not index an unbounded string, and refusing the declaration was the wrong fix

TASK-248, the last of TASK-245's three spawns. MySQL maps a plain `string` to `LONGTEXT` and cannot index a
BLOB/TEXT column without a key length (**1170**), so after the statement-syntax fix an index over an unbounded
string still could not be built there. The obvious remedy — refuse the declaration at table load, per
§ SH-H037 — was **measured and rejected**: 7 live consumer entities declare exactly that shape and work
correctly on PostgreSQL today, while 0 framework domain models declare it at all. Fixed instead by bounding the
column on MySQL alone (`VARCHAR(255)` when `AbstractField.IsIndexed`). 1,170 tests green across 19 suites
including the five `Birko.Models.*.SQL` domain suites; 6 new. The standing rule is in § Conventions. Four
things worth carrying:

- **The measurement inverted the decision.** "This declaration is unhonourable, so refuse it" was right in
  the abstract and wrong here: it is unhonourable on one of four providers, so refusing framework-wide would
  have turned seven working entities into start-up failures. § SH-H037's fail-fast still requires the blast
  radius to be cleared first, and this is the case where clearing it says no.
- **A revert that fails nothing is a missing test.** `DataBase.LoadIndexes` resolves index columns at two
  points, one per attribute form; dropping the per-property marking failed **0 of 67** because every model in
  the new suite used `[CompositeIndex]`. Adding an `[IndexedField]` model made it fail exactly 1. Second
  instance of this shape in three days after TASK-245's async-site revert.
- **Degrade what the caller can see.** A prefix index would have made all seven UNIQUE constraints weaker
  than declared — silently refusing genuinely different values sharing a prefix. A bounded column refuses the
  over-long write instead, loudly.
- **The blast-radius survey was wrong twice before it was right.** The consumer entities declare their
  attributes fully qualified, so an unqualified `[CompositeIndex(` grep found none of them, and a hand-rolled
  property parser then mis-classified them as bounded. Both corrections came from checking one known instance
  by hand. Verify such a count against a known case before trusting it — a survey that under-reports reads
  exactly like a clean bill of health.

---

## 2026-08-18 — The migration schema builder's connector-free fallbacks were deleted, not fixed

TASK-247, closing the index-DDL family TASK-245 opened. `SqlSchemaBuilder` took an **optional** connector and
carried a hand-written raw-SQL fallback in all eight methods for the null case; two had drifted into emitting
DDL that MySQL and PostgreSQL reject (`CREATE INDEX IF NOT EXISTS "Col"`, and a `DROP INDEX IF EXISTS … ON …`
that is wrong on both in opposite directions). So the connector-free path was never portability — only the
appearance of it. Deleted, connector required, 48 lines lighter. Build clean; **1,199 tests green across 17
suites**; the revert fails 2 of 47. Four things worth carrying:

- **The fallback's real damage was to the tests.** `connector == null` was how **every** test in that project
  built the schema builder, so six of them exercised only the dead branch — exactly how TASK-246's missing
  `Unique` flag on the live branch stayed green. Requiring the connector turned those six into real tests.
- **Unreachability was measured, not assumed** — and the same sweep corrected a claim I had already committed.
  All 16 consumer repos: 0 hand-built contexts, 0 uses of `ISchemaBuilder`, 0 migration-declared indexes. That
  last one means **TASK-246 was latent, not firing**; its entry below and its commit message said "silently
  accepting duplicates", which overstated the live impact. Corrected in place rather than left to read as data
  corruption.
- **The test that pinned the fallback became the test that pins its refusal.** TASK-246 had added one
  asserting the fallback honoured `_unique` correctly; rather than delete it, it now asserts the null connector
  is refused *and* that the message names both why the fallback was not a usable alternative and where to get a
  connector.
- **Two capabilities genuinely disappear with it**, written down so a later reader can tell a deliberate drop
  from an oversight: composite `PRIMARY KEY (a, b)` (the connector renders per-column primary keys only), and
  `RenameField` keeps its hand-written SQL because there is no connector equivalent — with `RENAME COLUMN` not
  being universal, a latent gap of the same family.

---

## 2026-08-18 — A migration's `.Unique()` built a non-unique index on every provider

TASK-246, spawned by TASK-245's planning grill. `SqlIndexBuilder.Build()`'s connector path — the one every
production migration takes — constructed its `Tables.IndexDefinition` without copying `_unique`, so
`.Unique()` emitted a plain `CREATE INDEX` on SQLite, PostgreSQL, MySQL and MSSql alike. A missing
**constraint**, not a missing optimisation — any duplicate the migration was written to forbid would be
accepted. **Latent, not firing** (corrected 2026-08-18 by a sweep of all 16 consumer repos): no consumer
declares an index through a migration, and Symbio's unique docnumber indexes come from `[CompositeIndex]`
attributes via schema-ensure, which was never affected. The fix still matters — it is public surface, and
Symbio's own `UniqueIndexDataCheck` exists because they expect to add migrations — but no data was corrupted. One-line fix; the work was the test surface. 7 new tests, 46 green in
`Birko.Data.Migrations.SQL.Tests` with live PostgreSQL 16; the revert fails **3 of 46**, including the live
one. Three things worth carrying:

- **A fallback branch can make a green suite meaningless.** The raw-SQL fallback three lines below the defect
  *did* honour `_unique`, and it is taken exactly when `connector == null` — which is how **every**
  pre-existing test in that project built the schema builder. The feature was demonstrably working in the
  branch nobody uses and broken in the branch everybody uses. Check which branch your fixture selects.
- **Second instance of the lost-flag shape in two days**, after `SqlIndexManager.ToSqlIndexDefinition`
  dropped the identical property one layer over (TASK-245). Both were invisible for the same reason: the
  omission is in an object initialiser, where a missing line looks like nothing at all.
- **The enforcement assertion is the test; the DDL text is the companion.** Asserting `UNIQUE` appears in
  `sqlite_master` would also pass for an index the engine never applied, so every test here inserts the
  duplicate and requires the write to fail — and the without-`.Unique()` case is pinned too, so the fix
  cannot be "make everything unique".

---

## 2026-08-18 — No declared index was ever created on MySQL or PostgreSQL

TASK-245, spawned by TASK-243. Two independent defects with one user-visible symptom — a declared
`[IndexedField]` / `[CompositeIndex]` producing **no index, and for a UNIQUE one no constraint** — on the two
providers most likely to be in production, silent since TASK-204 made schema-ensure record rather than throw.
MySQL rejected `CREATE INDEX IF NOT EXISTS` outright (`ERROR 1064`); PostgreSQL 16 could not resolve the
quoted index columns against the folded ones its bare-column `CREATE TABLE` actually creates (`42703`) —
seventh instance of the identifier family. `DROP INDEX` was wrong on MySQL twice over (`IF EXISTS` rejected,
mandatory `ON` clause missing). The standing rule is in § Conventions above. Verified against live **MySQL
8.4**, **PostgreSQL 16** and **SQL Server 2022** plus on-disk SQLite: **1,086 tests green across 14 suites**,
61 of them new. Six things worth carrying:

- **The gate found it, and the boundary of it, before the fix existed.** Written test-first, the new MySQL
  suite failed **12 of 14** against the unfixed tree; the 2 that passed were "still throws" pins that passed
  for the *wrong* reason (1064 rather than 1062/1091), so both were strengthened to assert the error code and
  now discriminate. Choosing the test model is also what exposed a **third** defect: an index over an
  unbounded `string` (→ `LONGTEXT`) fails with `1170` on MySQL — closed the same day by TASK-248, which
  bounds an *indexed* string to `VARCHAR(255)` on that provider only. That shape is exactly what the canonical
  SQLite example declares.
- **Revert splits, each isolating one claim:** MySQL statement **13 of 14** · base column unquote **6 of 6**
  (PostgreSQL) · tolerance filters **3 of 14** · predicate narrowing **4 of 14** · `DropIndexSql` **2 of 14**
  · `Unique` hand-off **1 of 6** · DDL funnel **1 of 14** — but only at the **sync** site.
- **The async path you patch may not be the one anything calls.** `AsyncDataBaseStore.InitCoreAsync` invokes
  the **sync** `Connector.CreateTable` inside a `Task.Run`, so `CreateIndexesAsync` has no store-level caller
  and reverting it alone fails **0 of 14**. Fourth instance of TASK-243's "a funnel with four overrides is
  not a funnel", and it was found by a revert measuring zero rather than by reading.
- **Three duplicate emitters existed because one property was dropped upstream.**
  `SqlIndexManager.ToSqlIndexDefinition` never copied `Unique`, which is the *only* reason
  `CreateUniqueIndexSql` existed on the base plus the PostgreSQL and MSSql managers — and the PostgreSQL copy
  carried the quoted-column defect independently, so `IIndexManager.CreateAsync` could never build a unique
  index there either. Copying one field collapsed four emitters into one producer. The filed plan had proposed
  adding a **fourth** override.
- **An opt-out only one provider can honour is a silent no-op.** `CreateIndexes(..., throwIfExists: true)`
  would have been meaningful on MySQL alone, so `CreateIndexSql` gained `conditional` too — base drops
  `IF NOT EXISTS`, MSSql drops its `sys.indexes` guard, MySQL stops tolerating 1061. Pinned per provider.
- **The close-gate review returned after the commits landed, and three of its four findings were defects in
  them** — filed and fixed as **TASK-249**, +14 tests, **1,100 green** with all four providers live. The
  serious one: this fix's own rule ("enumerate that sink's callers by provenance") applied to one of **two**
  caller-derived sinks, so `SqlIndexBuilder.WithField` in `Birko.Data.Migrations.SQL` still let a migration
  append a statement through a column name. Also: the new guard accepted a `Table.` qualifier *and its test
  pinned that as correct*; `IIndexManager` was left more divergent on MySQL than it started (it bypasses the
  funnel by design, so it needed its own tolerance on **both** verbs); and a comment asserted the very
  invariant the same commit reversed. A late review is still a review.
- **"Nothing in the tree called `CreateIndexes` directly"** — the TASK-204 contract it supposedly preserved
  was documented and entirely unasserted. Narrowing it to what it *meant* (unbuildable = 1062 still throws;
  "already present" = 1061 no longer does, matching the other three) is now pinned by tests in both
  directions. Also spawned **TASK-246**: a migration's `.Unique()` silently builds a NON-unique index on all
  four providers, because `SqlIndexBuilder.Build()` loses the flag the same way — and **TASK-247**, the same
  builder's raw-SQL fallbacks carrying a third and fourth copy of the broken clause.

---

## 2026-08-18 — A store's first operation inside a boundary silently committed it on MySQL

TASK-243, spawned by TASK-242's own regression suite. Stores initialise lazily, so the first data access
issues `CREATE TABLE IF NOT EXISTS` — and after TASK-240 that DDL ran on the ambient boundary's connection.
**MySQL implicitly commits an open transaction on any DDL**, so the boundary was committed before the
caller's own write ran and the rollback undid nothing: 3 rows survived, no error either way. Fixed with
`SupportsTransactionalDdl` (false for MySQL alone) consulted by one DDL funnel; the standing rule is in
§ Conventions above. 24 new tests across four suites; reverts **7 of 38** (MySQL rejoins), **3 of 3**
(suppress unconditionally → SQLite deadlock), **7 of 38** (provider override bypasses the funnel); 19 SQL
suites / 1,129 tests green. Five things worth carrying:

- **The obvious provider-independent fix is a hang.** "Run schema-ensure outside any boundary" was this
  task's own filed first option; measured, it fails 3 of 3 on SQLite with `database is locked`. The two
  halves of the trade land on opposite providers — SQLite *needs* DDL on the boundary's connection, MySQL
  needs it off — which is exactly why the answer is a stated capability and not a rule.
- **A funnel with four overrides is not a funnel.** The base emitters were rewired and the fix measured as
  not working: all four server connectors override `CreateTable(string, IEnumerable<string>)` with their
  own `DoCommand`. Third instance in a fortnight after TASK-215 and TASK-242. Grep `override` before
  believing a funnel is wired, and prove it with a revert.
- **Measure the objection before mitigating it.** Issuing DDL on a second connection looked like a
  metadata-lock hazard worth a much larger fix. One `docker exec` settled it: on MySQL 8.4 an open
  transaction holding a row lock does not block a concurrent `CREATE TABLE IF NOT EXISTS` on that table
  (17 ms).
- **A warm-up in a test is a claim that needs an owner.** TASK-242 added `WarmUpAsync` to three MySQL
  tests with a comment naming this task; closing it removed the warm-up and all 38 pass cold. A warm-up
  whose reason is not written down is indistinguishable from a bug being hidden.
- **Two providers now answer oppositely and both are pinned.** A table created by schema-ensure inside a
  boundary survives the rollback on MySQL and dies with it everywhere else. Asserting both is the record
  of why they are allowed to differ. Also spawned **TASK-245**: MySQL cannot create *any* declared index —
  the base emits `CREATE INDEX IF NOT EXISTS`, a syntax error there, and MySQL is the one provider that
  neither overrides nor supports it. Silent since TASK-204 made schema-ensure record rather than throw.

---

## 2026-08-18 — Bulk writes escaped every transaction boundary — silently on three providers

TASK-242, completing [TASK-240] which wired `AmbientSqlTransaction` into the single-command paths and left
the bulk ones behind. `BulkInsert` / `BulkUpdate` / `BulkDelete` + async twins opened their own connection
and their own transaction unconditionally, and every collection-shaped repository write routes through them
— so create-many, update-many, delete-many, delete-where and delete-all all happened *outside* the caller's
boundary. Measured in consumer Symbio (TASK-442): **20 of 158** boundary-wrapped service operations broke.
Verified here against live **PostgreSQL 16**, **MySQL 8** and **SQL Server 2022**, plus on-disk SQLite; the
standing rule is in § Conventions above. 21 connector methods + 24 store-level scope publications; 43 new
tests across four suites; 19 SQL suites / ~1,105 tests green. Five things worth carrying:

- **The loud provider is not the dangerous one.** On SQLite the escaping write cannot take the boundary's
  write lock, so it blocks for the command timeout and fails — survivable. On PostgreSQL / MySQL / MSSql two
  connections are perfectly legal, so it **committed and survived the owner's rollback with no error
  anywhere**: on the three providers most likely to be in production, the boundary read as working and was
  not. Every assertion counts committed rows after a rollback, because "no exception was thrown" passes
  against the broken code on all four.
- **A rule wired into one layer is not wired.** TASK-240 taught the connectors; the eight provider stores
  override the bulk `*Core` methods and call `Connector.Bulk*` directly, and the base was the only place
  that entered the scope — so `SetTransactionContext` was inert for every bulk write on every provider.
  Reverting just those 24 lines fails 4 of 10 (SQLite) and 3 of 11 (each server). Same shape as TASK-215's
  "wire it per backend does not mean wire it only in backends".
- **Two provider paths were dead on arrival, and only a live server said so.** PostgreSQL's binary COPY
  quoted its column list, so `BulkInsert` had **never** worked for a PascalCase column (sixth instance of
  the identifier family: bare DDL columns case-fold, a quoted `"Name"` cannot resolve). MSSql's
  `command.Prepare()` throws on placeholder parameters with no explicit type, so **`BulkUpdate` and
  `BulkDelete` have never worked on MSSql at all**. Both had to be fixed inline — a regression test that
  cannot reach the behaviour cannot distinguish a fix from a no-op.
- **A shared helper is where per-provider policy gets flattened by accident.** SQLite's bulk path retries
  (CR-M144) and the three servers' never did. `retryWhenOwned` makes that an explicit parameter rather than
  a silent change to three production write paths. Likewise `RunBulkOnConnection`, so COPY and
  `SqlBulkCopy` keep running without a transaction of their own on the owned path — the boundary is the fix,
  not their standalone atomicity.
- **Proving it found a third defect that is nobody's fault here.** On MySQL a store whose *first* operation
  happens inside a boundary silently commits it: lazy `CREATE TABLE` goes through the ambient connection and
  **MySQL implicitly commits on any DDL** (TASK-243, with TASK-244 for the ordering underneath it). The
  MySQL suite warms up and names the reason; PostgreSQL and SQL Server have transactional DDL and are
  unaffected.

---

## 2026-08-17 — Every worker enqueued its own copy of every recurring job

TASK-237. `RecurringJobScheduler` kept `NextRunAt` in process memory, so N workers each concluded
independently that a job was due — N copies, on every backend **including the two that have a lock**,
because nothing consumed `IJobLockProvider`: it appeared in exactly three files, its declaration and its two
implementations. Now wired as leader election, opt-in: pass a provider and only its holder schedules; pass
nothing and behaviour is bit-for-bit unchanged, which is what lets the two shipped consumer call sites stay
untouched. Reverts: un-wiring **5 of 86** offline + **3 of 20** Redis + **3 of 25** PostgreSQL; removing only
the re-baseline **1 of 86**. 166 tests green across 9 job suites. Four things worth carrying:

- **The filed acceptance criterion had its own mechanism inverted, and implementing it literally would have
  shipped the bug it warned about.** It paired "skips enqueueing but still advances `NextRunAt`" with "fires
  immediately on becoming leader" — opposite halves. Advancing keeps a follower *in phase*; not advancing is
  what leaves it overdue. The answer was neither: a follower makes **no scheduling decision at all**, and the
  new leader **re-baselines** (`NextRunAt = now + interval`), because it cannot know what the previous leader
  enqueued. Re-derive a criterion's mechanism before implementing to it.
- **"Has this occurrence already been enqueued?" is an idempotency question, not a mutual-exclusion one.**
  Locking each individual decision cannot work — every process releases right after enqueueing, so one whose
  clock lags arrives later, finds the lock free and duplicates. Closing that means holding until the *next*
  due instant, which is a persistent record, not a lock. The right long-term answer is a unique key on the
  queue (job name + due instant), which all eight backends could enforce rather than the two that can express
  a lock. Recorded, not built: it is a queue-contract change.
- **A test that could not fail was caught by reading it, not by running it.** The cancellation test cancelled
  the loop from outside, which its own `Task.Delay` observes essentially every time — so it passed with and
  without the fix. Only a provider that cancels *during acquire* reaches the path. Same family as this
  epic's recurring finding, arriving in a test this time rather than a checker.
- **A guard's catch has to be narrowed as well as added.** Swallowing every `OperationCanceledException`
  would end the loop on a cancellation belonging to someone else's timeout — permanently stopping scheduling
  over a transient. And the *release* passes `CancellationToken.None` deliberately: the loop exits precisely
  because its token was cancelled, and both providers' `ReleaseAsync` open with `ThrowIfCancellationRequested`,
  so forwarding it would skip the release on the only path that ever runs.

---

## 2026-08-16 — CosmosDB rendered `.Date` as a JSON sub-property and matched nothing

TASK-223 made `CosmosFilterMatrixLiveTests` runnable — it was gated *and* unreachable, because the
framework could not select Gateway mode and the emulator serves nothing else. Its first run ever reported
26 of 27. TASK-224 closed the 27th: `x.When.Date == d` emitted
`WHERE (root["CreatedAt"]["Date"] = "…")`, addressing a member of a *string* (Cosmos stores a DateTime as
ISO text), so the query ran and returned **zero rows with no error**. Now 27/27. Split: unwiring **1 of
54**, gutting the rewriter **3 of 86**; 1,168 tests green across 8 suites. Four things worth carrying:

- **The whole thread was one dark suite deep.** TASK-214 → 218 → 220 → 221 → 222 → 223 → 224, and every
  single defect was found by running a suite that had never run. The last two needed a Docker emulator
  that took a minute to start. **"Needs a live service" is a claim to test, not a reason to skip** —
  Cosmos renders SQL offline, Raven builds RQL offline, and both emulators run in one `docker run`.
- **A per-backend rewrite family now has three members**, all in `Birko.Data.Core/Expressions/` and all
  wired only where measured: `SpanContains` (Mongo, Cosmos), `RavenFilterRewriter` (Raven),
  `DateTruncation` (Cosmos). The shape is settled; the discipline is that the helper is available to
  everyone and the wiring follows a measurement.
- **Handle the whole operator family or none of it.** `.Date` needed all six comparisons plus operand
  mirroring — `d < x.When.Date` inverts silently if only `==` is rewritten, which is the same defect in
  a different coat. Same rule as TASK-215's "guard the whole verb family".
- **Two implementations of one semantics is recorded debt, not an oversight.** The SQL connector has
  had this exact rewrite since Symbio TASK-355 but emits `Condition` objects, so it could not be shared
  as-is. Both operator tables now name the other, and the consolidation (run the pre-pass before the SQL
  parser, delete the method) is written down rather than done as a side effect of a Cosmos fix.

---

## 2026-08-16 — RavenDB dropped a boolean ternary's WHERE clause entirely

TASK-222, the last of the five TASK-214 spawned. RavenDB does not *reject* a boolean ternary — it emits
**no `where` clause at all** and returns every document, or emits malformed RQL like
`where Active = $p0 and`. `ExpressionNormalizer` already existed to desugar exactly that and its own doc
comment excluded the native-LINQ backends; running it for Raven fixes the whole silent class. Split:
Revert A **1 of 51**, Revert B **2 of 70**; 1,147 tests green across 8 suites. Four things worth carrying:

- **The filed hypothesis was wrong in the useful direction.** The task guessed the normalizer would close
  4 of 6 shapes; it closes **1** — the normalizer keeps non-boolean coalesce and arithmetic intact *by
  design*. But probing the silent class found **three more unfiled shapes**, all silent or malformed. The
  count went down and the coverage went up: **count the mechanism, not the symptoms.**
- **Silent beats loud, and they need opposite treatments.** One dropped predicate outranked five loud
  refusals, and the loud five were then *accepted* — computed operands need a Raven static index, not a
  tree rewrite. Fix what lies; document what refuses.
- **A ledger must fail in both directions.** The matrix's accepted-divergence list fails the run on an
  unlisted divergence **and** on a listed one that starts passing. Without the second half an entry
  silently becomes a blanket and masks the next regression in that shape.
- **A unit test proving a transform is not a test that anyone benefits.** Reverting the boolean-constant
  reduction failed 2 of 70 in Core while RavenDB's live suite stayed green at 51/51 — its shape list had
  no literal-branch ternary. Two shapes were added there. Same lesson as TASK-221's dead wiring, from the
  other end: there, the helper was tested and uncalled; here, the transform was tested and unexercised.

---

## 2026-08-16 — RavenDB could not express `IN`, and its matrix suite was broken in its own setup

TASK-220's audit excluded RavenDB from the span rewrite because *every* `Contains` spelling fails there,
not just the array one — a different defect, filed as TASK-221 and fixed here. `IN` is the canonical
batch-load pattern, so the portable spelling that works on SQL, ElasticSearch, MongoDB and CosmosDB threw
on RavenDB alone. `RavenSetMembership` now rewrites it to Raven's own `.In()`. Split: **6 of 51**;
6 suites green. Four things worth carrying:

- **The suite that would have caught it was gated AND broken in setup.** `RavenFilterMatrixLiveTests`
  built its oracle with `ReadAsync(x => true)`, which RavenDB refuses outright — so even with
  `BIRKO_RAVEN_URL` set it threw before reporting a single shape. Worse than TASK-214's plain gating:
  there the suite would at least have run. Its first run ever, after both fixes, reports **21 of 27**;
  the other 6 are TASK-222, one of them a **silent wrong answer** (`ternary` returns 6 rows where C#
  says 1).
- **The live run caught dead wiring that 15 offline tests walked straight past.** The bulk
  `ReadCoreAsync` rewrite had been inserted *inside* `if (_documentStore == null) { return …; }` —
  unreachable. Every non-gated test passed because they call the rewriter directly. **Offline tests pin
  a helper; only an end-to-end run pins that anything calls it.** The Cosmos and MongoDB wirings were
  then checked for the same slip — both fine, verified rather than assumed.
- **The dangerous half of a rewrite is what it must NOT touch.** `x.Tags.Contains("red")` is membership
  in the opposite direction and already worked; a blanket `Contains` → `In` would have broken it. The
  discrimination — which operand references the lambda parameter — was already written in
  `ElasticSearch.ParseContains`, so it was reused rather than re-derived.
- **`ls *.cs` is not a survey of a test project.** I filed the task asserting Raven had no matrix suite
  at all and called that the larger finding. It had one, in a subdirectory. Corrected in the task rather
  than quietly dropped, because the claim had also been reported verbally and an acceptance criterion was
  written against it.

---

## 2026-08-16 — An array-backed `IN` filter could not be translated on MongoDB

TASK-218, the last of the three tasks TASK-214 made visible. `MongoFilterMatrixLiveTests` had never run
until TASK-214 fixed serialization; on its first run it reported 26 of 27 shapes correct, and the 27th was
real. `x => arr.Contains(x.Amount)` over an `int[]` throws `NotSupportedException: Specified method is not
supported` on the driver, while `List<int>` renders `$in` — a look-alike one keystroke away and no warning
either way. The standing rule is in § Conventions above. Split: unwiring the rewrite **2 of 85**, gutting
it **2 of 85 + 2 of 59**; 5 suites green (85 + 59 + 500 + 129 + 69). Four things worth carrying:

- **The measurement was the deliverable, and it both shrank and widened the fix.** The task's first
  acceptance row demanded SQL and ElasticSearch be measured. Both were already correct, so a normalisation
  pass over every translator became one helper. The follow-up audit (TASK-220) then found CosmosDB
  *equally* broken — TASK-218 had waved it off as needing a live service, and it renders SQL offline.
- **A test that calls the helper is not a test that the wiring exists.** TASK-220's first three Cosmos
  tests invoked `SpanContains.Rewrite` in their own render helper; unwiring all six store entry points
  left the suite green at 47/47. Only running the revert exposed it. The fix discriminates on failure
  *phase* — unwired throws at translation before any I/O, wired reaches the network — which needs no
  server.
- **Three symptoms, one cause, found from three directions over three months.** The same overload change
  had already produced an unguarded destructive filter (`PredicateScope`) and a silently-empty SQL result
  (Symbio TASK-249/254). Only writing them down together made it obvious they were one thing — and that
  the unwrap should have one producer.
- **Nine entry points, not thirty hand-offs.** The stores pass a filter to the driver from ~30 call sites
  but the caller's expression *arrives* at nine methods. Normalising on arrival is what makes the guard,
  the whole-collection check and the driver agree, and what makes the next hand-off site correct for free.
- **A test pins the premise.** `arr.Contains(x)` binding `MemoryExtensions` is a runtime fact, not a law;
  if it reverts, the rewrite becomes dead code and that test is the only thing that would notice.

---

## 2026-08-16 — Two answers for what MongoDB's `_id` is — so every view was silently wrong

TASK-219, spawned by TASK-214's close-gate review and picked immediately after it. Yesterday's fix left
`_id` to the driver as an auto-generated ObjectId with the canonical `Guid` beside it;
`Birko.Data.MongoDB.Views`'s translator had always rewritten the `Guid` property to `_id`. Each layer was
self-consistent; together they made **every Mongo view wrong** — measured on MongoDB 7, projecting the
canonical id **threw** and filtering on it returned **0 rows for a document that exists**. Settled by
making the canonical `Guid` **be** `_id`. The standing rule is in § Conventions above. Split: Revert A
(back to ObjectId `_id`) **6 of 84** + **1 of 12**, Revert B (drop the view class map) **1 of 12**;
ungated 84/84 + 12/12, 7 suites green. Four things worth carrying:

- **The cheaper edit was the wrong one.** Patching the translator would have satisfied the filed finding
  and left two ids per document — plus the framework-wide silent-drop reader that tolerating the second
  one required. Resolve the contradiction, don't patch the louder side.
- **The migration objection was measured away, and it was expiring.** Changing an id layout is normally
  expensive; TASK-214 had just proved no write had ever succeeded, so there was nothing stored to
  migrate. That is only true until the fixed stores are used — which is why this was worth doing
  *immediately after* TASK-214 rather than scheduling it.
- **Fixing the entity half did not fix the view half, and the probe said so.** After `SetIdMember` the
  projection worked but the filter still returned 0: `MongoViewStore` renders `$match` through the *view
  type's* class map, where a `Guid?` property still used the global binary serializer and compared
  BinData to a string. A second registration (`MongoViewSerialization`) was needed. **Re-run the whole
  probe after the fix — the first symptom clearing is not the finding clearing.**
- **A projection type is not an entity.** The same registration also clears the id member and pins
  element names, because the driver's `NamedIdMemberConvention` binds a view property called `Id` to
  `_id` — which the projection explicitly suppresses. Found by accident: my first probe named its view
  property `Id` and threw for that reason, which looked like the defect and was not. **Check whether a
  reproduction failed for the reason you think.**

---

## 2026-08-16 — Nothing could be saved to MongoDB — the store was never able to serialize an entity

TASK-214, verified against a live **MongoDB 7** rather than the offline registry the finding was filed
from. The finding held and was **wider than filed**: not "the sync store cannot serialize", but
**neither store could persist a single entity**, and repositories inherited it through the same
constraints. `Birko.Data.MongoDB` registered no driver serialization at all, and `MongoDBModel`'s
attempt to compensate — a `[BsonRepresentation(BsonType.String)]` **override** of `AbstractModel.Guid`
— is what made the class map unfreezable. Fixed by deleting the override and adding one
`MongoSerialization.EnsureRegistered()`, called from the `MongoDBClient` constructor. The standing rule
is in § Conventions above. Split: **Revert A 7 of 78** (6 fix-dependent), **Revert B 6 of 78**
(5 fix-dependent); ungated 78/78, 6 dependent suites 39/39. Four things worth carrying:

- **The live server found a third failure the offline probe structurally could not.** With the writes
  finally landing, every *read* threw `FormatException: Element '_id' does not match any field or
  property` — no Birko model declares `_id`, by design, so the driver's auto-generated ObjectId had
  nowhere to go. **Failures queue** (§ TASK-209's rule), and the ones behind the filed one only appear
  once you clear it.
- **The env-gated suite had never run, and running it was most of the value.** `MongoFilterMatrixLiveTests`
  no-ops without `BIRKO_MONGO_HOST`, so the entire MongoDB surface was green while unable to write.
  Starting a container took a minute. The new serialization suite is deliberately **non-gated** —
  class-mapping and BSON round-trip need no server, which is precisely why gating them was indefensible.
- **Registration goes at a funnel and lets the consumer win.** `MongoDBClient`'s constructor, not a
  `[ModuleInitializer]`: shared projects compile into the consumer's assembly, so an initializer would
  run *first* and the framework would always beat the consumer's own configuration. Stricter coverage,
  wrong precedence.
- **`.map.yml` under-coverage, fifth instance — and this time it bit before the fix, not after.** None
  of the four changed files was reachable by any glob, so the harvest never specced the defect *and*
  this fix's own regen would have produced a clean diff over unread code. Added to
  `core-model-contracts`; `ChangeStreams/*.cs` + `MongoDBLogModel.cs` remain unmapped (TASK-208).
  Also spawned **TASK-218**: with writes working, the matrix suite reported 26/27, and the 27th is real
  — an array's `.Contains` binds to `MemoryExtensions.Contains` on .NET 9+ and the driver rejects it.

---

## 2026-08-16 — The unbounded-filter guard was never wired into the base it lives on

TASK-215 set out to wire `RequireBoundedFilter` into two more backends and found the hole one layer up:
`AbstractBulkStore` / `AbstractAsyncBulkStore`'s **own six** filter-based destructive wrappers never called
it. Measured on `JsonStore`, which overrides none of them, `Delete(x => !empty.Contains(x.Value))` left
**0 of 3** rows with no exception — so the defect was live on JSON, XML, RavenDB, CosmosDB and InfluxDB,
none of which the finding named. Now called by all twelve paths: six base wrappers, InMemory's two
overrides, ElasticSearch's four. The standing rule is in § Conventions above. Split: **18 of 34** on the
whole revert (InMemory 10/16, JSON 2/5, ES 6/13), plus **2 of 69** on an isolating revert of the door-name
fix alone; 35 suites / ~2,100 tests green. Four things worth carrying:

- **A "wire it per backend" rule got read as "wire it only in backends".** The guard's helper and its
  unguarded callers were in the *same class*, and three tasks walked past them. When a rule says measure
  before wiring, that is about not guessing which shapes reach destruction — not a licence to skip the
  implementation every unlisted backend inherits.
- **The probe for the filed half found the real half.** The InMemory measurement was only supposed to
  confirm two overrides; including the four non-overridden `Update` paths in the same probe table is what
  exposed the base. Probe the whole verb family, not the methods the finding lists.
- **ElasticSearch is the third backend where the defect shape renders as ordinary output.**
  `!empty.Contains(x)` → `bool { must_not: [match_none] }` (selects everything) versus the legitimate
  `bool { must_not: [terms] }` — same structure, different inner type, so CR-H047's null-check guard never
  fired. After SQL's `1 = 1` and MongoDB's `$nin: []`, this is settled: guard the expression.
- **`.map.yml` under-coverage, fourth instance, this time caught before it mattered.** The ES stores were
  reachable by no glob in `bulk-filter-operations`, so the regen for this very fix would have been blind to
  them. Added. The older `AbstractConnectorBase.cs` gap the file already documents twice is still open
  (TASK-208).

---

## 2026-08-15 — The write half: a filtered DELETE/UPDATE drops the qualifier the read path aliases

TASK-216, spawned by TASK-211 rather than absorbed into it, because the mechanism is shared but the fix is
not. `ResolveColumnName(…, withTableName: true)` qualifies every condition name while a write quotes its
target table, so on PostgreSQL every filtered `Delete`/`Update` on a PascalCase entity failed:

```sql
DELETE FROM "FwPeople" WHERE FwPeople.Name = $1
-- ERROR: missing FROM-clause entry for table "fwpeople"
```

Fixed by **stripping** the target table's qualifier in `AddRequiredWhere` — not by quoting it and not by
aliasing (MSSql rejects `DELETE FROM t AS a`). The standing rule is in § Conventions above. Split: **4 of 22**
live PostgreSQL, **3 of 500** offline; 23 SQL suites green. Four things worth carrying:

- **Unlike the read half this was loud, and that is the whole reason it was a separate task.** The
  missing-FROM wording is not the missing-relation wording, so it threw rather than being swallowed. Same
  root cause, opposite failure mode — worth separating, because severity follows the failure mode, not the
  cause.
- **The reproduction found all four shapes at once, including both function-wrapped ones.** `LOWER(T.Col)`
  and the `.Date` rewrite's `(T.Seen >= @a AND T.Seen < @b)` are the shapes a per-condition-name rewrite
  misses, and they were in the first run because the probes were written to include them *before* choosing
  the mechanism. Design the reproduction against the fix you might get wrong, not the one you expect.
- **Two of the six probes passed immediately, and both were worth writing.** `SelectCount` goes through
  `CreateSelectCommand`, so TASK-211's alias already covered it — the task's acceptance asked whether a
  fourth sink existed, and measuring answered *no* instead of leaving it to be rediscovered. The other pins
  the whole-table write guard, which the rewrite runs after.
- **The regression test found a second, unrelated defect and it was filed, not asserted.** The obvious
  `Update(Table, values, conditions)` overload builds its SET list from **every** column while binding only
  the caller's subset, so a partial update emits unbound placeholders. Measured on both providers before
  filing (loud on each; SQLite leaves the row unchanged) — the initial guess, that unbound would bind NULL
  and silently blank the other columns, was wrong and would have justified a much larger fix. TASK-217.

---

## 2026-08-15 — Every read on PostgreSQL returned zero rows, and the swallow hid the bug that caused it

TASK-211, filed by TASK-209 as "on-the-fly views are broken". They were — and the measurement that confirmed
it also showed the premise was two orders of magnitude too small. The task named *multi-table joined*
SELECTs as the residue; the emitter qualifies **unconditionally**, so verbatim from the server:

```sql
SELECT OfPersons.Name, OfPersons.Guid FROM "OfPersons"
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 8
```

Every SQL store read funnels through that builder, so on PostgreSQL **every read of every entity whose table
name is not already all-lower-case returned zero rows, silently** — `Read()`, `ReadFirst()`, filtered,
unfiltered — and `TimescaleDBConnector : PostgreSQLConnector`, so it reached consumer Symbio. The fix is
`FROM "Widgets" AS Widgets`: quoted relation, bare alias, one site, every qualifier correct by construction.
The second half is the swallow that made it invisible — `IsMissingTableException` accepted any `42P01`
(*missing FROM-clause entry* shares that SQLSTATE with *relation does not exist*) plus a
`Contains("does not exist")` catch-all that also covered undefined **columns**. Both standing rules are in
§ Conventions above. Split: **5 of 16** live PostgreSQL, **7 of 538** offline across three suites; 23 SQL
suites green. Five things worth carrying:

- **⚠ Consumers: reads that returned nothing on PostgreSQL will now return rows.** No API changed and no
  behaviour that ever worked is altered — but code written around "this query is always empty" on
  PostgreSQL/TimescaleDB will start seeing data, and a rejected statement that used to read as an empty
  result now **throws** (an undefined column, function or type; a genuinely missing table still reads empty).
- **The filed defect was real, correctly diagnosed, and one twentieth of the actual blast radius.** TASK-209
  wrote the scope note from reading the emitter and got "multi-table" from the join it happened to be looking
  at. One probe against a single-table read moved this from a view bug to a framework-wide one. **When a
  residue is filed from reading rather than running, re-measure its boundary before costing it** — the
  seventh time in a month a written remedy needed re-costing, and the first where the correction made the
  task *bigger*.
- **The choice of mechanism was decided by the failure mode, not by symmetry with the previous fix.**
  TASK-209 quoted each qualifier through a `quoteTable` delegate, and copying that here looked obviously
  right. It would have been a partial fix: the read path's qualifiers include function-wrapped ones
  (`LOWER(T.Col)`, `COALESCE`, `.Date`), each a separate producer, and a missed producer reproduces the
  identical silent empty result. An alias is one site and cannot be partial. **When the failure mode is
  "silently incomplete", prefer the mechanism that cannot be incomplete.**
- **A guard that swallows an error class must be narrower than the bug it can hide.** `42P01` is *undefined
  table*, and PostgreSQL also raises it for a statement that merely mis-references an existing table — so
  the framework's own qualifier defect produced an error its own swallow classified as "table missing, yield
  empty". The two halves of this task are one story: nothing was ever red because the layer that should have
  gone red was configured to interpret the failure as an empty table.
- **The write path was measured, found broken, and NOT fixed here.** `DELETE FROM "T" WHERE T.Col = $1` fails
  the same way, but the alias does not port (MSSql rejects `DELETE FROM t AS a`) and writes fail *loudly*
  rather than silently. Filed as TASK-216 with its evidence, and the test that measured it was **removed
  rather than left asserting the broken behaviour** (the TASK-111 precedent).

---

## 2026-08-14 — A computed operand inside `Contains` was answered by a different predicate

TASK-213, found by TASK-137's own spec step — adding its shapes to the compiled-delegate oracle made a case
fail for an unrelated reason. `ids.Contains(x.Amount + 1)` never emitted an `IN`: the arm looped **every**
argument through `ParseConditionExpression`, so a computed operand was parsed as a nested **predicate**, took
the binary-comparison path, and fabricated a **subcondition** (`Amount = 1`) on the condition being built.
`AppendConditionTo` branches on `SubConditions` before it consults `Type`, so the `In` and its values were
discarded and the fabricated equality was emitted instead. Measured on SQLite: **1 row where C# says 0**, and
**3 where C# says 4** — wrong in both directions, silently. The operand now resolves through
`RenderValueFragment` exactly as a comparison's column side does. Split: **18 of 21**. Four things worth
carrying:

- **⚠ Consumers: an operand this parser cannot express now throws.** `ids.Contains(x.Name.Length)` (and an
  unmapped collection property in the extension-method form) previously returned rows chosen by a substituted
  predicate; they now raise `NotSupportedException` at parse time. That is § SH-H037's position, and the blast
  radius was measured across 22 SQL-touching suites with no failures — but the change is visible to a consumer
  whose predicate was quietly wrong.
- **The fix was a reuse, and the task's own § Approach had budgeted for a translator.** `RenderValueFragment`
  already rendered arithmetic / `COALESCE` / `CASE` / `.Value` and already threw for the rest, and
  `BuildValueComparison` was already doing precisely this for comparisons — so "translate or refuse" was not
  an open decision, it was answered by code that shipped months ago. Same shape as TASK-112, where the
  per-provider type mapping the task called "the bulk of the work" already existed. **Look for the existing
  producer before costing a new one.**
- **A test written to pin the fix passed against the defect, and only the revert said so.** The always-true
  read test used a seed with no NULL `Score`, and over non-null values the fabricated `NOT (Score = 0)`
  returns exactly the right rows. Adding one NULL row made SQL's three-valued logic diverge from C# and took
  the split from 17 to 18. Third instance of this in the epic (TASK-113, TASK-118) — **the revert is what
  classifies a test, not the intent it was written with.**
- **Two of the three surviving pins pass by arithmetic coincidence**, not by design: for
  `Ids.Contains(x.Score ?? 0)` and `x.Amount > 4 && Ids.Contains(x.Amount + 1)` the correct answer and the
  fabricated predicate's answer are both 0 rows on that seed. Their shapes are still covered by evidence
  because the *positive-match* variants of each fail on revert. Worth designing sets that match rather than
  sets that happen to exclude everything.

---

## 2026-08-14 — The tautology chosen for being harmless was the thing that walked past the guard

TASK-137. An empty `NOT IN` rendered `WHERE 1 = 1`, filed — correctly — as a log-hygiene defect: `1 = 1` is the
`' OR 1=1--` signature and trains operators to scroll past it. What nobody had checked was what the constant
does one layer up. `AddRequiredWhere`'s whole contract (SH-H002 / TASK-109, landed 18 days earlier) is
*"nothing rendered → refuse"*, and `1 = 1` is a **non-empty `WHERE` that constrains nothing** — so
`store.DeleteAsync(x => !emptyIds.Contains(x.Amount))` reached a whole-table `DELETE` **with the guard's
blessing**: measured on real SQLite, 0 of 3 rows left and no exception, while the `Update` twin rewrote 3 of 3.
Always-true terms are now reduced away instead of rendered, and `WouldTargetEveryRow` shares that reduction.
The standing rule is in § Conventions above. Split: **29 of 54** (re-derived; the first number, 29 of 45,
expired when the fix's own spec step added 9 oracle cases). Five things worth carrying:

- **⚠ Consumers: a call that used to succeed now throws.** `Delete`/`Update` with a filter that reduces to
  everything — an empty negated `Contains`, or an `OR` chain containing one — now raises
  `WholeTableWriteException` instead of writing every row. Anything relying on that (deliberately or not) must
  move to `DeleteAll()` / `UpdateAll(updates)` or an explicit `x => true`. Reads are byte-for-byte unaffected.
  `InConditionStrategy.BuildSql` also now throws for the empty negated case rather than returning `1 = 1`,
  which is breaking only for code calling the strategy directly rather than through `ConditionDefinition`.
- **The finding was right, its severity was wrong, and the task's prescribed remedy would have preserved the
  bug.** Acceptance criterion 2 required the sole-condition destructive case to reach TASK-109's
  *deliberate-all-rows* path — i.e. to keep deleting everything — reasoning that refusing "would be a
  regression, not a fix". It was inverted before any code was written. **Sixth time in a month a written
  remedy needed re-costing** (TASK-111, 112, 117, 129, 207). The tell each time is the same: the approach
  reasons about what the code *should* do without measuring what it *does*.
- **Reads were never wrong, which is why nothing found this for 18 days.** All eight read shapes returned
  correct rows before and after — `1 = 1` is genuinely always-true. The 9 cases added to the
  compiled-delegate oracle (`SqlExpressionParityTests`, the strongest instrument available: SQL vs
  `expr.Compile()` over a real database) therefore **pass either way** and are recorded as contract pins.
  When a defect's whole surface is a guard being fooled, no amount of result-correctness testing can see it.
- **The fix's own first draft had the defect it was fixing.** Dropping an always-true term that *opened* an
  `OR` run would have rendered `A OR TRUE AND B` as `A AND B` — the intersection instead of the union, a
  silent narrowing. Caught by reasoning through the reduction rather than by a test, since the flat-list path
  is unreachable from the parser (which always yields one nested root). The dropped term now hands its `OR` to
  the next survivor.
- **Adding the shapes to the oracle suite found a second, unrelated defect — filed, not asserted.** A
  **computed** operand inside `Contains` (`x.Amount + 1`, `x.Score ?? 0`) is silently discarded and replaced
  by a fabricated subcondition, so `SomeIds.Contains(x.Amount + 1)` over a *non-empty* set answers 1 row where
  the truth is 0 — a wrong answer in the positive direction, pre-existing and independent (TASK-213). The
  parity case was rewritten over a plain column: encoding the broken behaviour to keep the suite green would
  have blessed it (the TASK-111 precedent). MongoDB's half-guard is TASK-212 — `RequireFilter` refuses only a
  *null* filter, so the shape this task spent its whole scope on is unguarded there, filed with its mechanism
  marked **unverified** because the MongoDB driver owns the translation and nothing in this repo settles it.

---

## 2026-08-14 — Re-keying half a dictionary moved the collision instead of closing it

TASK-207, the residue TASK-129 filed rather than widening its own scope. `View.AddField`'s
`if (!table.Fields.ContainsKey(fieldName))` discarded any field whose key was taken — no column, no
exception, no log entry, the property reading back as `default(T)`. TASK-129 closed the aggregate instance by
keying aggregates on their view property and **left the guard**, which put two namespaces in one key space:
aggregates keyed by view property, non-aggregates beside them keyed by source column. Both surviving shapes
reproduce off the public fluent API and off the attribute builder — **6 of 7 first-pass tests failed against
unmodified code**. Every view field is now keyed by the property it populates. The standing rule is folded
into § Conventions' existing "one producer" entry above. Split: **7 of 9**. Three things worth carrying:

- **The task's own § Context under-counted the legitimate re-add paths, and that decided the design.** It
  named two; the load-bearing third is that `ViewAttribute` is `AllowMultiple = true`, so `LoadView` runs its
  whole per-property field loop **once per `[View]` attribute** — the ordinary way a three-table view
  declares its second join — re-presenting every field as a *fresh* `AbstractField`. § Approach's preferred
  option ("throw when the incoming field is genuinely different") would have broken every such view under the
  natural reading of "different". Fifth time in a month a prescribed remedy needed re-costing (TASK-111,
  TASK-112, TASK-117, TASK-129). **Enumerate the callers before choosing between report-it and prevent-it.**
- **Prevent beat report, and then both shipped.** Keying by view property makes the collision impossible;
  the throw stays as a backstop for `AddField`'s explicit `name` and for `AddTable`, because "I keyed it so
  it can't collide" is construction, not evidence — the same argument that put a test on `FlushDatabaseAsync`
  being off `ICache` (TASK-117). The backstop has its own test and its own opt-out test.
- **The fix landed in a file no spec area covers, exactly as the map predicted.** `View.cs` is in the 90% of
  `Birko.Data.SQL.View` the map excludes; its comment names TASK-129 and asks for a DECISION (TASK-208, still
  open). The new spec scenarios are grounded in `SqlViewTranslator.cs` instead and the backstop is left
  unspecced — the second consecutive task to hit this, which is the argument for deciding TASK-208 rather
  than routing around it a third time.

---

## 2026-08-14 — An aggregate column had two names, so it got two aliases — and the quiet half lost a column

TASK-129. Every SQL view containing an aggregate generated
`COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — **two aliases on one column**, which SQLite rejects with
`near "AS": syntax error` and which is a syntax error on every other provider, so a persistent (or `Auto`)
aggregate view could **never be created**. The capability was unreachable, not degraded. Reproducing it turned
up a second defect with the same root cause and no filing: two aggregates of the *same* function collided on
their `Fields` dictionary key and the second was **dropped silently** — no column, no exception, no log entry,
the property reading back as `default(T)`. The standing rule is in § Conventions above. **Split: 15 of 17**
new-or-changed tests fail on a full revert. Five things worth carrying:

- **CR-L195 was right and only got two thirds of the way.** It decided an aggregate's identity is its view
  property and taught `GetPersistentViewSelectFields` and `ViewOrderFieldName` to read `field.Property.Name` —
  then aliased at its own emit site instead of at the producer, leaving `Table.GetSelectFields` still reading
  the dictionary key. Two producers of one name is what put two aliases on the column. The fix moves the read
  to the producer so a fourth consumer is correct without being told.
- **This task's own § Approach recommended the smaller fix, and the smaller fix was wrong.** It suggested
  parameterising an un-aliased projection so the shared `Table.GetSelectFields` stayed untouched — which
  closes the syntax error, leaves the identity split, and leaves the silent-drop defect entirely alive. Its
  *caution* was still correct about what to check: the on-the-fly path was verified to read **positionally**
  (`SqlViewStore.CreateTransformFunction` ignores the field-name map) and ORDER BY to match on
  `Property.Name`/`Name`, never the key — which is what made the larger fix safe. **Check what the written
  approach tells you to check, then re-decide the approach** — fourth time in a month a prescribed remedy
  needed re-costing (TASK-111, TASK-112, TASK-117).
- **The shipped test asserted the absence of the broken thing, and the broken thing passed it.**
  CR-L195's pin was `sql.Should().Contain("AS \"OrderCount\"")` — satisfied byte-for-byte by
  `as COUNT AS "OrderCount"`. It never executed the DDL. Its MSSql twin asserted `AS [OrderCount]` and did
  the same. Every DDL assertion now **executes** against SQLite and reads the created columns back out of the
  database, compared to what `GetPersistentViewSelectFields()` asks for rather than to a literal. Same lesson
  as the `b-chart` suite (2026-08-09): **assert the shape you want positively.**
- **Two tests changed classification by changing what they execute.** TASK-128's two
  `Persistent_aggregate_sort_*` were contract *pins* while their DDL was hand-written — they asserted a shape
  the generator could not produce, so no revert could touch them. Switching them to the generator (an
  acceptance criterion here) made both fix-dependent evidence. TASK-118 saw this in the opposite direction;
  the rule is that **classification follows what a test executes, not what it was written for.**
- **The fix landed partly in files no spec area covers, and `.map.yml` had predicted exactly that** — naming
  this task, and asking for a DECISION rather than a coverage fix. `ViewSelectSqlBuilder.cs` and
  `DataBase_View.cs` are in no area's globs, so the spec diff could not be evidence for the part of the
  change that lives there; a fix confined to those files would have produced a spotless diff. Left excluded
  and filed as TASK-208 (`assignee: human`) rather than silently widened — a note that gets quietly acted on
  stops being a decision. The residual silent-skip in `View.AddField` is TASK-207.

---

## 2026-08-12 — "Clear the cache" deleted the database — by default, and the default had no default

TASK-117 / SH-H006. With no `KeyPrefix` configured — the **default**, since `RedisSettings.KeyPrefix` is an
unassigned `string?` — two doors in `RedisCache` targeted every key in the logical database, including the
queued messages and pending jobs of the siblings that share the connection by design
(`Birko.MessageQueue.Redis`, `Birko.BackgroundJobs.Redis`, the Redis sync stores). `ICache.ClearAsync` promises
to clear *the cache*; the implementation was wider than its own contract. Both doors now refuse with
`WholeDatabaseDeleteException` before opening a connection, and `FLUSHDB` lives on
`RedisCache.FlushDatabaseAsync` — off the `ICache` surface. The generalised rule is in § Conventions above.
Split: **9 of 27** new tests fail on a surgical reintroduction. Six things worth carrying:

- **The finding named the loud door and the quiet one was next to it.** `ClearAsync`'s `FLUSHDB` is what the
  finding described — but `FLUSHDB` is admin-gated by StackExchange.Redis (measured by reflecting the shipped
  2.8.41: `Message.IsAdmin` is `true` for `FLUSHDB` and `KEYS`, `false` for `SCAN`/`DEL`), and
  `GetConnectionString()` never emits `allowAdmin=true`, which nothing in `Framework`, `Framework.Tests` or
  `Consumers` sets. So on a settings-built cache that branch **threw** rather than flushing. The door that
  destroyed data silently on every configuration was `RemoveByPrefixAsync("")` — `SCAN "*"` + `DEL`, neither
  gated — found while re-verifying and filed as *secondary*. It was primary. Two lessons: **a defect's
  reachability depends on the client library's own gates, not only on the code path**, and the ranking
  rationale ("the default path destroys and reports success") was right about the defect and wrong about which
  command did it. Both were recorded, then corrected.

- **The finding's preferred remedy was unimplementable, and the reason is worth more than the fix.** It asked
  for an unprefixed clear to delete "this cache's entries and leave other keys intact". That set does not
  exist: an unprefixed cache writes bare keys, so they are byte-for-byte indistinguishable from every
  sibling's, and two unprefixed caches on one database *are* the same key space. Every way to invent one was
  worse — an owned-key index needs a key name (the layout change the finding's option 2 was rejected for),
  costs a round-trip per write, and grows without bound because Redis expiry does not remove members; while
  scanning a made-up prefix finds nothing and turns the clear into a **silent no-op reporting success**. The
  option the task ranked *last* was the only one that is neither destructive nor a lie. **When a finding
  prescribes a remedy, check the remedy is possible before costing it** — third time in a fortnight (TASK-111
  rejected "resolve and quote", TASK-112 found the mapping already built).
- **The documented door was not the only door.** `RemoveByPrefixAsync("")` reached the identical
  whole-database delete by scanning `"*"` instead of `FLUSHDB` — same root cause, one function away, not in
  the finding. Fixed together through one resolver. Findings travel in packs, and the pack members are
  usually in the same file.
- **Widening the guard meant narrowing what it fires on.** Guarding "no `KeyPrefix` configured" would have
  refused `RemoveByPrefixAsync("user:")` on an unprefixed cache — bounded and legitimate. The guard is on the
  *resolved scope* (`"*"`), not on the configuration that produced it. A refusal must not fire on the case it
  was never about.
- **`verify-conventions` found the register-on-introduce gap, and its own step 0 is why.** This is the second
  instance of one guard (after `WholeTableWriteException`) and § Conventions recorded it only in SQL terms —
  precisely the TASK-111 lesson arriving again, twelve days later, in a different backend. The frozen
  checks 1–10 could not have caught it; the live rulebook sweep did. Check 5 then caught that a ticked
  criterion ("`FLUSHDB` not reachable from `ICache`") rested on construction rather than a test.
- **The close gate then found a one-character bypass of the new guard, and the split had to be re-derived
  three times.** `security-review` could not run (no `origin/HEAD`, and the production change is in a sibling
  repo no skill in this repo can diff), so the pass ran **inline** — which is the only reason the question
  *"can this guard be walked past?"* got asked. It could: the literal prefix went into a Redis `MATCH` pattern
  unescaped, so `RemoveByPrefixAsync("*")` resolved to `"**"` — non-null, past the emptiness check, and
  matching **every key in the database**. A `KeyPrefix` of `"*"` did it to `ClearAsync` via `"*:*"`. The same
  escaping fixed a latent read/write disagreement, since `GetFullKey` always wrote metacharacters as literals.
  Two things generalise: **when a review skill fails to resolve, run the pass by hand — the gate is not
  optional**, and **a scope guard's own test is whether a caller can widen the scope back**, not whether it
  fires on the reported input. The split went 5 of 13 → 5 of 16 → **8 of 25**; the first number would have
  understated the check by three tests and the fix by a whole defect. Final: **9 of 27**, after the review's
  findings added two more.
- **The regression suite for a destructive-clear defect was itself destructive.** The tests asserting a call is
  *not* refused must run past the guard, so they reached `GetDatabase()`/`GetServer()` — and pointed at
  `localhost:6379` they issued real `SCAN test:*` + `DEL` and `SCAN user:*` + `DEL` against database 0. On any
  developer box with a local Redis they were deleting live `user:*` keys, and
  `NotThrowAsync<WholeDatabaseDeleteException>` swallowed every trace. They now point at TEST-NET-1
  (`192.0.2.1`, RFC 5737, never routable). The tell was in plain sight and nobody read it: suite runtime went
  **36s → 800ms** once the connections stopped. **A "not refused" assertion is an instruction to execute the
  dangerous path** — give it somewhere harmless to execute, and treat a slow offline suite as evidence it is
  not offline.
- **A `NotThrowAsync<TSpecific>` assertion passes on every other exception.** `FlushDatabaseAsync_IsNotItself
  Refused` claimed to prove "the escape hatch opens" while asserting only that one exception type was absent —
  so it passed identically whether the flush worked, hit the admin gate, or never connected. Narrowed to what
  it can actually see, with the real property (the admin precondition) pinned separately and by measurement.

---

## 2026-08-12 — A rule field was executable SQL, and the second sink proved the first one's rule

TASK-111 / SH-H023. `RuleConditionConverter` made a rule's `Field` into the condition's **name**, and every
strategy interpolates that straight into `CommandText`. Measured against SQLite on a 3-row table, with rules
whose value matched nothing: `Rank OR 1=1 --` returned **3 rows of 3**, and
`Rank; CREATE TABLE Pwned (x INTEGER); --` **created the table** — from configuration data that
`docs/rules.md` advertises as a way to build "dynamic filtering from user-defined rules". Fields now resolve
against table metadata via new type-aware overloads (`ToConditions<T>`), which also fixes the ordinary half:
a `[NamedField("label_col")]` property emitted `WHERE Label = @p` and the database answered *no such column*,
so a remapped property could not be filtered at all. The standing rule is in § Conventions above.

Four things worth carrying:

- **The second instance is what turns a fix into a rule.** TASK-110 closed the identical defect on ORDER BY
  twelve days earlier and recorded its reasoning beautifully — in a commit message and a doc comment, which
  is exactly where the *next* sink's author will not look. Two sinks, one root cause, and no § Conventions
  entry between them. The shared `ResolveFieldNameIn` and the rule above exist so the third sink is a
  compile-time reuse rather than a rediscovery. **When a fix is the second of its kind, the deliverable
  includes the rule, not just the fix.**
- **The prescribed remedy was wrong, and the closed twin was the evidence.** The finding said "resolve
  **and quote**". Quoting would have broken working filters on PostgreSQL, where an unquoted DDL identifier
  folds to lower case — TASK-110 had already measured this and rejected it. Following the finding would
  have shipped a regression while closing a hole. Read what the twin decided before re-deciding it.
- **A test tripped over a second, unrelated defect, and the honest move was to not assert it.** The
  end-to-end OR-group check returned 0 rows where 2 were expected — SH-M128, already filed, different root
  cause (`ConvertGroup` wraps with `AndSubCondition`). Asserting the OR result would have had to encode the
  broken behaviour to stay green, which blesses it. The test uses an AND group and says why.
- **Full revert would have reported a fraction of the check.** Most of the new tests reference the new
  type-aware overloads and would not compile against the pre-fix tree, so a plain revert would have hidden
  them behind a build error — the TASK-204 trap arriving again. Reintroducing the defect *surgically* (one
  line in `ConvertLeaf`, every signature intact) gave a real split: **42 of 55**.
- **The split was then reported stale, and `/code-review` caught it.** The first recorded numbers ("34 of
  42", plus two mutually inconsistent totals) were taken *before* the security pass added four payload
  cases, and were carried by hand across three edits without being re-run. **A red-verify split expires the
  moment the suite changes** — re-derive it as the last step before reporting, never carry it forward. Two
  further defects in the fix came out of the same review: rules over a `[View]` type threw on the first
  call in a process (the resolver was registered only inside `LoadView`, while rule fields resolve at the
  caller — now a `[ModuleInitializer]`), and the "don't quote, for PostgreSQL" rationale was recorded in
  three places without noting that it covers the *column* and not the *table qualifier*.

---

## 2026-08-12 — One index it could not build took down six entities' read surfaces — and the fix leaked per request

TASK-204. A duplicate `(TenantGuid, OrderNumber)` pair made a later-declared UNIQUE index unbuildable, and
because schema-ensure runs lazily and sets `_initialized` only on success, the store never initialised and
**re-threw on every later operation** — reads included, on six entities in consumer Symbio, permanently. An
unbuildable index is now recorded (`IndexCreationFailures` / `OnIndexCreationFailed`) instead of thrown; the
two standing rules that came out of it are in § Conventions above. Four things worth carrying:

- **The read surface is what makes repair possible.** The old behaviour was self-sealing: you could not read
  the duplicate rows to delete them, because reading them ran the schema-ensure that refused. Degrading the
  index kept the door open, and the fix's own recovery test walks through it — read, delete the duplicate,
  re-init, index builds itself with no restart.
- **The fix had the same class of bug as the defect.** An append-only failure `List` on a **process-cached**
  connector, fed by a **per-store** init flag, grew one entry per HTTP request and re-raised its event each
  time. Measured 5 stores → 5 entries and 5 re-executed failing DDL statements. Lifetime mismatches between
  a cached collaborator and its per-instance gate are worth checking whenever you add state to a connector.
- **Only two of the original five tests were evidence.** The other three referenced the new API and so could
  not compile against the reverted fix — pins, not proof. Counting them as red-verified would have reported
  five-fifths confidence for two-fifths of a check.
- **Found by diffing the working tree, not by a failure.** This arrived as three modified files with no
  commit, no test and no task — the third such find in a week after TASK-197/198. Nothing in this repo knew
  it existed, and nothing would have.

---

## 2026-08-09 — Six fixes that were written but never committed — and the false premise that kept them there

A sweep of the sibling repos found six real fixes sitting in working trees or landed without their
aggregator commit. All are now committed with framework-side regression coverage: `TimeOnly` mapping
(**TASK-197**, `b0dec59`), the `.Date` predicate rewrite (**TASK-196**, `f3cdf99`, landed 2026-08-08 with
no tracking at all), and four in the `Birko\Web` bucket — `b-chart`'s span-aware time axis (`a2521ce`),
`FormControlComponent`'s `willValidate` guard (`5a94c59`), `BaseCrudPage._openEdit`'s pre-fill window
(`352e198`) and the `setChecked` page object (`69e0583`), with Playground coverage in `c285e48`.

The individual defects are in their task files. What generalises:

- **A false premise about tooling can quietly replace version control.** The four Web fixes were
  uncommitted because a consumer's `docs/birko-framework-fix-prompts.md` recorded, twice, that *"the
  framework repo has no git"* and reproduced the diffs as prose on that basis. All four packages are
  ordinary git repos (Components 165 commits, Shell 59, Core 53, Testing 2). For two days a consumer's
  docs folder was the source of truth for framework code, and Symbio's committed `wwwroot/app.js`
  shipped a bundle whose sources existed in no repository. Nothing was broken and nothing failed —
  the only symptom was a note explaining why it had to be that way. **Run `git log` before concluding
  a repo is unversioned;** the cost of being wrong is invisible until a restore.
- **A polyrepo fix that stops at two commits looks finished.** `.Date` had its production fix and its
  regression suite, both excellent, and no aggregator commit — so no task, no `pr:` sha, no spec regen,
  and nothing in this repo knew it existed. It was found by diffing sibling `git log` against this
  repo's HEAD, which is now the only reliable way to notice: the third commit is the one with no
  compiler or test to demand it.
- **"We enumerated what this will break" was not true.** `TimeOnly` was on neither TASK-112's
  CLAUDE.md note nor TASK-150's list of remaining unmapped types, and it is the one that reached a
  consumer. An inventory of what the *mapper omits* is worth less than an inventory of what *consumer
  models declare* — grep the consumer trees, don't reason from the dispatch.
- **A wrong finding id survives every check a compiler can make.** The `TimeOnly` code cited SH-H038,
  which is an unrelated ElasticSearch reindex finding; it came in with the working-tree comments and
  was carried into two commit messages before anyone compared it to the register. It points every
  "which findings are closed" sweep at the wrong defect.
- **Two regression suites passed against the wrong thing, in opposite directions.** The `b-chart`
  checks set `type: 'line'` via `setOptions` — but `type` is an *attribute*, so the default **bar**
  renderer ran and its thinned category labels (`"0"`,`"3"`,`"6"`…) satisfied a "no label is a clock"
  assertion; three checks passed against a chart that was never a line chart. Assert the shape you
  want **positively**, never the absence of the broken one. And the `willValidate` checks used only
  `b-input`, which reports no validity flag while disabled, so they passed with the fix reverted — the
  throw needs `b-select`/`b-textarea`. **One representative is not a suite**, and picking the wrong one
  gives a green run over a live crash.

---

## 2026-08-08 — Five CLR types that mapped to no column at all — and the mapping that was waiting for them

TASK-112 / SH-H037. A `[Table]` model with a `long`, `short`, `double`, `float` or `byte[]` property got a
`CREATE TABLE` **without that column**: the value was dropped on every save and read back as the type's
default, with no exception and no log entry. `decimal` *is* mapped, so money was safe — which is precisely
why it survived; what vanished were identifiers, measurements and blobs. Five new `AbstractField`
subclasses and their dispatch arms fix it, and an unmappable type now throws instead of disappearing (the
standing rule is in § Conventions above).

**⚠ Consumers: this can break a running deployment.** A consumer whose model already carries a `long` has a
live table with no such column. Adding the mapping means their DDL and their live schema now disagree, and
a read will *fail* rather than silently return zero. Migrating those tables is deliberately out of scope —
it is a consumer decision. The same applies to any model with a property the mapper still cannot express
(`char?`, `TimeSpan`, `DateTimeOffset`, collections): those now throw at table load where they previously
loaded fine minus a column. `[IgnoreField]` / `[NotMapped]` is the opt-out.

Four things worth carrying past this mapper:

- **The finding was right and its cost model was wrong — check both.** The task called the per-provider type
  mapping "the bulk of the work". All four `ConvertType` implementations **already** had `Int16` / `Int64` /
  `Single` / `Double` / `Binary` arms emitting exactly the requested types, `AddParameter` binds untyped, and
  `ModelMap<T>` does no type dispatch at all. The mapping had been built for a `DbType` the dispatch could
  never produce. Scope went from four providers to one method plus five small classes; following the written
  approach would have meant a large pointless change.
- **A test that builds the object under test by hand cannot witness a dispatch fix.** Step 6's first run had
  **all 15** per-provider DDL tests passing with the fix reverted — they constructed `new LongField(...)`,
  and the field classes survive a revert that only touches `CreateAbstractField`. They were pinning the
  providers' `ConvertType` contract while looking like evidence. Driving `DataBase.LoadTable(typeof(Model))`
  instead took the provider suites from 0 → 15 failing, and the total from 28 → **43 of 52**.
- **Fixing an inert defect is cheaper than filing it.** SQLite mapped `DbType.Single` → `INTEGER`, grouped
  with the integral types — the identical mistake PostgreSQL and MSSql had both already fixed under CR-H087.
  It was unreachable because nothing could produce a `Single` field; the moment `float` mapped, the reference
  and test provider would have been the one declaring a float column as an integer.
- **A silent skip and a wrong answer are the same bug.** `char?` was never in this task's scope and is still
  unmapped — but the fail-fast changes it from *silently dropped* to *reported*, which surfaced a latent
  instance of the very defect being fixed. Pinned by a test and specced rather than quietly mapped.

---

## 2026-08-04 — Birko.Web: a cascade invariant that needed asserting twice, and two half-fixes

A review of what landed in the `Birko\Web` bucket over 2026-08-02…04. Three shipped fixes, one in flight; the
per-component detail is in `Birko.Web.Components`/`.Shell`'s own CLAUDE.mds. What generalises past the web
tree:

- **A style invariant that must hold in both trees needs a rule in both sheets.** `[hidden] { display: none
  !important }` now sits in `reset.css` *and* in `BasePage.styles` — `BasePage.styles` is adopted into a
  shadow root and `reset.css` styles the document, so neither reaches the other. The UA's `[hidden]` rule
  ties on specificity (0,1,0) with any class selector, so an author `display` declaration silently un-hid an
  element the code believed was gone: it shipped a four-week date range recorded as **one day**, with a
  success message, under a fully green suite. `!important` because a specificity bump only outranks the
  selectors that exist today. **Assert computed style, not the attribute** — an attribute assertion was green
  throughout the original defect.
- **Two layers sizing themselves in different viewport units is a defect even when both units are correct.**
  `BCoreAppShell` sized itself in `dvh`; `reset.css` kept `body { min-height: 100vh }`. `vh` is the *large*
  viewport, so on Android Chrome in a tab the body was taller than the shell by exactly the URL-bar height
  (56px), giving dead scroll that dragged the bottom nav behind the system bar. Neither declaration is wrong
  in isolation; the disagreement is the bug. And it **cannot be reproduced headlessly** — with no retractable
  chrome `vh == dvh` — so the guard asserts the declaration and labels itself as not a reproduction.
- **A regression test the fix's own revert cannot break is not evidence.** Two of the three fixes shipped as
  half-fixes for exactly this reason. `b-segmented`'s touch floor cited a 44 × 44 target and floored only
  *height*, and the test asserted height; the consumer's suite ran a locale whose labels all cleared 44px
  from padding alone, so no label short enough to fail existed. Reviewing the in-flight
  `Surface.alsoMatches` change found the same shape: its "first match wins" check passed with the fix
  reverted, because the fallback chain's last resort is `surfaces[0]` — the same answer it asserted. Both are
  fixed and red-verified (3 of 6 nav checks now fail on revert, up from 2).
- **A shared component's regression test belongs in the framework, not only in the consumer that reported
  it.** The half-floor's only coverage lived in the consumer for two months, which is how a single-locale
  suite was able to bless it. The framework check now includes the inverse case (a *fine* pointer stays
  dense), since a fix that floored every pointer type would have passed the original assertion and silently
  resized every consumer.

---

## 2026-08-03 — A tenant-scoped sync that only filtered its writes — and deleted other tenants' rows

TASK-113 / SH-H050+H051+H052, three findings with one root cause. `TenantSyncProvider.ApplyTenantFiltering`
wrapped `CanSaveToLocal`/`CanSaveToRemote` and **said so in its own XML doc** — *"only modifies save filters,
not fetch predicates"*. Everything else followed from that sentence being true: every tenant's rows entered
`localDict`/`remoteDict`, so a preview under tenant *t* enumerated and version-hashed another tenant's
entities, their guids went into the knowledge store, and the `SyncAction.Delete` arm — the one arm that
consulted no predicate at all, unlike Create/Update/conflict-resolution — **deleted them**. The tenant term
now goes on the fetch predicates and one `ResolveTenantScope` answers "which tenant" for the whole run. The
two standing rules this produced are in § Conventions above; four things worth carrying past this provider:

- **A silent no-op beats a wrong answer only if someone notices.** The worst of the three was the *documented*
  call shape: `SyncAsync(new TenantSyncOptions { TenantGuid = u })` from a background job with no ambient
  tenant keyed knowledge to *u* while `if (_tenantContext.HasTenant …)` installed **no write filter at all**,
  copying every tenant's items into both stores. The configuration the docs recommend was the one that failed
  hardest, and it reported success.
- **Widening the guard's reach meant narrowing what the API accepts.** Ambient *t* plus an explicit *u* now
  **throws** instead of resolving by precedence, and a tenant-scoped entity with no tenant anywhere throws
  instead of syncing everything. From first principles the explicit option is the more specific instruction
  and should win — rejected, because that is code running in *t*'s scope reaching *u*, the same shape as
  SH-H048, and a silent winner makes it unobservable. The deliberate cross-tenant caller says so out loud.
- **A refusal must not fire on the case it was never about.** The missing-tenant throw applies **only** to
  entity types that declare `TenantGuid`. This provider legitimately serves models without one — two
  pre-existing regressions use exactly such a model — so an unconditional throw would have broken working
  behaviour rather than closing a hole. Fail-closed still has to know what it is closing.
- **The tests that passed before the fix are the informative ones.** 14 of 37 failed on the revert;
  `Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore` passed, because the write filter was the single path
  the old code *did* scope. Recorded as a contract pin, not as evidence — a pin logged as proof is how the
  next reader concludes a fix was verified when it wasn't.
- **The review found a defect in the fix, and then the revert found a defect in that fix's test.**
  `code-review` caught that scoping the fetch predicates wrote them back onto the **caller's**
  `SyncFilterOptions`, so the per-tenant admin loop — reusing one instance, the shape the README had just
  blessed — would carry `t1 && t2` on iteration two and silently sync nothing (fail-closed, so it would have
  read as "tenants stopped syncing", never as a leak; third time this file has been bitten by writing to a
  caller-owned object, after CR-M168). Its regression test then **passed the pre-fix revert**: asserting only
  the loop's *end state* ("both tenants present") cannot distinguish two correctly-scoped iterations from one
  unscoped iteration that copied everything. Asserting after *each* iteration fixed it. **Re-run step 6 when
  a later step adds a check** — a test written to pin a fix is not automatically evidence of it.

Also: `/specs regen` for `tenant-isolation` deleted three scenarios that **asserted the defects** as shipped
behaviour (`Both stores are read across all tenants`, `Deletes bypass the save predicates entirely`, `A
caller-supplied TenantSyncOptions is mutated in place`) — the ordering constraint the spec harvest warned
about, arriving exactly as predicted. And the `shaped-by` evidence pass **cannot run from this repo at all**:
every `tenant-isolation` source glob points into a sibling repo, so no task's `pr:` sha resolves under
`git show` here. That is true of every area in this aggregator's spec tree, not just this one.

---

## 2026-08-02 — The tenant guard was on a transport, not on the tenant — and its own correction was wrong

TASK-118 / SH-H048. The guard shipped on 2026-07-28 compared one hard-coded `X-Tenant-Id` header against the
JWT `tenant_id` claim. Every other door was open: `TenantQueryStringKey`, `TenantRouteKey`, both
custom-resolver hooks, `SubdomainTenantResolver`, and — the quiet one — a **renamed**
`TenantMiddlewareOptions.TenantHeaderName`, which made the guard stop working with no error on a deployment
that looked correctly configured. A caller authenticated in tenant A reached tenant B's reads *and writes*
with their own permissions intact, and nothing failed or logged. Both resolving middlewares now publish their
result as a `ResolvedTenant` and the guard checks that, so a source added later is covered without editing it.

Four things worth carrying past this middleware:

- **A correction to a finding can be the thing that's wrong.** Both the filed finding and this task instructed
  the fix to record that "no `RouteValues` tenant source exists". It exists — `TenantMiddleware.cs:111-120`.
  Following the correction would have deliberately left a live, unguarded source out of a security fix. The
  usual step-3 failure is a finding that overstates; this one *understated*, via its own verification pass.
  Re-verify the corrections too, not just the claims.
- **Where the resolution travels decides whether the guard fails open.** Reading the resolved tenant off
  `ITenantContext` covers every source and is simpler — and would fail open, because `UseTenantMiddleware`
  binds its context from the root provider (SH-H049) while the guard resolves one per request, so under
  `AddTenantContextScoped()` they are different objects and the guard sees no tenant. It goes on
  `HttpContext.Items` under a **fixed** key: `TenantContextKey` is configurable, and a guard keyed on a
  configurable name is defeated by the same class of config change as the hard-coded header constant was.
- **Widening a guard can narrow it.** Replacing the literal `X-Tenant-Id` check with the resolved-tenant check
  would be cleaner and would have been a *coverage regression* for any app that never wired a tenant
  middleware but reads the header in its own code — the premise the original guard was written on. Kept both.
- **The revert reclassified one of the tests.** `SystemScopeToken_CannotAddressARealTenant` was filed as a
  contract pin and failed on the step-6 revert: it reaches the victim through the query string, so it was
  never pinning old behaviour. Split: **9 of 16 failed**, 2 more don't compile pre-fix, 7 genuine pins. The
  guard had also shipped with **zero tests** — this is its first suite.

---

## 2026-08-02 — Shadow depth per theme, and the generator drift that had been running for two days

Started as a question — *should shadows be distinguishable on dark / neon / inverse?* — asked while trying
the new global theme switcher in `Birko.Web.Playground`. Measuring rather than eyeballing answered it and
turned up two defects and a process hole.

**The scale, not the depth, is the contract.** `sm`→`lg` on the dark themes were already fine: they raise
alpha from light's 0.05–0.1 to 0.3–0.6 and measure on par with light. But **`dark`/`neon`/`inverse` had
never overridden `--b-shadow-xl`**, so it fell through to the light `:root` value (0.1/0.04) and measured
**7 / 4 / 9** against those themes' own `--b-shadow` at **13 / 10 / 15** — the ladder inverted at its
deepest rung, and `xl` is the level *every* overlay uses (`b-modal`, `b-drawer`, `b-confirm-dialog`,
`b-command-palette`, `b-tour`). Now 33 / 25 / 37. `finstat` had the same inversion one rung lower
(`md` > `lg`, from a 1:1 mapping to the legacy `@box-shadow-preset`); where a brand mapping contradicts the
ladder, **the ladder wins** — `md` and `xl` still map 1:1.

Three things worth carrying past shadows:

- **A metric that is too narrow invents defects.** Judged on peak pixel, finstat's `xl` looked broken too.
  It isn't: it is deliberately wide and diffuse (`0 20px 70px -25px`), reads as the theme's deepest level,
  and only measures a low peak. Switching to *ink* (delta integrated down the column) cleared `xl` and kept
  `lg` condemned. Assert the ordering; the absolute number is decoration.
- **A question can be correctly closed with "no change".** `dark`/`inverse` set `--b-bg-secondary` ==
  `--b-bg-elevated`, so a `b-card` has zero surface step — which sounds like a defect until measured: the
  card edge reads at **26 / 23** against light's **22**, because the 1px border does the work. Nothing
  changed, and a permanent check now guards the property that made "no" the right answer.
- **Some defects have no DOM to assert against.** `--b-shadow-*` has no element, no ARIA, no geometry, so
  `verify.mjs` and every in-page smoke suite are blind to it *by construction*. It has to come off rendered
  pixels — screenshot, hand the PNG back into the page, sample a canvas. Now a permanent group in the
  playground's `device-fix-check.mjs` (63 checks, up from 47), verified to fail by reverting both fixes.

**The process hole is the more valuable find.** `verify` was **already red before any of this work**:
`c97d9bd` and `e07f9d3` had written real fixes straight into the *generated* CSS — the four dark
`--b-color-*-light` tint fixes and `--b-split-detail-sticky-top` — so `generate` would have deleted them,
and did, until they were restored and recovered with `extract`. That recovery exposed a live bug nobody
could see: the **Avalonia dark dictionary was still serving the light pastel tints** (`#DCFCE7`/`#FEF3C7`/
`#FEE2E2`/`#CFFAFE`) under near-white text — the very defect the web side fixed on 2026-08-01 — because
AXAML is generated from `tokens.json` and agreed *perfectly* with a stale source.

- **`verify` answers "does the output match the source", never "is the source still true".** A green run is
  evidence of consistency, not correctness. It flagged the two CSS files and passed the AXAML that was
  actively wrong.
- **Only the CSS has ever drifted, and only the CSS lacked a banner.** Every AXAML dictionary opens with
  `AUTO-GENERATED … DO NOT EDIT`; `tokens.css` opened straight into `:root {` and `dark.css` opened with a
  prose "how to use this theme" comment that reads exactly like a hand-authored file. All five sheets now
  carry the banner (in `Sheet.prologue`, so the verbatim round-trip keeps it with no emitter change), with
  `CssParityTests.Every_sheet_declares_itself_generated` per sheet.
- **The banner is a human signal; the gate is CI, and there was none.** `tokens.json`, the CSS, the AXAML
  and the parity suite live in **four separate repos**, so no single checkout can run the gate — which is
  exactly why an editor's diff, review and test run never mention `tokens.json`. A `token-parity` workflow
  now exists in **all four**: a gate that only fires on the source cannot catch an edit made to the output.

---

## 2026-07-30 — `b-button`: the form story was already decided — the button had just been left out

Third gap under STORY-052 (framework `TASK-107`), from Reps stopping **before** converting 69 buttons because
reading the component first found two silent regressions. Shipped: `--b-button-padding-y` / `-x` (no size
reached a mobile tap target, and the consumer could not fix it from outside without hijacking the button's own
`gap`), and form participation via `type` (`button` default, `submit`, `reset`).

Three things worth carrying:

- **Check whether the "open question" is already answered before you decide it.** This arrived as a three-way
  policy choice — `ElementInternals` vs a synthetic submit vs documenting it away. The catalogue had settled
  that a day earlier: `FormControlComponent` (`formAssociated` + `attachInternals`) already backs **15**
  controls, and `b-button` was the only member of the family still on `BaseComponent`. What looked like a
  design decision was a three-override omission. Read the family before writing the policy.
- **Where a default is contested, the call sites decide it.** From first principles `type` should default to
  native's `submit` — defaulting to `button` preserves exactly the silent-nothing-happens bug that raised the
  ticket. One grep overruled it: Presenter has five `b-button`s in a `<form>` whose `submit` listener does
  something *different* from the buttons' own click handlers, so a native default would fire two actions on
  one tap. Symbio has 102 files of `b-button` and no `<form>` at all. Opt-in it is — and the reasoning is
  written down so it is not "fixed" later by someone reasoning from first principles again.
- **Expose the knob, not the policy** — the same line taken on `--b-card-shadow`. A `pointer: coarse` media
  query inside the component would have fixed every consumer with no opt-in, which is precisely the problem:
  it re-renders apps that did not ask (a desktop back-office on a touch laptop reports `coarse`) and makes a
  component's size depend on the input device rather than its design. The consumer writes the media query.

Also: the origin task's premise (`--b-space-md` ≈ 44px, from Reps' own `.btn`) **did not survive
measurement** — `b-button` fixes its font at `--b-text-sm` with a tight line-height, so it measures 32px →
39px → 46px across `sm`/`md`/`lg` padding, and `--b-space-lg` is the rung that clears 44. The numbers live in
the smoke check names rather than in a pass/fail against a magic constant.

## 2026-07-30 — `b-card`: two additive options, one refusal, one deferred framework-wide decision

Second gap under STORY-052, from Reps adopting `b-card` (framework `TASK-105`). Shipped: a `padding="md"`
rung (the scale skipped it although `--b-space-md` exists and the card's own header pads with it) and
`--b-card-shadow` defaulting to `var(--b-shadow-sm)`, so a consumer can flatten one card without
neutralising `--b-shadow-sm` for everything else in scope. Both additive; Reps has opted into neither and
measures byte-identical.

The parts worth carrying past the component:

- **A gap can be correctly answered with "no", and the "no" is the deliverable.** A `layout`/`gap`
  attribute on `b-card` was rejected: a card is chrome, how its contents stack is the contents' business,
  and the need is not card-specific — the same stack is wanted in three *non-card* Reps surfaces, so a
  card-scoped answer helps none of them. The bar this repo's backports have met is *things a consumer got
  wrong or would rediscover the hard way*; a flex column is neither. Recorded with its reasoning in the
  task so it is not re-proposed as if unexplored.
- **Don't settle a framework-wide question as a side effect of a component tweak.** The refusal leaves a
  real complaint standing — "I cannot style into a shadow root" — whose platform answer is `::part`, which
  today appears in exactly one component (`b-sidebar`). Whether the catalogue commits to it is an API
  decision (an exposed part is a selector consumers write; renaming it breaks them silently at runtime),
  so it is filed as **TASK-106** in `tasks/_loose/` next to TASK-059, and no parts were added.
- **Assert against the live token, not a px literal.** The consumer check compared card padding to a probe
  carrying `var(--b-space-lg)`. Just as well: it computes to **14px**, not 16px, because the Birko reset
  sets `html { font-size: var(--b-text-base) }` = 14px, so every `rem` in the token scale resolves against
  a 14px root. A literal would have failed for the wrong reason — and would have kept passing if the scale
  were ever rescaled.
- **A back-compat check is only worth something if the fix-dependent ones can fail.** Removing both changes
  and re-running took the suite to 126/131: exactly the five fix-dependent checks broke, and the five
  back-compat ones stayed green. Without that split they would just be restatements of the fix.

## 2026-07-30 — `b-chart` small-chart axis, and a new home for gaps consumers find in the catalogue

Reps reported that `b-chart` reads busy at 90–150px — the sizes its Progress surface is entirely made of.
Fixed in the component (`Birko.Web.Components`, framework `TASK-104`; per-component detail in that repo's
CLAUDE.md): the y-tick count now follows the plot height (`tickIntervalsForHeight`, capped at 5 so the default
300px chart is unchanged) or an explicit `yAxis.ticks`; tick values snap to 1/2/2.5/5×10ⁿ (`niceScale`);
`showLatestValue` is a top-level option instead of something you could only reach by opting into `realTime`.

Three things worth carrying past this component:

- **A "label overlaps X" report is a z-order question before it is a coordinates question.** The write-up
  named the label's x coordinate, a halo shipped on that reading, and the screenshot of the real surface then
  showed a bar painted straight *through* the text — the label was emitted with its line, before the bars.
  Placement was never wrong.
- **Only the real surface found it.** The playground proved the component correct and the smoke was green;
  the defect needed a bar tall enough to reach the threshold, in the consumer's own data. Finish verification
  in the surface that reported the problem, not in the harness.
- **Decouple what varies for different reasons.** Deriving the *band* from the height-derived tick count made
  a 90px chart round an 11 357 peak up to 20 000 and draw its bars at 57% of an empty plot. The band is now
  rounded at a fixed density and only the labels thin out, so the same data lands on the same band at every
  size — same shape as the ribbon lesson that the scaling decision must not read the applied layout.

Tracking: **STORY-052** under EPIC-016 is the new home for gaps a consumer hits while *adopting* an existing
component — distinct from STORY-037/038, which are closed ledgers of capabilities moved upstream. The two have
opposite acceptance tests: a backport is done when the consumer can delete its copy, an adoption gap is done
when no consumer needs a fork or a special case.

## 2026-07-30 — `docs/specs/` exists: 25 capability specs harvested from code, and the 865 findings that fell out

`docs/specs/` now holds **25 cross-cutting capability specs** — 736 SHALL requirements and 2,426
Given/When/Then scenarios, generated from 648 source files at code HEAD `f3ac675`. **Spec bodies are
generated, not written**: `/specs regen` overwrites them, so a wrong statement is fixed by fixing the code or
the map, never by editing the `.md`. `docs/specs/.map.yml` (capability → source globs) is the only
human-owned file in that directory. Scope is deliberately **aggregator-only** — cross-cutting contracts
spanning several `Birko.*` sub-repos, with globs reaching out via `../Birko.X/...`; per-sub-repo
`docs/specs/` trees remain follow-up work, and 64 single-repo projects are named in the map's explicit
out-of-scope block so their absence is a decision rather than a silent gap.

**Areas include their backend implementors, not just the interface**, wherever the contract's whole point is
cross-backend conformance. That is a direct consequence of this family's bug history: the empty-`IN` and
dropped-ElasticSearch-clause defects were per-provider *divergence*, which a contract-only spec cannot see.
The uncapped sweep vindicated it — `RefreshAsync` validates its view name on ElasticSearch and silently
no-ops on the other four backends; MySQL inherits a `CREATE INDEX IF NOT EXISTS` and a table-less
`DROP INDEX` that it does not accept.

**Specs record actuality, defects included — which is what makes them find things.** Harvesting turned up
**865 findings (57 high)** under `tasks/EPIC-014-code-review-remediation`, with `SH-*` ids and one story per
severity mirroring the `CR-*` sweep — **STORY-051** (high, 11 tasks pre-created for the verified subset),
**STORY-053** (medium), **STORY-054** (low), **STORY-055** (the three unrated areas, whose findings were
swept but never written down). Distinct provenance from the `CR-*` audit, so nothing renumbers. Confirmed by
hand: `Pbkdf2PasswordHasher.Verify`
returns **`true` for any password** against a `PBKDF2-SHA512:600000::` column (empty string is valid Base64,
so the CR-M233 guard never fires and `FixedTimeEquals` compares two zero-length spans); a null or
silently-dropped filter renders **`DELETE FROM "T"`**; `long`/`double`/`float`/`short`/`byte[]` properties map
to **no column and never persist** (`decimal` is mapped, so money escapes); the tenant write guard compares a
**caller-settable** `item.TenantGuid` instead of the stored row; and ORDER BY keys are interpolated verbatim,
reachable via `OrderBy<T>.ByName(string)`. Note the ordering constraint this creates: **the specs currently
document these defects as shipped behaviour**, so any fix must be followed by a `/specs regen` of its area
with the spec diff reviewed — that diff is the fix's evidence.

Three rules worth keeping beyond this exercise:
- **A spec that silently picks a side is wrong.** `store-crud-contract` specced `Destroy()` with its
  destructive implementation meaning while the interface doc says "releases all resources" — but the
  *disagreement* was the defect, and recording only one side hid it. Where code and its own documentation
  contradict each other, spec both.
- **Verify before filing.** Of the 15 high findings checked by hand, 12 held exactly and **3 needed their
  scope corrected** — one named the wrong trigger entirely (the `IsNegated` claim: the unresolved-field
  branch returns *before* the negation, so only the non-string path degrades to match-all). Filing all 57
  unverified would have put three misleading tickets into EPIC-014.
- **Structured-output bounds silently shape results, in both directions.** A `maxItems: 8` on the
  finding array made 22 of 25 areas return exactly 8 — hiding roughly 90% of what was found, and looking like
  data rather than a ceiling. Later a 600-character per-item limit made one area fail its schema five times
  and be dropped entirely. A bound that is too small does not truncate a list, it loses the agent; make
  agents report exhaustion explicitly and treat a suspiciously round number as a cap, not a count.

## 2026-07-29 — Ribbon overflow: the ribbon body scales, it does not scroll — delivered in both skins

A field report — on a narrow window the ribbon shows fewer commands with no way to reach the rest — turned
out to be true in **both** skins, failing differently. `Birko.Xaml.Avalonia`'s `Ribbon` clipped its tab strip
*and* its groups row with no `ScrollViewer` at all. `b-ribbon` had working tab-strip chevrons but its panel
was `overflow-x: auto` with `scrollbar-width: none` and no buttons — scrollable in theory, invisible in
practice, so an overflowing group was unreachable by mouse. Its tab arrows also only re-evaluated on `scroll`
and on re-render, so narrowing the window left the arrow hidden while the tabs overflowed.

The **standing design rule** this establishes, recorded here because it constrains all future ribbon work:
the ribbon *body* **resizes, it never scrolls.** Office degrades each group `Large → Medium → Small → Popup`
in an author-declared priority order (`scalingPriority`), collapsing a whole group to one chunk button with a
full-size flyout rather than moving commands offscreen. A scroll offset destroys the spatial memory the ribbon
exists to provide ("Cut is top-left of Clipboard") — the exact failure the ribbon was invented to fix over
Office 2003's toolbars. The `»` overflow chevron *is* an Office pattern, but it belongs to **toolbars**
(Fluent `CommandBar`/`OverflowSet`), not the ribbon body. **Ribbon tabs are the deliberate exception** and do
scroll, as in Office Web / Fluent.

**Delivered (TASK-097 → TASK-100).** Both skins now scale: `Large` → `Medium` → `Small` → `Popup` (the whole
group as one chunk button with a flyout), plus a **compact** chunk that drops the group name at the extreme.
The groups row has **no scroller in either skin**; only the tab strip scrolls. `RibbonScaling` in
`Birko.Xaml.Core` owns the policy and `b-ribbon`'s `ribbon-scaling.ts` mirrors it, with the playground smoke
asserting the *same numeric table* as the C# unit tests so the two cannot drift. Default look is `Medium`
(what both skins already rendered, so no consumer's ribbon changed height); `Large`/`Small` are opt-in.

Two model-level decisions worth knowing beyond the ribbon:
- **`MinSize` is a preference, not a guarantee** — breached least-important-first rather than letting the row
  overflow. Unreachable commands are worse than a group being less legible than its author wanted.
- **The scaling decision must never be a function of the applied layout**, or it oscillates at a boundary.
  Proven the hard way: while the groups row still had a scroller, the scroll chevrons' hysteresis fed back
  into the width being scaled against and the same window width resolved differently depending on drag
  direction. Removing the scroller fixed it with no other change.

**The accessibility round found more than the layout round did (2026-07-30), and every defect was in a
control whose behaviour was already correct and tested.** The XAML skin was, in effect, unusable without a
mouse: **no ribbon button had an accessible name** (Avalonia derives one from `Content` only when it is a
*string*, and a ribbon item's content is a panel — so a screen reader was handed `"Avalonia.Controls.StackPanel"`,
or the bare glyph at `Small`); there is **no focus visual on `Button` anywhere** in the skin, which faked two
"Tab is broken" reports before anyone suspected styling; `Rebuild()` **destroyed focus**, so activating a tab
by keyboard threw the user out of the ribbon; and **arrow navigation did not exist at all**, while `b-ribbon`
had it in both the tab strip and the panel. `b-ribbon` had every one of these right — hence the rule now in
`Birko.Xaml.Avalonia/CLAUDE.md`: **when porting a web component, port its ARIA discipline too.** The XAML
equivalents (`AutomationProperties.Name`, `AccessibilityView.Raw`, an automation peer) all exist, and none of
them are automatic. The missing focus visual is framework-wide and is tracked as **TASK-103**.

Two lessons that generalise past the ribbon:
- **A non-empty accessible name is not a criterion.** `Content.ToString()` satisfies it, so the first version
  of that test passed with the fix removed. Assert the name is a string the *user* would recognise.
- **Some criteria cannot be signed off by hand, and saying so beats a false tick.** "An open flyout closes
  when its group is promoted" was ticked with no implementation behind it, and the manual step could never
  have caught that: dragging a window edge is a click outside the flyout, and snapping or moving it between
  monitors dismisses popups too. Three causes, one observation. Only a headless resize isolates it.

Review found five defects the automated suites had missed, four of them one species — **state that did not
survive a re-render** (an imperative CSS class wiped by a synchronous morph; chevron visibility that changed
*layout*, letting a tab swallow the click; a tab-strip scroll offset discarded by `Rebuild()`; flyout wiring
applied only in `onUpdated` while the measure pass re-rendered). The fifth was subtler and is now a documented
Avalonia gotcha: **measuring an `IsVisible = false` control yields zero**, so a panel that hid its
alternatives before measuring them under-degraded and clipped its last group — while the tests, which asserted
the *decision* rather than whether the row *fits*, stayed green. Per-skin detail lives in the two sub-project
CLAUDE.mds. Remaining Avalonia parity gaps are tracked as TASK-101 (pinned / temporary-reveal) and TASK-102
(narrow fallback — `b-ribbon` has a hamburger, the XAML ribbon has never had one).

## 2026-07-29 — Per-theme AXAML dictionaries — Avalonia themes are opt-in like the web ones

Themes were opt-in on the web (one CSS file each, linked + `registerThemes`) but all-or-nothing on
desktop: one 36 KB `Tokens.axaml` held all four `ThemeDictionaries`, and `BirkoTheme.axaml` pulled the
lot. `Birko.DesignTokens` now emits **one file per theme** — `Themes/Tokens.{Light,Dark,Neon,Finstat}.axaml`
plus a shared `Tokens.Brushes.axaml`, with `Tokens.axaml` kept as a back-compat aggregate merging all
five (existing consumers unaffected). New `BirkoTheme.Core.axaml` ships light+dark; extra themes are
merged per file. Core+extras is ~43% lighter than all-in (23 KB vs 41 KB).

Three Avalonia behaviours were spike-verified first and are now pinned by `ThemeCompositionTests`,
because the design rests on them: `ThemeDictionaries` entries **do** resolve from *merged*
dictionaries (so the split is possible at all); an omitted custom variant degrades to its
`InheritVariant` (so Neon/Finstat are safely omissible); **`ThemeVariant.Dark` has no
`InheritVariant`**, so a light-only app resolves *nothing* under OS dark mode — which is why core is
light+**dark**, not light alone.

Themes are **detected, not listed twice.** Each generated dictionary declares
`<x:String x:Key="BThemeId">` naming itself, and `AvaloniaThemeManager.DetectThemes(IResourceNode)`
reads it, so `Available` is derived from what was actually merged and the switcher can never offer a
theme whose tokens are missing. Presence probing cannot do this — an omitted variant inherits
silently and would answer anyway; only a value that names its dictionary distinguishes the cases.
This is the fix for the drift that bit the web side, where CSS-linking and `registerThemes` are two
lists nobody reconciles.

Also, while regenerating: **`tokens.json` was stale and `CssParityTests` had been red on main.**
Generated CSS had been hand-edited — `--b-color-danger-text` (a WCAG contrast token, all four
sheets), an AA-darkened finstat `--b-text-secondary`, `--b-modal-width-xxl`, `--b-modal-full-inset`,
`--b-drawer-width-xxl`, `--b-input-font-size` — so `generate` would have silently deleted all of it.
Recovered with `extract` (folds the live CSS back into the source and self-checks the round-trip);
CSS is byte-identical again and the AXAML now carries those tokens too. `verify` gained an **AXAML**
drift gate (it was CSS-only, so six generated dictionaries would have been unguarded). Three
`ShellChromeTests` were also converted `[Fact]`→`[AvaloniaFact]`: they built an `AvaloniaThemeManager`
with no `Application` and only passed when an earlier test happened to leave `Application.Current`
set.

Both gaps are now closed at the mechanism level rather than just fixed:
- **`AxamlParityTests`** gates the six generated dictionaries from the *suite* (each must equal what
  tokens.json regenerates, plus a check that `Themes/` holds exactly the generated set so a dropped
  dictionary can't linger). The CSS always had a suite gate; AXAML had only the `verify` CLI verb, and
  nothing runs a CLI verb by itself — which is precisely how the CSS drift went unnoticed. Verified to
  actually fail by perturbing a generated file.
- **Every one of the 32 Avalonia test classes now passes in isolation**, so order-dependence can no
  longer mask a broken test. Swept the whole suite for the same ambient-`Application` pattern; the
  five remaining plain `[Fact]`s are genuinely app-independent (pure VM/service logic).

Tests: `Birko.DesignTokens.Tests` 42 (was 18 green / 12 red), `Birko.Xaml.Avalonia.Tests` 144,
`Birko.Xaml.Core.Tests` 41.

## 2026-07-28 — X-Tenant-Id must agree with the JWT tenant claim

`Birko.Security.AspNetCore` gained **`TenantHeaderClaimGuardMiddleware`**, wired via
`UseBirkoTenantHeaderGuard()`. `HeaderTenantResolver` parsed `X-Tenant-Id` with no comparison to the
`tenant_id` claim — and *cannot* compare, because `TenantMiddleware` runs before `UseAuthentication()`, so
`context.User` is unpopulated there. In a typical app the header and the claim feed **different** consumers
(repository tenant scoping follows the header, permission resolution follows the token), so a caller could
authenticate in their own tenant, send `X-Tenant-Id: {victim}`, keep their home-tenant permissions and point
every tenant-scoped read **and write** at another tenant. Hence a separate post-authentication step, placed
after `UseAuthentication()`/`UseAuthorization()` and before anything that scopes by tenant; a mismatch returns
403 `Tenant.HeaderClaimMismatch`. **Secure by default** — `BirkoSecurityOptions.RequireTenantHeaderMatchesClaim
= true`; an opt-*in* guard was rejected because a check nobody knows to enable protects nobody. Deliberate
pass-throughs: no header (the claim is then the only source; SSE cannot set headers), unauthenticated
(login/register/setup), wildcard `*` holders (cross-tenant reach is intentional), unparseable header (resolves
to no tenant anyway). `BirkoSecurityOptions` is now registered as a singleton so middleware can read it.
Docs: [docs/security.md](docs/security.md#tenant-headerclaim-guard), [docs/tenant.md](docs/tenant.md).

## 2026-07-27 — Empty-set and enum filter translation fixed across SQL + ElasticSearch

Three defects in the same family — an operand the parser mis-read, and an empty collection with no explicit
case — each of which made a filter match the **wrong rows** rather than fail:
- **SQL, empty `IN`** — `InConditionStrategy` had no empty-set case and emitted `Col IN ()`. SQLite's grammar
  permits it (always-false), which hid the defect from the SQLite-backed suites; PostgreSQL and MSSQL reject
  it as a syntax error. Now renders set-faithful constants: empty `IN` → `1 = 0`, empty `NOT IN` → `1 = 1`
  ("not in the empty set" is true of every row — always-false there would silently invert the predicate). All
  four providers share the one strategy, so the single change covers them. `ParseConditionExpression` also
  stopped degrading an empty materialization to `IsNull` (a different wrong answer: rows with a NULL column).
- **SQL, `enumSet.Contains(x.EnumColumn)` matched zero rows** — on .NET 9+ an array `Contains` binds to
  `MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)` when `T` isn't `IEquatable<T>` (every
  enum), and the trailing `null` comparer was parsed as a value, flipping the condition to `IsNull`.
  `IsNonOperandArgument` now skips comparer / `StringComparison` / `CultureInfo` arguments (same family as the
  earlier `Contains(q, StringComparison…)` bug). Plus `NormalizeParameterValue` unwraps enums to their
  underlying integer in all four provider connectors — Npgsql rejects an unmapped CLR enum.
- **ElasticSearch, empty `Contains` DROPPED the clause** — `ParseContains` returned null and `CombineBool`
  drops nulls, so `ids.Contains(x.Field) && x.Status == active` with an empty `ids` silently became
  `x.Status == active`. Now `MatchNoneQuery` for both the empty and null collection (negation via `MustNot`
  gives every document — the same asymmetry as SQL's empty `NOT IN`).

Also **CR-H047 is now enforced at every ES filter→query boundary**, not just in `ElasticSearchViewStore`: the
entity stores assigned the parser's output straight to their requests across 14 sites, and a NEST request with
`Query = null` reads as match-all — so an untranslatable filter turned reads into "return everything" and
reached `_delete_by_query`/`_update_by_query` unguarded. Two shared helpers own the invariant:
`ParseFilterQuery` (optional filter — null filter means read-everything on purpose, untranslatable throws) and
`ParseRequiredFilterQuery` (the four destructive paths — a null filter throws). Three outcomes stay distinct
and only one is an error: no filter, matches-nothing (`MatchNoneQuery` — a legitimate translation), cannot be
expressed. Details: `Birko.Data.SQL` / `Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".

## 2026-07-21 — Doc-index registration is now a required, linted step

`Birko.EventBus.Tenant` shipped fully built and build-registered yet invisible in every human-facing
doc (README project table, `CLAUDE-projects.md`, `docs/event-bus.md`) — an audit found it was the only
project of 175+ missing from the doc index. Added it there, and closed the gap structurally so it can't
recur: new **`CLAUDE-maintenance.md` § "Documentation Index Registration"** makes doc-index membership a
required registration step distinct from build-file registration (`.slnx`/`.code-workspace`/`.csproj`
make it compile; the doc index makes it discoverable), and **`verify-conventions` check #7b** lints
new `.shproj` projects against all three index locations — including a full-repo drift sweep that catches
pre-existing gaps, not just the current diff.

## 2026-07-19 — Shared expression normalizer — ternary / `??` / column-arithmetic in SQL predicates

STORY-047 follow-up. Added `Birko.Data.Expressions.ExpressionNormalizer` (in `Birko.Data.Core`) — a
backend-agnostic pre-pass the hand-rolled store parsers run at the lambda boundary. It **funcletizes**
any parameter-free subtree to a constant (collapsing parameter-free ternary / `??` / arithmetic and all
closures) and **desugars** boolean-typed ternary `c ? t : f` → `(c && t) || (!c && f)` and boolean-typed
`a ?? b` → `(a == true) || (a == null && b)`, so predicate parsers only ever see AND/OR/NOT/comparisons.
Wired into `DataBase.ParseConditionExpression` (Where/Delete/Update predicates) and `DataBase.ParseExpression`
(value position). SQL parser additionally gained **value-expression operands in predicates** — column
arithmetic (`x.A + x.B > 5`, `x.Price * 2 >= 10`, `x.Total == x.A + x.B`, `x.Bonus % 2 == 0`),
null-coalescing (`(x.Score ?? 0) > 5`) and a value-position ternary compared to something
(`(x.Vip ? x.Premium : x.Score) > 100`, i.e. **CASE in WHERE**). The value side renders to a raw fragment
in `Condition.Name` (arithmetic / `COALESCE` / `CASE WHEN`), operator flipped when the value is on the left,
`IsField` for column-vs-column; fragment-internal constants are inlined as portable SQL literals (numeric /
bool→1/0 / enum→int / escaped string), with `NotSupportedException` for non-portable types (DateTime/Guid)
instead of a silent drop. Value position also gained `COALESCE` / `CASE WHEN` / `IS [NOT] NULL`.
**ElasticSearch adopted the same normalizer** (`ParseLambda` runs it first), so boolean ternary / `??`
desugar to AND/OR/NOT with no ES-specific code; ES **value-expression operands** (arithmetic / value-`??` /
value-CASE compared) now emit a guarded Painless `ScriptQuery` (`(existence-guard) ? (body) : false` so a
missing field excludes the doc, matching C#) instead of being dropped, throwing on non-scriptable shapes
(also fixed ES `ContainsParameter` to recurse into `ConditionalExpression`). The **live document-backend
matrices** (Mongo/Cosmos/Raven `FilterMatrixLiveTests`) gained ternary/`??`/arithmetic shapes to check the
driver LINQ translators against the oracle when a backend is present (env-gated no-op otherwise). Tests:
`ExpressionNormalizerTests` (Core, 12) + `SqlPredicateNormalizationTests` (SqLite, 24, oracle-compared) +
`ExpressionDivergenceTests` (+5 ES cases); existing `SqlExpressionParityTests` (23) and full
SQL/SqLite/Core/ES suites green (312 / 75 / 29 / 102).

## 2026-07-18 — Composite UNIQUE index support in attribute-driven SQL DDL

`[IndexedField(name, order, IsUnique: true)]` now emits a composite `CREATE UNIQUE INDEX` — the storage-level backstop for per-tenant uniqueness such as `(TenantGuid, Number)` (two tenants may each issue `FV2026000001`; the pair must still be unique). Additive and provider-wide:
- `IndexedField` gained `IsUnique` (default false); `Tables.IndexDefinition` gained `Unique`.
- `LoadIndexes` sets `idx.Unique` when any contributing `[IndexedField]` for that name is unique (both the direct-cast and cross-assembly reflection paths).
- `AbstractConnectorBase.CreateIndexSql` (all providers — SQLite/PostgreSQL/MySQL) and the `MSSqlConnector` override emit `CREATE UNIQUE INDEX` when `index.Unique`.
- **Class-level `[CompositeIndex("name", nameof(A), nameof(B), IsUnique = true)]`** (added next; `AttributeTargets.Class`, `Inherited = false`, `AllowMultiple`) declares a composite index whose columns may be **inherited from a base class** — the only safe way to form `(TenantGuid, Number)` when the tenant discriminator lives on a shared base type (per-property `[IndexedField]` on a base would land on every subclass table and collide on the DB-global index name). `LoadIndexes` resolves each property → column via the same fields map (so `[NamedField]`/ModelMap remaps and inherited props work) and fails fast at table-load on an unmapped property name. Reuses the existing `IndexDefinition.Unique` + `CreateIndexSql` — no connector changes.
- **Decision on the draft-empty case:** only a **full** unique index is emitted — partial/filtered unique indexes (`WHERE Number <> ''`, to allow multiple empty-string drafts) are **not** supported, because they are not portable (SQLite/PostgreSQL partial vs MSSQL filtered vs MySQL neither). Composite-unique therefore fits **always-populated** columns; columns left empty on drafts must rely on an application-level guarded allocator. Tests: `Birko.Data.SQL.Tests` `CompositeUniqueIndexTests` (DDL) + `Birko.Data.SQL.SqLite.Tests` `CompositeUniqueIndexEndToEndTests` (enforced end-to-end). Consumer follow-up tracked in Symbio `TASK-170`.

## 2026-07-18 — Filter-parser parity: SQL negated-group fix + ElasticSearch gaps closed + cross-backend tests

Audited the LINQ-`Expression` filter translators across backends. Fixed two hand-rolled parsers and added parity tests:
- **SQL correctness bug:** a negated **group** (`!(a && b)`, `!(a || b)`, or a negated comparison that became a sub-group) rendered as `(a AND b)` with the `NOT` **silently dropped** — the filter matched the opposite rows. Fixed in `AbstractConnectorBase.AppendSubConditionsTo` (prefix `NOT` + parenthesise negated groups).
- **ElasticSearch gaps** (separate commit): `EndsWith`/`ToLower`/IN-pattern threw, bitwise `&`/`|` was silently dropped, bare/const bool produced malformed queries — all brought to parity. See `Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".
- **Tests:** `SqlExpressionParityTests` (SQLite oracle) + `ExpressionDivergenceTests` (ES structure) cover comparisons, null, strings, IN, and complex nested grouping in-process; env-var-gated `*FilterMatrixLiveTests` (Mongo/Cosmos/Raven) verify the native LINQ/driver translators against a compiled-delegate oracle when a live backend is available (tracked in `tasks/EPIC-011 STORY-047`).

## 2026-07-18 — SQL translator: nullable-column `== nullVariable` now emits `IS NULL`

`Birko.Data.SQL` `ParseConditionExpression`: a predicate `x.Col == v` where `v` is a **closure
variable that holds null** compiled to `Col = NULL` (three-valued-logic UNKNOWN → silently zero rows),
whereas a literal `x.Col == null` correctly emitted `IS NULL`. The closure-member branch set
`Values=[null]` with `Type=Equal`; it now maps a null-valued variable to `ConditionType.IsNull` (and
`IS NOT NULL` for `!=`), mirroring the literal-null branch. Dangerous because the in-memory store
compiles the lambda (`null == null` → true), so unit/E2E tests pass while real SQL returns nothing —
surfaced by a Symbio audit where `x.VariantId == variantId` for variant-less products returned zero
rows (duplicate cart lines, false "no stock", missed dedup guards). Tests:
`Birko.Data.SQL.Tests` +3 (`NullEqualityTranslationTests`); full SQL suite 302 + SQLite integration 43 green.

## 2026-07-18 — WithAllTenants now spans all tenants on reads (even with a tenant set)

Follow-up to EPIC-017. `WithAllTenants(...)` bypassed the Strict no-tenant *throw* and the write-authorization guards (`BelongsToCurrentTenant` / `SetTenantGuidIfNeeded` both special-case `IsAllTenantsScope`), but the **read** seam did not: `TenantFilter` unconditionally built `ModelByTenant(CurrentTenantGuid, …)`. So from within a request scope (a tenant set), `WithAllTenants` reads/counts silently stayed scoped to the ambient tenant — contradicting its documented "operate across tenants on purpose" intent. Back-office/maintenance code reading global reference data (`TenantGuid == Guid.Empty`) or other tenants' rows saw only its own tenant. Fixed in both the async and sync `TenantFilter` (the STORY-044 single seam): while `IsAllTenantsScope` is active the effective tenant is treated as null, so reads span all tenants. `CurrentTenantGuid` is unchanged, so a nested `WithTenant(...)` used purely for event attribution still stamps that tenant. Surfaced by the Symbio Pricing "global FX rate + per-tenant override" model (needs to read global + own rows under Strict). Tests: `Birko.Data.Tenant.Tests` +1 (`Read_InsideAllTenantsScope_SpansAllTenants_EvenWithTenantSet`); adjacent tenant suites (Composition/Sync.Tenant/EventBus.Tenant/InMemory) green.

## 2026-07-17 — Tenant isolation hardening — EPIC-017

Multi-tenant fail-closed support across the data + event layers (from a Symbio security review):
- **`TenantIsolationMode { Permissive (default), Strict }`** in `Birko.Data.Tenant` (STORY-044) — Strict throws on "no tenant in scope" instead of silently spanning all tenants; opt in via `StoreWrapperBuilder.Build(tenantMode:)`, `AsTenantAware(mode:)`, or the `AddTenant*Repository` DI extensions. Explicit cross-tenant admin via `ITenantContext.WithAllTenants(...)` (non-breaking default interface methods). Both async **and** sync wrappers are mode-aware.
- **`StoreWrapperBuilder` decorator reorder** (STORY-045) — Tenant now sits *inside* Default/Sluggable/SoftDelete but *outside* Audit/Timestamp/EventSourcing, so per-tenant uniqueness probes are tenant-scoped (fixed a cross-tenant default-clobber + global-slug-uniqueness leak).
- **`IEventScopeAccessor`** in `Birko.EventBus` + `OutboxProcessor` scope restoration (STORY-046) — background event dispatch re-establishes the tenant an event was published under, so handlers work under Strict. New **`Birko.EventBus.Tenant`** bridge sibling implements both halves via `AddEventTenantScope()`: a publish-side `TenantEventEnricher` stamps `EventContext.TenantGuid` from the ambient `Tenant.Current` (so `OutboxEntry.TenantGuid` is correct for HTTP, jobs, and explicit `WithTenant` scopes — not just authenticated HTTP), and a consume-side `TenantEventScopeAccessor` restores it (`WithTenantAsync` / `WithAllTenants`) before handlers run.

## 2026-07-14 — Birko.Helpers.PathValidator.ValidateDirectory — reject only path-invalid chars (consumer backport)

`Birko.Helpers.PathValidator.ValidateDirectory` checked the **whole** directory path against `Path.GetInvalidPathChars()` **plus** `Path.GetInvalidFileNameChars()` — but the file-name set additionally includes the directory separators (`\`/`/`) and the drive-letter `:` on Windows, so it threw `ArgumentException` on **every absolute Windows path** (e.g. `C:\Users\…`). A directory path legitimately contains separators and a drive colon; the method now checks against `Path.GetInvalidPathChars()` only (control chars etc.), and `Path.GetFullPath` still rejects truly-malformed paths. Surfaced by a consumer app. Tests: `Birko.Helpers.Tests` 88 → 92 (`ValidateDirectory` accepts an absolute path + separators/drive-colon, rejects a NUL control char, rejects null/empty).

## 2026-07-14 — BardStudio consumer backports — SQL/Xaml/Migrations/AI fixes

Backported seven framework fixes surfaced while building the BardStudio consumer (see `Consumers/BardStudio/docs/framework-backports-prompt.md`).
- **SQL translator (`Birko.Data.SQL/SQL/DataBase.cs`)** — `string.Contains/StartsWith/EndsWith(value, StringComparison)` corrupted the LIKE pattern: the argument loop fed EVERY arg into the condition value, so the trailing `StringComparison` enum (e.g. 5) overwrote the search string → `Title LIKE '%5%'`. String-pattern methods now use only the first argument; culture/comparison args carry no SQL operand and are ignored (case-insensitivity delegated to DB collation — SQLite LIKE is already ASCII-insensitive).
- **Avalonia Button (`Controls/Buttons.axaml`)** — the template now binds `BorderBrush`/`BorderThickness` (consumer borders rendered as none before), and hover/pressed set the Button's own `Background` property (was set directly on `PART_ContentPresenter`, outranking every consumer style) so consumer styles override normally.
- **Avalonia TabItem (`Controls/Surfaces.axaml`)** — `PART_Border` binds `{TemplateBinding Background}` (default Transparent via setter) instead of a hardcoded `Transparent`, so a consumer's `:selected`/`:pointerover` Background paints.
- **Avalonia ComboBox (`Controls/Inputs.axaml`)** — obsolete `PlacementMode` → `Placement` (clears AVLN5001 on every consumer build).
- **`SqlScriptMigration` (new, `Birko.Data.Migrations.SQL`)** — raw-SQL migration base: `UpSql` (required) + optional `DownSql`, runs each against the migration context's `Connection`/`Transaction`; consumers no longer cast `IMigrationContext`→`SqlMigrationContext` or hand-roll plumbing.
- **`Message.GetText()` / `MessageText.From` (new, `Birko.AI.Contracts`)** — canonical text accessor for `Message.Content` (an `object?` — string for user turns, `List<ContentBlock>` for assistant turns); replaces the `Content is string` cast that stringified a block list to a CLR type name.
- **Nested-projitems MSB4011 (#5) — interim "middle option"** — the three nested projitems imports (`Data.Stores`→`Configuration`, `Data.Core`→`Contracts`, `Time`→`Time.Abstractions`) now carry an **include-guard sentinel**: each leaf sets a `Birko*ProjitemsImported` property and each parent conditions its nested import on it, so an aggregator listing the full closure no longer double-imports (MSB4011 gone) while a parent-only import still pulls the leaf. Keeps the documented free-closure architecture; the permanent convention decision (keep guards vs. flip to "shared projects never import shared projects") is tracked in `tasks/_loose/TASK-059`.
- Suites green: SQL 289 (+4), Migrations.SQL 34 (+6, **0 MSB4011**), AI 27 (+7), Xaml.Avalonia 134 (+4). Not committed — pending review.

## 2026-07-14 — SqLiteConnector AUTOINCREMENT DDL fix — closes CR-M166 offline (TASK-058)

`SqLiteConnector.FieldDefinition` appended a bare `AUTOINCREMENT` after any other constraints for **any** `IsAutoincrement` field — invalid SQLite: the keyword is only legal as part of an `INTEGER PRIMARY KEY AUTOINCREMENT` constraint (adjacent to PRIMARY KEY, type exactly `INTEGER`), and never on a non-PK column. So a **dual-key** model (`[PrimaryField] Guid` + a separate non-PK `[IncrementField] Id`, e.g. `SqlSyncKnowledgeItem`) made `CreateTable` throw a syntax error. Fixed ([TASK-058](tasks/_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md), option (a)): an autoincrement **PK** now emits `INTEGER PRIMARY KEY AUTOINCREMENT` as one adjacent clause; a non-PK `[IncrementField]` emits a **plain column** (SQLite has no per-column auto-increment outside the PK — the caller assigns the value; MSSql `IDENTITY` / PostgreSQL `SERIAL` differ, documented inline). **Verified offline (no Docker):** `Birko.Data.SQL.SqLite.Tests` 24 (no regression); the SQL sync-store CRUD round-trip now runs on a real SQLite `.db` (`Birko.Data.Sync.Sql.Tests` 2 → 6). This **closed CR-M166 offline** and removed it from the STORY-028 Docker pile → STORY-026 **267/275**, STORY-028 down to **8** findings.

## 2026-07-14 — Code-review remediation — STORY-027 low findings (EPIC-014)

Worked the 418 low-severity code-review findings from EPIC-014 in ~30 batches by project cluster (Birko.AI → BackgroundJobs → Caching → the whole Communication cluster → the Data.* storage backends → the SQL provider/view family → Data.Sync). Reached **209/418**; every batch ran `/code-review` clean. This entry replaces the ~30 per-batch notes that previously lived in `CLAUDE.md` § Recent Updates — the per-finding record (CR-L001 … CR-L209, with the fix + tests for each) lives in `tasks/EPIC-014-code-review-remediation/STORY-027-low-findings`. Breaking API changes surfaced by this story are recorded as their own entries (e.g. `IPort : IDisposable` below).

## 2026-07-13 — Code-review remediation — STORY-026 medium findings (EPIC-014)

Worked the 275 medium-severity code-review findings from EPIC-014 in ~60 batches spanning every project — Birko.AI, BackgroundJobs, Caching, the full Communication cluster, the Data.* storage backends (CosmosDB / ElasticSearch / InfluxDB / MongoDB / RavenDB / the SQL family / JSON / XML / InMemory), Migrations, Models (+ the SQL mapping siblings), MessageQueue, EventBus, Security, Serialization, Storage, Structures, Telemetry, Workflow, and the TypeScript Birko.Web.* track (verified via the Playground headless build — no in-framework unit runner by design). Reached **267/275**; the remaining **8** need Docker / a live server and are tracked as STORY-028. This entry replaces the ~60 per-batch notes previously in `CLAUDE.md` § Recent Updates — the per-finding record (CR-M001 … CR-M275) lives in `tasks/EPIC-014-code-review-remediation/STORY-026-medium-findings`.

## 2026-07-14 — Birko.Communication.Ports — BREAKING: `IPort : IDisposable`, `InvokeProcessData` protected (CR-L042/L043)
Two public-API changes landed with the STORY-027 low-findings sweep. **No in-tree consumer is affected** (a scan of `Consumers/` found no source implementing `IPort` directly nor calling `InvokeProcessData`), but they are breaking for **external** consumers, so recording them here.
- **`IPort` now extends `IDisposable`** (CR-L043). Every port is disposable (`AbstractPort.Dispose()` calls `Close()`), so consumers can use `using`. **Impact:** any type implementing `IPort` **directly** (not via `AbstractPort`) must now provide a `Dispose()`. Types deriving from `AbstractPort` inherit it and are unaffected; the three ports that already had their own `Dispose` (Serial, NfcReaderPort, BluetoothLE) became `override`. The one in-tree direct implementer, the Modbus test `MockPort`, was updated.
- **`AbstractPort.InvokeProcessData()` is now `protected`** (was `public`) (CR-L042). It fires the `OnProcessData` notification internally from a derived port after it processes data — it was never part of the `IPort` contract. **Impact:** external code calling `InvokeProcessData()` on a concrete port instance won't compile; derived ports (which is every caller in-tree) are unaffected.

## 2026-07-08 — Birko.Xaml — imperative dialog service (web `dialogs` backport)
Backported the web `birko-web-components/dialogs` helper (Reps TASK-062/063) into the XAML skin: an imperative, awaitable dialog API so a view-model calls a function instead of hand-wiring a `Modal` + buttons per screen.
- **`Birko.Xaml.Core/Dialogs/IDialogService`** (Avalonia-free — Core constraint #1) + option/enum models (`ConfirmOptions`, `PromptOptions`, `ChooseOption<T>`, `DialogVariant`, `NotifyVariant`). Eight members mirroring the web: `ConfirmAsync` / `ConfirmDeleteAsync` (→`Task<bool>`), `AlertAsync`, `PromptAsync` (→`Task<string?>`), `ChooseAsync<T>` (→`Task<T?>`), `PromptFormAsync<T>` (model-based, not a value dict — matches the XAML `Form` which two-way binds a POCO → returns the mutated model or `null`), `BusyAsync<T>`/`BusyAsync`, and `Notify`.
- **`Birko.Xaml.Avalonia/Dialogs/DialogService`** renders each as a token-styled `Modal` (reused) / spinner / toast added to a host `Panel` that spans the window (supplied via a `Func<Panel?>` provider, so a VM holds only the Avalonia-free interface). Confirm/prompt/choose/promptForm reuse `Modal`; `busy` is a non-dismissable `BusySpinner` overlay; `notify` stacks transient token-colored toasts (auto-dismiss). Localized via `II18n` (`bxaml.dialog.*`) with English fallbacks.
- **Danger/secondary buttons applied here** via token brushes (`BColorDangerBrush` / `BColorSecondaryBrush`) — the framework ships only a primary `Button` theme.
- **Gotcha fixed:** `Dismiss()` flips `Modal.IsOpen=false`, which re-enters the backdrop-cancel handler — so the result must be set *before* dismissing (guard on `TaskCompletionSource.TrySetResult`'s return), else the cancel value clobbers a positive answer.
- **Tests:** `DialogServiceTests` (Avalonia suite **119→129**) — confirm/cancel, danger+delete defaults (background bound to the danger token), alert removal, prompt value/required-block/Escape, choose selection, promptForm save-returns-model / cancel-null, busy overlay-then-remove, notify toast, + a rendered screenshot. Core stays **41** (the Avalonia-free guard still holds). **Gallery visual sign-off deferred** (needs a desktop session; behaviour + a headless screenshot cover it).

## 2026-07-06 — Birko.Communication.AspNetCore — docs backfill + workspace registration
The `Birko.Communication.AspNetCore` project (owner-scoped minimal-API CRUD helpers) had shipped its code + tests but was missing the mandatory per-project docs and its `.code-workspace` folder entries. Backfilled to convention — no code change.
- **Per-project docs added** — `README.md`, `CLAUDE.md`, `License.md` (the standard trio every sibling carries), documenting `MapOwnedCrud<TModel,TRequest,TRepo>`, the host-free `OwnedCrudResults` guards (404-absent/foreign, 409-own-vs-404-foreign id, PUT/DELETE gate), and the per-resource `OwnedCrudMapping` delegate set. Requires the `Microsoft.AspNetCore.App` shared framework in the host (like `.gRPC.Server`).
- **Root registration** — added to `README.md` (Communication table), `CLAUDE-projects.md` (Communication list), `docs/communication.md` (new section), and `Birko.Framework.code-workspace` (project + `.Tests` folders; it was already in `Birko.Framework.slnx`).

## 2026-07-06 — Birko.Xaml — clickable Breadcrumb (web b-breadcrumb parity)
The Avalonia `Breadcrumb` was display-only (static `TextBlock`s from an `IEnumerable` of arbitrary objects) while the web `b-breadcrumb` renders non-last crumbs as `<a href>` links. Brought them to parity.
- **`Navigation.BreadcrumbItem`** (`Birko.Xaml.Core`, Avalonia-free) — the crumb model (`Label` + optional `Href` / `Run`), mirroring the web `{ label, href? }` items.
- **`Breadcrumb` control** (`Birko.Xaml.Avalonia`) — `ItemsSource` now accepts plain values (static text, **backward compatible** — strings still render as before) **or** `BreadcrumbItem`s. A non-last item carrying a `Run` action or an `Href` renders as a clickable text link; clicking invokes `Run` and raises a new `ItemInvoked` event (so a shell can route on `Href`). The **last** crumb is the current location and is never clickable (exact web behaviour).
- **`BBreadcrumbLink` ControlTheme** (`Controls/Nav.axaml`, merged by `Controls.axaml`) — a chrome-free `Button` styled as a link: token-driven secondary text that turns primary + underlined on hover (web `a` / `a:hover`), disabled-opacity aware. Resolved in code via `Application.Current.TryGetResource` (falls back to a plain clickable button if absent).
- **Tests:** Avalonia suite **116→119** (`Tier1TailTests`: links render only for non-last items with a target; click fires `Run` + `ItemInvoked` carrying the item/`Href`; last item is never a link). Core stays Avalonia-free (suite 41 — `BreadcrumbItem` is pure model).

## 2026-07-06 — Birko.Xaml — Form MultiSelect / Tags / File field types (EPIC-015 / TASK-057)
The remaining `b-form` field types that needed new Xaml controls — closes the field-type parity gap entirely. [tasks/EPIC-015/TASK-057](tasks/EPIC-015-birko-xaml-ui-framework/TASK-057-xaml-form-multiselect-tags-file.md).
- **MultiSelect** → a multi-`ListBox` (`SelectionMode=Multiple|Toggle`) over `Options`, synced to an `IList` model prop on SelectionChanged (SelectedItems isn't bindable); initial model selection reflected.
- **Tags** → a `WrapPanel` chip input over an `IList<string>` (seeded when null): Enter adds, ✕/Backspace removes, chips re-render (token-styled).
- **File** → a read-only path `TextBox` (bound to a `string`) + a Browse `Button` via `TopLevel.StorageProvider.OpenFilePickerAsync` (no-op headless/unsupported).
- Core stays Avalonia-free (FieldType neutral; controls in the Avalonia `Form`). Avalonia suite **111→116** (MultiSelect/Tags bind, File render, screenshot); gallery `DemoForm` exercises all three; screenshot-verified. **`FieldType` is now 21** — full parity with `b-form` (the File OS-dialog pick is the one manual check). EPIC-015 complete.

## 2026-07-06 — Birko.Web.Components — b-range vertical orientation (equalizer) (EPIC-001 / TASK-053)
The web counterpart to the Xaml Slider work: `b-range` gained a vertical mode so it can stack into an equalizer/mixer. [tasks/EPIC-001/TASK-053](tasks/EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md).
- **`orientation` attribute** (`horizontal` default | `vertical`). Vertical: native input via `writing-mode: vertical-lr` + `direction: rtl` (up = more); the custom rail/fill overlay geometry switches to bottom/height (`_fillStyle` + `_updateFill` branch). Horizontal path untouched.
- **Layout fix:** a vertical `<input type=range>` has a huge (~600px) vertical min-content height that blew out the container; positioning it `absolute` (fills the definite slider box) makes sizing uniform.
- Playground `pg-equalizer` demo (5 slider-only vertical ranges) + README updated. Screenshot-verified in headless Chromium (grey rail, primary fill bottom→thumb, thumbs at their values). Web has no unit runner (TASK-052), so verification is verify.mjs + the screenshot.

## 2026-07-06 — Birko.Xaml — date/time picker field types; EPIC-015 complete again (TASK-056)
The last EPIC-015 gap: the Form had no date/time inputs. [tasks/EPIC-015/TASK-056](tasks/EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md).
- **`FieldType` +4:** `Date` (→`CalendarDatePicker.SelectedDate`), `Time` (→`TimePicker.SelectedTime`), `DateTime` (a date+time composite; handlers recombine into one `DateTime?`), `DateRange` (two pickers over a shared `Forms.DateRange` value class, seeded when the model prop is null). Core stays Avalonia-free (the composites/handlers live in the Avalonia `Form`).
- **Light restyle:** `CalendarDatePicker` + `TimePicker` ControlThemes `BasedOn` Fluent's + Birko token setters on the resting surface (border/bg/radius/font). Verified to resolve at runtime (whole theme loads). The flyout calendar/clock keep Fluent internals — full grid re-theming is a deferred follow-up. Native pickers are culture-aware automatically (screenshot: `5. 1. 2026`).
- **Tests:** Avalonia suite **106→111** (Date/Time bind, DateTime combine, DateRange seed+write, datetime screenshot). Gallery `DemoForm` now exercises all four.
- **EPIC-015 is complete again** (TASK-054/055/056 done) — the field-type/Slider follow-ups that reopened it are all landed.

## 2026-07-06 — Birko.Xaml — Slider control + Range field type (EPIC-015 / TASK-054)
Closed the Tier-1 `Slider` gap and wired a `Range` form field — the vertical/equalizer slider the review surfaced. [tasks/EPIC-015/TASK-054](tasks/EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md).
- **Token-restyled `Slider` ControlTheme** (`Inputs.axaml`), horizontal **and** vertical. Custom template: a rail `Border` + a `Track` laying out DecreaseButton (primary fill) | `Thumb` (white circle, primary border) | transparent IncreaseButton; cross-axis thickness/alignment set per orientation via `:horizontal`/`:vertical` styles on the named parts (one template, both orientations). Sub-themes `BSliderRepeat` / `BSliderThumb`. All `{DynamicResource B*}`.
- **`Forms.FieldType.Range`** — `Form` renders it as a `Slider` (Min/Max, `Step`→SmallChange+TickFrequency+snap), `RangeBase.Value` two-way bound to the model. (`Min`/`Max`/`Step` came from TASK-055.)
- **Tests:** Avalonia suite **102→106** — Range-field bind, `Slider_uses_the_birko_template` (both orientations, rail+thumb present), + an equalizer screenshot. Gallery Controls tab shows a horizontal slider + a 6-up vertical equalizer bank; verified visually (grey rail, primary fill bottom→thumb).

## 2026-07-06 — Birko.Xaml — Form field-type parity with b-form (EPIC-015 / TASK-055)
The schema-driven Xaml `Form` supported only 5 field types (Text/TextArea/Number/Checkbox/Select) vs `b-form`'s ~22; this lands the "cheap half" — the types whose restyled Avalonia control already exists. [tasks/EPIC-015/TASK-055](tasks/EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md).
- **`FieldType` +8:** `Switch` (→ToggleSwitch), `Markdown` (→MarkdownEditor), `Password` (TextBox `PasswordChar`), `Email` / `Search` (TextBox — semantic), `Percent` (numeric TextBox), `Radio` / `OptionGroup` (RadioButton group, vertical/horizontal). `Form.cs` grew the render branches; a radio group two-way-binds each button to the model via an equality converter (checked ⇔ value == option; only the newly-checked one writes back).
- **`FormField` +5 props (Core, neutral):** `Min` / `Max` (Number/Percent clamp on commit) · `Step` (carried for slider/spinner consumers — TASK-054) · `Default` (seeds the model property when null at bind time) · `Hint` (muted helper text rendered under the field).
- **Tests:** the `Form` control is Avalonia-side, so tests live in `Birko.Xaml.Avalonia.Tests` (`FormFieldTypesTests`, 8 headless) — Avalonia suite **94→102**; Core stays Avalonia-free (suite 41). Gallery `DemoForm` now shows Email/Password/Switch/Radio/Number+Hint.
- **Deferred (own tasks):** Range slider (TASK-054), date/time pickers (TASK-056), and MultiSelect/Tags/File (new controls).

## 2026-07-06 — Birko.Xaml — offline mirror + device utils (EPIC-016 / STORY-040 P3)
Closed STORY-040 (6/6) with the offline/device trio, ported as **neutral abstractions + desktop impls** (mobile backends slot in once `Birko.Xaml.Avalonia` targets mobile TFMs). [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/STORY.md).
- **`Data.MirrorDataSource<T>`** (`Birko.Xaml.Core`, Avalonia-free) — network-first read-through over the `ICrudDataSource<T>` port (the web `MirrorStore`/`readThrough` analogue): reads try the remote and refresh a local mirror; on failure fall back to the mirror; a remote 404 evicts the stale entry; writes pass through remote→mirror. Exposes an observable `Status` (`SyncStatus` Synced/Syncing/Offline). Caller supplies an `idOf` selector (the port has no id accessor).
- **`Controls.SyncStatusIndicator`** (`Birko.Xaml.Avalonia`) — the `<b-sync-status>` analogue; a `ContentControl` whose `Status` styled property drives localized text (`bxaml.sync.*`, English fallback) + a status class + token foreground (success/warning/danger). Binds straight to `MirrorDataSource.Status`.
- **`Device.IWakeLock` / `IAudioCue`** (`Birko.Xaml.Core`) + **`AvaloniaWakeLock` / `AvaloniaAudioCue`** (`Birko.Xaml.Avalonia`) — screen wake-lock (desktop no-op tracking `IsActive`, `AcquireCore`/`ReleaseCore` hooks for mobile) and a beep+vibrate cue (`Console.Beep` on Windows off the UI thread, no-op elsewhere; never throws). The web audio-cue's gesture-unlocked `AudioContext`/iOS priming is documented as web-only (doesn't port).
- **Tests:** Core suite **37→41** (`MirrorDataSourceTests`: passthrough / offline-fallback / 404-eviction / upsert), Avalonia suite **90→94** (`WakeLockTests`, `AudioCueTests`, `SyncStatusIndicatorTests`). Offline/online/evict behaviour is exercised via a togglable fake remote; mobile-device verification (real wake-lock, audible tone/vibration) is inherently deferred to the mobile-TFM work.

## 2026-07-06 — FIX: MSSqlStore.SetSettings was lossy (EPIC-016 / TASK-051)
Bug found while building TASK-042: the **sync** `MSSqlStore<T>.SetSettings(RemoteSettings)` rebuilt a `PasswordSettings` keeping only Location/Name/Password — dropping UserName/Port/MultipleActiveResultSets/TrustServerCertificate — so the store's connector authenticated with no user id and ignored the SQL-Server flags. (Its pre-existing `SetSettings(PasswordSettings)` override redirected *into* that lossy method, so both entry points were affected.) [tasks/EPIC-016/STORY-039/TASK-051](tasks/EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md).
- Fix: `SetSettings(RemoteSettings)` now passes the full settings through (`base.SetSettings((ISettings)settings)`), matching `AsyncMSSqlStore` and the MySQL/PostgreSQL sync stores. MySQL/PostgreSQL were already correct — no change.
- Regression test in `Birko.Data.SQL.Providers.Tests` (now **8**): after `SetSettings` with a full `MSSqlSettings`, the store's connector retains `User ID=…` + `MultipleActiveResultSets=True` (and is an `MSSqlSettings`, not a narrowed `PasswordSettings`).

## 2026-07-06 — Cross-provider SQL store-factory + DI (EPIC-016 / TASK-042)
Backported SQLite's store-factory + `IServiceCollection` wiring (TASK-033) to the other three SQL providers, so a server-DB host gets the same one-line setup. [tasks/EPIC-016/STORY-039](tasks/EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md).
- **`Add{MSSql,MySql,PostgreSql}Stores(...)`** + `{P}StoreFactory` / `I{P}StoreFactory` / `{P}StoreFactoryOptions` in `Birko.Data.SQL.{MSSql,MySQL,PostgreSQL}` (Stores/ + Extensions/). Options carry server/db/user/port/UseSecure/CommandTimeout + the provider flag (MSSql MARS+TrustServerCertificate / MySQL BulkInsertBatchSize / PG UseBinaryImport); the factory builds one shared `{P}Settings` and exposes `GetAsyncStore<T>()` + `GetConnector()`. No file-path logic (that's SQLite-only). Registered in each `.projitems`.
- **Factory hands out the *async* store** — `MSSqlStore<T>.SetSettings` is lossy (drops UserName/Port/flags, keeping only Location/Name/Password); `AsyncMSSqlStore` passes full settings. Fixing the sync store is a flagged follow-up.
- **`Birko.Data.SQL.Providers.Tests`** (new, registered in `.slnx` + `.code-workspace`) — 7 guarded tests: per-provider factory/settings/connection-string + `AddXStores` singleton resolution run offline; the **live CRUD round-trip is env-gated** (`BIRKO_{PROV}_TEST`) and skipped until a server is supplied. Task stays `review` pending that run.
- The other two backend backports were confirmed already-shared (create-tables migration + migration-transaction fix live in `Birko.Data.Migrations.SQL`, inherited by all SQL providers), so no work was needed there.

## 2026-07-06 — BMobileAppShell showcased in Playground + Gallery (EPIC-016 / STORY-041)
Reference-surface demos for the mobile shell, so it's discoverable, not just library code. Consumer-side (no framework edits). [tasks/EPIC-016/STORY-041](tasks/EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/STORY.md).
- **Birko.Web.Playground (TASK-049, done)** — a playground-local `BMobileAppShell` subclass registered as `pg-mobile-shell` (surfaces Home/Log/Stats), shown phone-framed in the Navigation section. Headless-verified: `node verify.mjs` renders it non-empty with zero page errors; `backport-smoke` asserts one nav item per surface + hash-driven active state.
- **Birko.Xaml.Gallery (TASK-050, review)** — a "Mobile shell" tab hosting `MobileShellView` (3 surfaces) in a phone frame; gallery builds clean. Run-the-app visual sign-off pending (needs a desktop session).

## 2026-07-06 — Birko.Xaml.Shell — mobile app-shell (BMobileAppShell equivalent) (EPIC-016 / TASK-043)
The flagship Web→Xaml backport (EPIC-016, from Reps): the Xaml family shipped only desktop *sidebar* + *ribbon* shells; this adds the **mobile** chrome the web already had. [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md).
- **`Views/MobileShellView`** (`Birko.Xaml.Shell`) — fixed top-bar (active-surface title + theme switcher), scrolling content region (same `ViewLocator` + `CrossFade` as the other shells), and a fixed **bottom-nav** (one item per surface, `UniformGrid Rows=1`). Binds the **same `ShellViewModel`** as the desktop shells — no new shell VM.
- **`MobileNavItem` + `ShellViewModel.NavItems`** (`Birko.Xaml.Core`, Avalonia-free) — the bottom-nav model is a *projection* of the existing `ModuleDefinition` list (the review's point that the web `Surface[]` maps 1:1 onto `ModuleDefinition`), adding only an observable `IsActive`. `ShellViewModel` refreshes active state on `Navigated`; the active item highlights primary via `Classes.active`. **No new nav-model type invented.**
- **Safe-area insets** — code-behind reads the platform `IInsetsManager` and pads the top-bar (notch/status-bar) + bottom-nav (home indicator); on desktop/headless the manager is null → no-op. Deliberately keeps `Birko.Xaml.Avalonia` on `net8.0` (mobile TFMs are a separate precondition tracked in STORY-040).
- **Tests** — 6 Core VM tests (`MobileShellViewModelTests`: NavItems projection + active-surface tracking on navigate/back/command) + 4 headless render tests (`MobileShellTests`: one nav item per surface, active page via ViewLocator, surface switch, screenshot). Core suite **31→37**, Avalonia suite **→90**. Screenshot-verified at a 390×780 phone viewport (fixed top-bar + content + bottom-nav, active Home highlighted).

## 2026-07-06 — Birko.Xaml.Core — locale-aware Formatter (EPIC-016 / TASK-044)
First of the **Web→Xaml backports from Reps** (EPIC-016, migrated from the WorkoutTracker consumer). `Birko.Xaml.Core` had translation-only `I18n` but no formatter; this adds the XAML analogue of Birko.Web.Core's `createFormatter`. [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md).
- **`Localization.IFormatter` / `Formatter`** — bound to an `II18n`; resolves `CultureInfo` from the active `Locale` at call time, so a `SetLocale` reflows formatting with no re-wiring. Avalonia-free (only `System.Globalization`).
- **`Duration(totalSeconds, alwaysHours=false)`** → `m:ss` / `h:mm:ss`, negatives clamp to `0:00`, fractional seconds floored — **byte-for-byte parity** with the web `duration()` (ported verbatim, locale-independent).
- **Culture-aware** `Date` (Short/Long/Full via `DateStyle`; Long strips the weekday to mirror Intl `{day, month:'long', year}`), `Time`, `DateTime`, `Number` (null decimals → ≤3 fraction digits), `Currency` (symbol driven by the currency **code**, not the culture, matching Intl), `Percent` (0–100 input).
- **16 xUnit + FluentAssertions tests** (`Birko.Xaml.Core.Tests/FormatterTests.cs`) — duration boundaries/clamp/floor + locale-independence, en-US vs de-DE separator parity for Number/Currency, code-driven currency symbol, Full-vs-Long weekday, live locale switch, and unknown-locale → invariant fallback. Core suite now **31**.

## 2026-07-06 — EPIC-015 (Birko.Xaml) complete — shell polish closes STORY-036
Final polish, closing STORY-036 and the whole **EPIC-015**. [tasks/EPIC-015](tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md).
- **RibbonShellView** (`BAppShell`): a second shell chrome — a `Ribbon` (bound to `ShellViewModel.RibbonTabs`) over the content region + status bar, in place of the sidebar, on the same `ShellViewModel` (+ Ctrl+K palette). Screenshot-verified (ribbon tabs/groups + ListPage content).
- **Cross-fade page transitions**: both shells' content regions use a `TransitioningContentControl` (`CrossFade`) so navigation fades.
- **`ListBoxItem` restyle** (`Controls/Lists.axaml`): token hover/selected — improves the command palette, split-page list, kanban.
- Avalonia suite now **84**; CSS parity clean.
- **EPIC-015 done (8/8 stories):** single-source design tokens (byte-identical web CSS + Avalonia AXAML), the theme system (4 variants, runtime swap), Avalonia-free Core (i18n + MVVM base VMs), ~20 Tier-1 controls + building blocks (Form/Drawer/SplitPanel/Modal), 7 Tier-2 composites (tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart), and the app shell with **both** sidebar + ribbon chrome, nav, page bases, palette, user/tenant, and transitions. WPF skin deferred (shares tokens/VMs; forks templates). Gallery lives in `Birko/Consumers`.

## 2026-07-06 — Birko.Xaml.Avalonia — BChart on LiveCharts2 → STORY-035 done (EPIC-015)
The final Tier-2 composite, closing **STORY-035** (all 7 done). [tasks/EPIC-015/STORY-035](tasks/EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md).
- **`BChart`** (`b-chart`) over **LiveCharts2** (`LiveChartsCore.SkiaSharpView.Avalonia` 2.0.5) — chosen over ScottPlot/OxyPlot for the best API + UX (modern/animated, MVVM-first) and Avalonia+WPF support (the epic's both-platforms constraint). Bind `Series` (Core `ChartSeries`) + `Kind` (Line/Column) + `Labels`; series colored from the token palette (`BColorPrimary`/`Info`/`Success`/`Warning`/`Danger` → `SKColor`). Verified: series/kind config tests + a screenshot (token-blue line + axes/labels; LiveCharts animates on load so the headless frame is mid-animation). **First external UI dependency beyond Avalonia**; SkiaSharp aligns cleanly with Avalonia 11.2.3 (spike-checked).
- Avalonia suite now **81**. **STORY-035 complete** — Tier-2: tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart.

## 2026-07-06 — Birko.Xaml — Ribbon (BAppShell chrome) (EPIC-015 / STORY-036)
The last big chrome piece: **`Ribbon`** (`b-ribbon`) — a tab strip whose active tab shows labeled groups of icon+label command buttons, model-driven via Core `RibbonTab`/`RibbonGroup`/`RibbonItem` (`Tabs` + `SelectedIndex`, item `Run` on click), token-styled. Screenshot-verified (Home/View tabs; Clipboard + Records groups). Avalonia suite now **78**. Remaining STORY-036: a thin RibbonAppShell view (compose the ribbon over the content region), transition animations, ListBox restyle. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md).

## 2026-07-06 — Birko.Xaml.Shell — command palette (Ctrl+K), user area, tenant switcher, FormModal (EPIC-015 / STORY-036)
Advanced the shell chrome + page shapes. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md).
- **Command palette (Ctrl+K):** `ShellViewModel` builds `PaletteCommands` from the nav modules (go-to) + themes (switch), exposes `IsPaletteOpen`/`OpenPaletteCommand`; `ShellView` overlays the `CommandPalette` (from STORY-035) bound to those, opened by a `Ctrl+K` `KeyBinding`.
- **User area:** header avatar + `UserName` (hidden when empty) with a Flyout of `UserCommands` (`RunUserCommand`).
- **Tenant switcher:** header `ComboBox` bound to `Tenants`/`CurrentTenant`, shown when `HasMultipleTenants`.
- **`FormModal` page-shape** (`Birko.Xaml.Avalonia`): reusable create/edit dialog composing `Modal` + `Form` + Save/Cancel (`IsOpen`/`Title`/`Fields`/`Model`/`SaveCommand`/`CancelCommand`). The epic's `FormModal<T>`.
- Screenshot-verified (full header chrome: tenant + theme + user; and the FormModal dialog). **11 shell/formmodal tests**, Avalonia suite now **74**. Remaining STORY-036: the ribbon (`BAppShell`), transition animations, ListBox restyle.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-2 composites: tree-menu, command-palette, viewers, kanban, markdown-editor (EPIC-015 / STORY-035)
Building the Tier-2 composites. [tasks/EPIC-015/STORY-035](tasks/EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md) in-progress.
- **tree-menu** — `TreeView`/`TreeViewItem` token restyle (`Controls/Tree.axaml`): expander chevron (`PART_ExpandCollapseChevron`), token hover/selected, indented children, `:empty` hides the chevron on leaves. Screenshot-verified.
- **command-palette** — `CommandPalette` (`Controls/CommandPalette.cs` + Blocks.axaml template) over a platform-neutral `CommandItem` model (`Birko.Xaml.Core.Commands`): an overlay whose search box filters commands, keyboard-navigable (Up/Down/Enter/Esc), invokes `Run` + closes. Screenshot-verified (search box + grouped command list). This is also the **STORY-036** shell palette — the control exists; wiring Ctrl+K into `ShellView` is a small follow-up.
- **object-tree / JSON viewer** — `ObjectTree` (`Controls/ObjectTree.cs`): `Source` (object graph) or `Json` (string) → recursive tree over the restyled `TreeView`, type-colored monospaced values (string=success/number=primary/bool=warning/null=muted); walks `JsonNode`/dict/enumerable/POCO; invalid JSON → raw-string leaf. Screenshot-verified (nested `user{}`/`roles[]`/`null`). Covers `b-object-tree` + `b-json-viewer`.
- **xml-viewer** — `XmlViewer` (`Controls/XmlViewer.cs`): `Xml` string → `XDocument` → tree (elements `<tag>` primary, `@attributes`/`#text` success-colored leaves, monospace); invalid XML → raw leaf. Screenshot-verified. Covers `b-xml-viewer`.
- **kanban** — `Kanban` (`Controls/Kanban.cs`) over Core `KanbanColumn`/`KanbanCard`: horizontal token-styled columns (header + live count) each an `ItemsControl` bound to `column.Cards` (`ObservableCollection` → model moves update live), card surfaces; best-effort pointer drag-drop between columns. Screenshot-verified (3-column board). Covers `b-kanban` (recursive nesting deferred).
- **markdown-editor** — `MarkdownEditor` (split editor + live preview) + dependency-free `MarkdownRenderer` (static): a common Markdown subset (headings, `**bold**`/`*italic*`/`` `code` ``/`[text](url)`, lists, fenced code, hr) → token-styled controls. Screenshot-verified (H1 + bold + list + inline-code + code block). Covers `b-markdown-editor` (Markdig can replace the parser later).
- Avalonia suite now **65**. **Only `chart` remains** in STORY-035 — parked for a both-platforms plotting-lib decision (LiveCharts2/ScottPlot/OxyPlot).

## 2026-07-05 — Birko.Xaml.Gallery moved to the Consumers bucket
Relocated the gallery from `Birko/Framework/Birko.Xaml.Gallery` → `Birko/Consumers/Birko.Xaml.Gallery` so it mirrors `Birko.Web.Playground`: a **consumer/demo app**, not a framework project. It now references the `Birko.Xaml.*` assemblies via `ProjectReference` across the bucket (`../../Framework/...`), is **removed from `Birko.Framework.slnx` + `.code-workspace`**, and the framework test suite no longer depends on it (dropped the gallery-only `ParityScreenshotTests`; the gallery is run/validated standalone via `dotnet run`, like the Web playground's `verify.mjs`). Keeps its own git history. Framework Avalonia suite: 45 → 44.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-1 complete: Modal + DataGrid (EPIC-015 / STORY-034 done)
The last two Tier-1 controls, closing **STORY-034** (all ~20 restyled controls done). [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md).
- **`Modal`** — centered dialog over a dimming backdrop (`IsOpen`/`Title`, backdrop-click closes, `--b-modal-width` card). A `Form` + Save/Cancel inside it = the **FormModal** pattern (screenshot-verified: titled card, required-asterisk form, buttons).
- **`DataGrid`** (`data-table`) — restyled with Birko tokens. DataGrid ships its theme as **Styles** (not a ResourceDictionary), so it's a separate `Controls/DataGridStyles.axaml` (includes DataGrid's Fluent theme + layers Birko tokens: header band via `--b-table-header-*`, cell text/font, grid lines) added to `Application.Styles`; needs the `Avalonia.Controls.DataGrid` package. Finstat's dark charcoal header band verified in the gallery.
- **3 new tests** (Modal toggle + FormModal screenshot, DataGrid header token) — Avalonia suite now **45**. CSS parity clean.
- **EPIC-015 is now 6/8 stories done** (029/030/031/032/033/034); only STORY-036 tail (ribbon/command-palette/user-tenant) and STORY-035 (Tier-2 composites) remain.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-1 tail: ToggleSwitch, BusySpinner, dropdown menu, Breadcrumb (EPIC-015 / STORY-034)
Filled in most of STORY-034's deferred controls — now **16 Tier-1 controls** done, only `DataGrid`/`modal` remain. [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md).
- **`ToggleSwitch`** — token-driven; correctly declares the required `PART_MovingKnobs` + `PART_SwitchKnob` (the latter must be a `Panel`) so the XAML compiler's `AVLN2207`/`AVLN2205` contract passes; `:checked` moves the knob + turns the track primary.
- **`BusySpinner`** — custom `TemplatedControl` (renamed from `Spinner` to avoid colliding with Avalonia's built-in `Spinner`): a rotating token-colored `Arc`, default size from `BSpinnerSize`.
- **Dropdown menu** — `MenuFlyoutPresenter` (token surface) + `MenuItem` (hover/padding) ControlThemes.
- **`Breadcrumb`** — `ContentControl` that builds token-styled crumbs + separators in code, last crumb emphasized.
- **Gotcha captured** (project CLAUDE.md): a top-level ControlTheme animation `Style` (`<Style Selector="^ /template/ X"><Style.Animations>`) **silently breaks the whole theme's application** — scope animations inside the templated element's own `.Styles` (BusySpinner rotates via `Arc.Styles` + a `RotateTransform.Angle` animation).
- **6 headless tests** (ToggleSwitch checked-track, BusySpinner theme+arc, Breadcrumb crumbs+separators, dropdown themes registered, + the existing) — Avalonia suite now **42**; shown across themes in the gallery.

## 2026-07-04 — Birko.Xaml.Shell — app shell + navigation + page views (EPIC-015 / STORY-036, MVP)
The capstone: a working desktop CRUD app shape — sidebar chrome + navigation + generic list/detail/split page views binding the STORY-032 base VMs. New `Birko.Xaml.Shell` Avalonia assembly + platform-neutral nav/shell VMs in `Birko.Xaml.Core`. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md) is **in-progress** (MVP done; ribbon/command-palette/user-tenant/FormModal deferred).
- **Navigation (Core, Avalonia-free — constraint #3):** `Navigation/ModuleDefinition` (the `buildModuleRoutes`/`ModuleManifest` analogue), `INavigationService`/`NavigationService` (module map + back-history + `Current` page VM), `Mvvm/ShellViewModel` (nav + `IThemeManager` + title; Navigate/Back/SetTheme commands), `Mvvm/SplitPageViewModel<T>`. Page-base VMs (`CrudViewModelBase`/`DetailPageViewModel`) gained a `Fields` schema so the generic views render a `Form`.
- **`Birko.Xaml.Shell` (Avalonia):** `ViewLocator` (`*ViewModel→*View` naming convention, else generic base-page mapping — Split before List since it derives from List); `Views/ShellView` (**sidebar** chrome = module nav + header title + theme switcher + content region via the ViewLocator + status bar — the `BSidebarAppShell` analogue); generic `ListPageView` (permission-gated New/Edit/Delete toolbar + search + inline create/edit `Form`), `DetailPageView` (`Form` + Save/Cancel), `SplitPageView` (master list + detail `Form` over `SplitPanel`).
- **Proven end-to-end** — a headless screenshot shows the full shell (sidebar Contacts/About nav, header title + theme switcher, a SplitPage list in the content region, status bar), all token-driven. **6 shell/nav tests** (navigation + Back, ViewLocator resolves Split/List/Detail VMs, shell renders the active page via the locator, nav swaps the page). Avalonia suite now **38**.
- **Page views use `x:CompileBindings="False"`** — their DataContext is a *generic* base VM with no fixed `x:DataType`, so bindings are reflection-based (documented in the project CLAUDE.md).
- **Deferred:** ribbon chrome (`BAppShell`), command palette, user-area / tenant switcher, a `FormModal` dialog (inline edit covers create/edit), content-transition animations, ListBox restyle/display templating.

## 2026-07-04 — Birko.Xaml — building blocks: Form + Drawer + SplitPanel (EPIC-015 / STORY-033)
The three controls the page layer composes, so the CRUD page bases stay declarative. Closes [tasks/EPIC-015/STORY-033](tasks/EPIC-015-birko-xaml-ui-framework/STORY-033-building-blocks-form-drawer-splitpanel/STORY.md). EPIC-015 now **5/8 stories done**.
- **`Form`** (keystone) — schema-driven (`Birko.Xaml.Avalonia/Controls/Form.cs`): binds `Fields` (`Birko.Xaml.Core.Forms.FormField[]`) + `Model` and generates labeled, two-way-bound inputs (Text/TextArea/Number→`TextBox`, Checkbox→`CheckBox`, Select→`ComboBox`) with a required asterisk, all token-styled. **Pairs directly with the STORY-032 VMs** — bind `Fields` + `CrudViewModelBase.EditingItem` / `DetailPageViewModel.Model` instead of hand-rolling XAML per screen. Verified in the gallery (renders + re-themes incl. finstat flat corners).
- **`Drawer`** — slide-in overlay (`IsOpen`/`Placement` left|right, backdrop-click closes), token width/bg.
- **`SplitPanel`** — master/detail over `GridSplitter` with responsive collapse (`:collapsed` below `CollapseWidth` hides the master; pixel master column so the splitter can drag).
- **Schema in Core, control in `.Avalonia`** — the `FormField` schema is platform-neutral (Avalonia-free, reusable by a future WPF `Form`); the control is the Avalonia view. Same split as i18n's logic/markup-extension.
- **8 headless tests** (Form generate/initial-bind/two-way/required-asterisk, Drawer visibility toggle, SplitPanel wide-vs-narrow collapse) + per-theme gallery screenshots. Avalonia suite now **31**.
- **Deferred:** DataAnnotations validation (only the required asterisk today), Drawer slide animation, `GridSplitter` drag precision.

## 2026-07-04 — Birko.Xaml.Core — i18n + base ViewModels (EPIC-015 / STORY-032)
`Birko.Xaml.Core` grew from theme abstractions into the thin, **Avalonia-free** platform core: i18n + base MVVM ViewModels + a CRUD port. Closes [tasks/EPIC-015/STORY-032](tasks/EPIC-015-birko-xaml-ui-framework/STORY-032-xaml-core-foundation/STORY.md). Its only dependency is CommunityToolkit.Mvvm (platform-neutral, works Avalonia + WPF).
- **i18n** — `Localization.II18n`/`I18n` (+ `I18n.Instance` singleton, mirrors Birko.Web's `i18n`): locale dictionaries, `this[key]` indexer (fallback → key), `SetLocale` (raises `INotifyPropertyChanged` + `LocaleChanged`), `Translate(key, args)` with `{placeholder}` interpolation.
- **`{l:Tr}` markup extension** lives in **`Birko.Xaml.Avalonia`** (`Markup/TrExtension.cs`), not Core — it must return an Avalonia `Binding`. It resolves through the Core singleton and re-resolves live on `SetLocale`. **Gotcha:** Avalonia doesn't observe `INotifyPropertyChanged` on **indexer** accessors, so `{l:Tr}` binds a small per-binding source's real `Value` property (refreshed on `LocaleChanged`) rather than `I18n[key]` directly.
- **Base ViewModels** (CommunityToolkit.Mvvm) — `BasePageViewModel` (busy/loaded/title + live re-localization), `CrudViewModelBase<T>` (observable items, selection, editing slot, **permission-gated** Create/Edit/Delete/Save/Refresh commands), `ListPageViewModel<T>` (client-side search), `DetailPageViewModel<T>` (load-by-id, gated Save).
- **`Data.ICrudDataSource<T>` port — deliberately NOT `IAsyncBulkStore<T>` directly.** Key architectural boundary: `Birko.Data.*` are shared **`.projitems`** (compiled into each importing assembly) while `Birko.Xaml.*` are **real assemblies** — importing the store projitems into Core would duplicate those types against a consumer's own aggregator (the hazard root CLAUDE.md warns about). So Core owns a thin CRUD port and the consumer supplies a ~10-line store→port adapter in the assembly that already has the Birko.Data types.
- **Avalonia-free enforced by test** — `Birko.Xaml.Core.Tests` asserts the Core assembly references no `Avalonia.*` (WPF-addendum constraint #1). **15 Core tests** (i18n, CRUD VM over a fake port, permission gating, i18n re-emit) + a headless `{l:Tr}` re-resolution test in `Birko.Xaml.Avalonia.Tests` (now 24). Registered in `.slnx` (`/Tests/`) + `.code-workspace`.

## 2026-07-04 — Birko.Xaml.Avalonia — Tier-1 restyled controls, first batch (EPIC-015 / STORY-034)
Started the Tier-1 control sweep (the ~80%-of-visual-value layer). `Birko.Xaml.Avalonia/Controls/` is now split into category `ResourceDictionary`s — **Buttons / Inputs / Toggles / Surfaces / Indicators / Overlays** — merged by `Controls.axaml` (which `BirkoTheme.axaml` includes). [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md) is **in-progress**.
- **11 controls done, all token-driven** (`{DynamicResource B*}`, no hard-coded colors/sizes): `Button`, `TextBox` (single + multiline via `AcceptsReturn`), `Card`, `Badge`, `Tag`, `TabControl`/`TabItem`, `CheckBox`, `RadioButton`, `ProgressBar`, `ToolTip`, `ComboBox` (+ `ComboBoxItem`). Named `ContentControl` themes (`BCard`/`BBadge`/`BTag`) apply via `Theme="{StaticResource …}"`.
- **Verified visually** — the gallery now shows the full set; headless Skia screenshots per theme confirm parity, incl. finstat's flat/square corners + brand green flowing through checkbox/radio/progress/tab accents. **23 headless tests** (theme system + control themes + Tier-1 + screenshot).
- **Deferred** (need more than a mechanical restyle; some are STORY-035 candidates): `ToggleSwitch` (strict `PART_MovingKnobs`/`PART_SwitchKnob` + knob animation), a `Spinner` (custom control + rotation), `Menu`/dropdown, `table`, `data-table` (Avalonia `DataGrid` = separate pkg + row-height parity), `modal` overlay, `breadcrumb`.
- **Control-theme gotchas** captured in the project CLAUDE.md: `double`→`CornerRadius` needs a `CornerRadius` resource (generator now emits radius tokens as such); several native controls enforce required template parts (`AVLN2205`) — supply them or defer; `TextPresenter.Foreground` isn't bindable (let it inherit).

## 2026-07-04 — Birko.Xaml — Tier-0 gallery + first restyled controls, gate = GO (EPIC-015 / STORY-031)
Third slice of **EPIC-015** and the **go/no-go gate**: the first restyled `b-*`-equivalent controls + a runnable Avalonia gallery proving the token pipeline gives visual parity with Birko.Web and live theme swap. Closes [tasks/EPIC-015/STORY-031](tasks/EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md). **Verdict: GO** — Tier-1 (STORY-034) proceeds.
- **First ControlThemes** — `Birko.Xaml.Avalonia/Controls/Controls.axaml`: token-driven `Button` + `TextBox` (implicit `{x:Type}` themes) and reusable `Card`/`Badge` (`ControlTheme` on `ContentControl`, applied via `Theme=`). Every visual value is `{DynamicResource B*}`, so controls re-theme live. New one-include `BirkoTheme.axaml` merges `Tokens.axaml` + `Controls.axaml`.
- **`Birko.Xaml.Gallery`** — a runnable Avalonia `net8.0` desktop app (Avalonia.Desktop + Fluent base), dev/validation artifact (not a shipped consumer). Theme switcher + Button/TextBox/Card/Badge; `dotnet run --project Birko.Xaml.Gallery`.
- **Gate proven visually** — a headless **Skia screenshot** test renders the gallery to a PNG per theme; all four (light/dark/neon/finstat) render distinctly and correctly, incl. **finstat's flat/square corners** (radius token → 0) vs rounded elsewhere. Same token source as the web → same look.
- **Generator fix surfaced by the gate** — a `double` radius token can't bind to a `CornerRadius` property (runtime `InvalidCastException` that aborts the ControlTheme). STORY-029's `AxamlEmitter` now emits `--b-radius*` as **`CornerRadius`** resources (not `x:Double`); other lengths stay `x:Double`. **CSS byte-parity unaffected** (`verify` clean).
- **15 headless tests** (`Birko.Xaml.Avalonia.Tests`, now with Fluent + Skia) — theme-system resolution (9) + control themes (Button/TextBox/Card re-theme per variant, `CornerRadius`-from-token, Badge findable) + the parity-screenshot capture. DesignTokens suite stays 30, `verify` clean.

## 2026-07-04 — Birko.Xaml.Core + Birko.Xaml.Avalonia — Avalonia theme system (EPIC-015 / STORY-030)
Second slice of **EPIC-015**: the Avalonia skin's theme system — drop in the generated token dictionaries and switch theme at runtime (light/dark/neon/finstat), matching Birko.Web. Closes [tasks/EPIC-015/STORY-030](tasks/EPIC-015-birko-xaml-ui-framework/STORY-030-avalonia-theme-system/STORY.md). **First real Avalonia/.NET assemblies in the `Birko\Framework` bucket** (the EPIC convention break — compiled AXAML through `.shproj`/`.projitems` is fragile), both `net8.0` (Avalonia 11.2.3; TFM differs from the framework's net10.0 because that's what Avalonia 11.2.3 targets). Registered in `Birko.Framework.slnx` (`/Xaml/`) + `.code-workspace`; referenced via `ProjectReference`, **not** the `Birko.Framework.csproj` aggregator (that's `.projitems`-only).
- **`Birko.Xaml.Core`** (Avalonia-free, WPF-addendum constraint #1 — enforced: no `using Avalonia.*`) — `ThemeInfo` (mirrors the web shell's `ThemeOption`), `BirkoThemes` (the 4 built-ins, in sync with the `Birko.DesignTokens` sheets + web `BUILTIN_THEMES`), `IThemeManager` (`Available`/`Current`/`ThemeChanged`/`SetTheme` — the XAML analogue of the web `setTheme`). STORY-032 grows this with i18n + base VMs.
- **`Birko.Xaml.Avalonia`** — `BirkoThemeVariants` (the `ThemeVariant` instances: Light/Dark reuse built-ins; **Neon** inherits Dark, **Finstat** inherits Light), `AvaloniaThemeManager : IThemeManager` (over `RequestedThemeVariant`), and the generated `Themes/Tokens.axaml`.
- **ThemeDictionaries, not swapped MergedDictionaries.** A single `Tokens.axaml` `ResourceDictionary` whose `ThemeDictionaries` hold one entry per theme; setting `RequestedThemeVariant` re-resolves every `{DynamicResource}` **live, no restart**. A spike de-risked this first: Avalonia resolves **custom** variants (neon/finstat), not just Light/Dark, via `TryGetResource(key, variant)` with `InheritVariant` fallback.
- **Key identity is the trick** — `ThemeDictionaries` keys are `{x:Static themes:BirkoThemeVariants.X}`, the *same* static `ThemeVariant` instances assigned to `RequestedThemeVariant`, so lookup matches by identity (not fragile string keys). **Brushes at root, colors per-variant**: one `SolidColorBrush` per color token with `Color="{DynamicResource BColorX}"`, so a swap updates the brush without duplicating it 4×; colors live per-theme in `ThemeDictionaries`. Controls bind `{DynamicResource BColorXBrush}` / `{DynamicResource BRadius}`.
- **Generator restructured** — STORY-029's `AxamlEmitter` now emits this one ThemeDictionaries file instead of 4 separate dicts. **CSS byte-parity is unaffected** (still `verify`-clean; the `git diff` on the web CSS stays empty).
- **8 headless tests** (`Birko.Xaml.Avalonia.Tests`, Avalonia.Headless.XUnit, net8.0) — the real `Tokens.axaml` loads, all 4 variants resolve `BColorPrimary`, `var()`-ref tokens (`BBorderFocus`) follow the active primary, lengths bake (finstat radius → 0), a live `RequestedThemeVariant` swap re-colors a `DynamicResource` brush, and `AvaloniaThemeManager` lists/switches themes. (DesignTokens suite adjusted to 30 for the single-file AXAML.)
- **Deferred:** composite/motion tokens (shadows/focus-rings/transitions/easings/gradients) map when a control needs them (STORY-034); gallery app is STORY-031; scoped `inverse` theme not emitted to AXAML.

## 2026-07-04 — Birko.DesignTokens — single-source token generator (EPIC-015 / STORY-029)
First slice of **EPIC-015 (Birko.Xaml)**: `tokens.json` becomes the single source of truth for all design tokens, and a C# tool generates every target so the web (CSS) and desktop (Avalonia XAML) design systems can never drift. Closes [tasks/EPIC-015/STORY-029](tasks/EPIC-015-birko-xaml-ui-framework/STORY-029-design-tokens-generator/STORY.md).
- **`Birko.DesignTokens` — the first real, buildable `.csproj` in the `Birko\Framework` bucket** (every other sibling is `.shproj`/`.projitems`). Justified: it is a build-time code-generation **tool**, not a runtime shared library, so it needs build output and is **not** imported into the `Birko.Framework.csproj` aggregator. Foreshadows EPIC-015's note that `Birko.Xaml.*` also ship as real assemblies. Registered in `Birko.Framework.slnx` (new `/Xaml/` folder) + `.code-workspace`.
- **CLI** — `generate` (everyday: emit CSS + AXAML from `tokens.json`), `verify` (diff on-disk CSS vs `tokens.json`, exit 1 on drift — for CI/pre-commit), `extract` (bootstrap/re-derive `tokens.json` from the current CSS). Path resolution follows the `$(BirkoSrc)`/`BIRKO_SRC` convention (walks up to the folder holding both `Framework` and `Web`).
- **Byte-identical CSS parity (the acceptance gate).** Each CSS file is modeled as `prologue` + `{selector} {` + ordered body lines + `}` + `epilogue`; every body line is one node — a structured `var` (name/value/**verbatim trailing comment**, which preserves the hand-authored comment-column alignment) or verbatim `raw` (comments, blanks). **Values** are single-sourced on the `var`; comments/layout round-trip verbatim. Regenerating all 5 files (`tokens.css` + `themes/{dark,neon,finstat,inverse}.css`) is byte-identical to the committed files, proven against `git show HEAD:`.
- **Line endings** — the repo is canonically **LF** (`core.autocrlf=true` had presented `tokens.css` as CRLF in the working tree; the 4 theme files were LF). The model is kept LF and comparisons normalize CRLF→LF, so generation is **machine-independent** (no dependency on a checkout's autocrlf state) — this fixed a latent "works on my machine" trap.
- **Avalonia AXAML** (first cut) — `Tokens.axaml` + `Theme.{Light,Dark,Neon,Finstat}.axaml` into `Birko.Xaml.Avalonia/Themes/`. `--b-color-x` → `Color` + paired `SolidColorBrush` (`BColorX`/`BColorXBrush`; `rgba` alpha folds into `#AARRGGBB`); rem/px → `x:Double` (rem baked at 16px; unitless `0` → 0); `--b-font*` → `FontFamily`. `var()` refs resolve **per theme**, so each dictionary is full & self-contained (a theme swap is a whole-dictionary replace) — all 4 expose an identical 181-key set. Composite/motion tokens (shadows, focus rings, transitions, easings, durations, gradients), `ThemeVariant`/`DynamicResource` wiring, and the scoped `inverse` partial in AXAML are **deferred to STORY-030**.
- **`tokens.json` is deliberately language-neutral** (no C#-isms, no logic in the data) so a later swap of the C# generator for a TypeScript one is a one-file emitter rewrite against the golden CSS — the schema and extracted data carry over untouched.
- **32 xUnit + FluentAssertions tests** (`Birko.DesignTokens.Tests`) — per-sheet CSS round-trip parity, extractor round-trip on the live files, single-source/uniqueness checks, AXAML well-formedness, per-theme resolution (primary + `var()`-ref follow-through), cross-theme key-set parity (swap safety), and color/length/key-name conversion unit tests.

## 2026-06-25 — Birko.Security.AspNetCore — server-side role resolution + claim-delimiter fix
`ICurrentUser.Roles` was always empty: the JWT deliberately omits roles and — unlike permissions — they were never resolved server-side, so anything relying on `Roles` silently got nothing. Roles now have the same per-request server-side resolution path permissions already had.
- **`IUserPermissionResolver.GetRolesAsync(userId, tenantId, ct)`** — new method, added as a **default interface method** returning an empty set so existing implementers don't break; role-store-backed resolvers override it (parallel to the existing `GetAsync` for permissions)
- **`PermissionResolutionMiddleware`** — new `RolesItemsKey` const; after stashing resolved permissions it now also calls `GetRolesAsync` and stashes the result in `HttpContext.Items[RolesItemsKey]`
- **`ResolvedPermissionsCurrentUser.Roles`** — reads the middleware-resolved set first, falls back to the role claim when the slot is absent (splitting on both `,` and `;`)
- **`ClaimsCurrentUser` delimiter fix** — the producer (`TokenServiceAdapter`) joins multi-values with `;` but both readers split on `,` only; `Roles` and `Permissions` now split on **both** `[',', ';']` (fixes e.g. a superadmin `*` packed into a joined permission claim not surfacing for `Contains("*")`)
- **Consumer responsibility** — the framework default returns empty; a consuming app must override `GetRolesAsync` (in Symbio: `UserPermissionResolver.GetRolesAsync` → cached → `RoleService.GetUserRoleNamesAsync`, batched, tenant-scoped, mirroring the permissions path)
- **13 new xUnit + FluentAssertions tests** (`Birko.Security.AspNetCore.Tests`) — `ResolvedPermissionsCurrentUserTests` (resolved set wins, claim fallback, both-delimiter split, empties), `PermissionResolutionMiddlewareTests` (populates both slots, null tenant, no-ops when unauthenticated / bad userId, default `GetRolesAsync` is empty), plus delimiter `[Theory]` cases in `ClaimsCurrentUserTests`. Suite green (77 total)

## 2026-06-24 — Birko.Web.Testing — shared E2E / browser-automation package
New fourth source-only sibling in the **`Birko\Web` bucket** (`Birko.Web.Testing`, pkg `birko-web-testing`) — a reusable browser-automation toolkit for every Birko.Web consumer (Symbio, Playground, Presenter, WorkoutTracker, …). Consumed by TypeScript `paths` via the Node test runner (not esbuild — no `build.js` alias). Not in any `.slnx`/`.sln`.
- **Two lanes + a driver-agnostic core.** **Playwright** = the test suite (`birkoPlaywrightPreset`, `runSmoke` route-sweep, fixtures `page/form/dataTable/nav/consoleGuard`, page objects). **Puppeteer** = utility scripts only (`launchSession` for PDF/screenshot/perf/scrape). **Core** (`/core`, no driver imports) = `RouteEntry` manifests, `b-*` selectors, `loginViaApi` (+ the `localStorage` auth snapshot `birko-web-shell`'s `createAuthStore` writes), one `attachCollector` (console + 4xx/5xx) that works for both drivers.
- **Selectors grounded in real source** — `b-data-table` `.row-action-trigger[data-id]` → top-level `b-dropdown-menu .item[data-id]`; `b-form` `[data-field]`/`[name]`; `b-sidebar`/`b-ribbon`; `BaseCrudPage` `#btn-create`/`#modal`/`#form`/`#btn-save`. Playwright CSS pierces shadow automatically; Puppeteer uses `>>>`.
- **One-Chromium policy** — Playwright owns the browser (`npx playwright install chromium`); Puppeteer reuses it (`PUPPETEER_SKIP_DOWNLOAD=1` + `PUPPETEER_EXECUTABLE_PATH`). Never install the drivers globally; pin per consumer. Note: a path-mapped source package resolves its deps relative to its own dir, so each consumer's tsconfig must also map `@playwright/test`/`puppeteer` → its `node_modules`.
- **Reference implementation** wired into Symbio at `Birko.Consumers/Symbio/tests/ui-e2e/` (config + `auth.setup` + Communication `smoke.spec` + Inquiries `communication.spec`); **typechecks clean**. Live run is the consumer's step (needs the dev stack up + a seeded Building). Docs: package `README.md`/`ENV.md`, `docs/web.md`, and the [[birko-new-project]] skill.

## 2026-06-19 — Birko.Web.Components — b-accordion + shared `coerceCssLength`
Closed [tasks/EPIC-001/STORY-028](tasks/EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/STORY.md) (display & disclosure components), both surfaced while building the Birko.Web Playground. Note: these live in the **`Birko\Web` bucket** (frontend libs), not the .NET `Birko\Framework`.
- **`b-accordion`** (new `layout` component, 58th overall) — collapsible disclosure group. `setItems([{id,header,open?,disabled?}])` config mirroring `b-tabs.setTabs`; bodies via `slot="{id}"`. Single-open by default, `multiple` attribute allows several open. Native `<button>` headers (`aria-expanded`/`aria-controls`), `<section role="region">` panels toggled via `hidden`, keyboard Enter/Space (toggle) + Up/Down/Home/End (`rovingIndex` from `dom-utils`) across enabled headers. `size` (sm|md|lg) = vertical-footprint via `--b-control-min-height-*`. Emits `toggle` `{id,open}`. No new i18n keys (headers are consumer-supplied; state is conveyed via ARIA). Added to the playground gallery.
- **`coerceCssLength(value, unit='px')`** (new in `Birko.Web.Core`, `src/css/length.ts`) + **`BaseComponent.lengthAttr(name, fallback, unit)`** — coerce a bare-number length (`"160"` → `"160px"`) so it can't produce the invalid `style="height:160"` the browser drops (which let a `height:100%`/`width:100%` child stretch unboundedly). Routed `b-chart` (`height`), `b-skeleton` (`width`/`height`/`size`), and the four data viewers (`b-json-viewer`/`b-object-tree`/`b-pre`/`b-xml-viewer`, `max-height`) through it — six components, same latent bug.

## 2026-06-15 — Birko.Data.InMemory — in-memory store backend + test-fake consolidation
New `Birko.Data.InMemory` sibling: the simplest possible store, backing the entity set with a thread-safe `ConcurrentDictionary<Guid, T>` and no persistence. Built to be the canonical **test double** (one correct implementation instead of a hand-rolled fake per test project), a **reference implementation** (the `AbstractJsonStore` dictionary model minus the file I/O), and a zero-setup **prototyping/demo** backend.
- **Stores** — `InMemoryStore<T>` (sync, `AbstractBulkStore<T>`) and `AsyncInMemoryStore<T>` (async, `AbstractAsyncBulkStore<T>`), plus `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>` base classes. Both implement the **full** `IBulkStore<T>` / `IAsyncBulkStore<T>` contract — filter-based `Update(filter, …)` / `Delete(filter)`, `ReadFirst`/`ReadFirstAsync`, ordering + paging, and `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` via `AggregateHelper.LinqAggregate(Async)` — which the ad-hoc fakes never fully did
- **Conventions honored** — overrides only the `*Core` methods (lazy-init preserved); `Read(Guid)` / `ReadAsync(Guid)` do O(1) dictionary lookups; `Delete(filter)` / `DeleteAsync(filter)` do a single pass; bulk reads return a `List<T>` snapshot so the concurrent dictionary can mutate mid-enumeration. No settings class — `ISettingsStore<Settings>` is implemented as a no-op purely for drop-in compatibility (an in-memory store can stand in for a JSON/SQL store in the same wiring)
- **40 xUnit + FluentAssertions tests** (`Birko.Data.InMemory.Tests`) covering both stores: CRUD, bulk ops, filter update/delete, ordering/paging, lazy-init, aggregation, `Save`, `Destroy`, settings surface, and async cancellation
- **Fake consolidation** — migrated five hand-rolled in-memory stores across four sibling test repos to subclass the new store, deleting ~250 lines of duplicated boilerplate: `Birko.Security.OAuth.Server.Tests` (78→14 lines; the 5 marker stores are now bare subclasses), `Birko.Validation.Tests` (~90-line nested fake → 4 lines), `Birko.Localization.Data.Tests` (kept only `Seed`), and `Birko.Data.Sync.Tests` (`TestBulkStore` + `TestSyncKnowledgeItemStore`, keeping only the sync-bookkeeping members). All four suites stay green (43 / 122 / 26 / 21)
- Registered in `Birko.Framework.slnx` (Data.NoSQL + Tests folders), `Birko.Framework.code-workspace`, and the `Birko.Framework.csproj` aggregator

## 2026-06-15 — Birko.Data.Stores — async stores observe cancellation consistently
`AbstractAsyncStore.EnsureInitializedAsync` now calls `ct.ThrowIfCancellationRequested()` at the top. **Behavior change:** every public async CRUD method funnels through this gate, so an already-cancelled `CancellationToken` now surfaces as `OperationCanceledException` even on an already-initialized store (previously the gate returned at `if (_initialized) return;` without checking the token, so cancellation was only observed on the very first, uninitialized call). This is the idiomatic .NET contract and affects only callers that pass a cancelled token — which is what they asked for.
- **Regression fix** — `AsyncStoreTests.Operations_ShouldRespectCancellationToken` was red on `main`: written 2026-03-11 when the in-memory test store overrode the *public* async methods as token-ignoring no-ops, it went stale when the **lazy-init refactor (2026-04-10)** routed all public methods through `EnsureInitializedAsync(ct)`. Its assertion (`NotThrowAsync<TaskCanceledException>`) contradicted the test's own name; corrected to `ThrowAsync<OperationCanceledException>` (the base type also catches `TaskCanceledException`)

## 2026-06-15 — Birko.Data.Stores — `ReadFirst` / `ReadFirstAsync` on bulk stores
Closed a sharp overload-shadowing footgun in the store hierarchy. On any bulk store, `store.Read(filter)` does **not** return a single entity (the `IStore<T>` contract) — it returns the whole `IEnumerable<T>` result set. This is C# member lookup, not overload preference: a bulk store declares the collection overload `Read(filter, orderBy, limit, offset)`, and the compiler only keeps methods named `Read` declared in the *most-derived* type that declares any such method, so the inherited single-result `Read(filter)` is removed from the candidate set entirely (reachable before only by casting to `IReadStore<T>`). The same applied to `ReadAsync`.
- **New single-result accessor** — `IBulkReadStore<T>.ReadFirst(filter)` → `T?` and `IAsyncBulkReadStore<T>.ReadFirstAsync(filter, ct)` → `Task<T?>`, added as **default interface methods** (delegate to the cast-through-`IReadStore<T>` path) so every existing `IBulkStore<T>` implementer — including the decorator/wrapper chain — gets them with zero breakage, and interface-typed callers can use them directly
- **Concrete `public virtual` overrides** on the four bulk base classes — `AbstractBulkStore<T>`, `DataBaseBulkStore<DB,T>`, `AbstractAsyncBulkStore<T>`, `AsyncDataBaseBulkStore<DB,T>` — so concrete-typed callers (the common case) call `ReadFirst` directly; each delegates to `base.Read(filter)` / `base.ReadAsync(filter, ct)`, which routes through the single-row `ReadCore` (the SQL stores already issue `LIMIT 1`). Override on a platform store for native single-row optimization
- **Non-breaking** — `Read(filter)` keeps returning the collection; nothing currently working changes. `ReadFirst` is purely additive. Named `ReadFirst` (not `ReadSingle`) because it is `FirstOrDefault` semantics, not uniqueness-enforcing
- **7 xUnit + FluentAssertions tests** (`Birko.Data.Tests/Stores/BulkStoreReadFirstTests.cs`) — single-match, no-match→null, the collection-vs-single distinction, and reachability through `IBulkStore<T>` / `IAsyncBulkStore<T>` for both sync and async
- Documented in the **Conventions** section above

## 2026-06-15 — Birko.Web.Components — accessibility (ARIA / screen-reader) pass
Closed the accessibility gaps found in an audit of the `b-*` catalogue. Adds shared infrastructure so new components stay accessible by default, plus a new [`Birko.Web.Components/ACCESSIBILITY.md`](../Birko.Web.Components/ACCESSIBILITY.md) reference. Form-association via `ElementInternals` is deliberately out of scope (it's a feature, not an SR gap) — tracked in [tasks/EPIC-001/STORY-023/TASK-035](tasks/EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md).
- **`BaseComponent.uid`** (`Birko.Web.Core`) — stable per-instance id prefix, allocated lazily, surviving re-renders. Used to mint deterministic element ids for `aria-*` IDREFs (`${this.uid}-error`, `${this.uid}-body`, `${this.uid}-tip`)
- **Form-validation ARIA** — new `fieldAria()` + `renderError()` helpers in `inputs/label-hint.ts`. `fieldAria({uid, error, required})` spreads `aria-invalid`/`aria-describedby`/`aria-required`; `renderError(uid, error)` renders the error as a `role="alert"` live region linked via `aria-describedby`. Wired across **12 inputs** (input, textarea, select native+combo, color-picker, multi-select, tag-input, markdown-editor, date-picker native+custom, datetime-picker, time, range, date-range-picker). `aria-required` only on non-native (div-based) controls; native `required` covers the rest
- **`aria-expanded`** on every expand/collapse toggle — `b-tree-menu` (on the `treeitem`; invalid empty value for leaves fixed; `aria-busy` while lazy-loading), `b-kanban` card toggles (+ `aria-controls`), `b-object-tree` (now full `tree`/`treeitem`/`group` semantics), `b-form` collapsible group legends (now `role="button"` + Enter/Space activation + `aria-controls`), `b-sidebar` toggle
- **Shared `.sr-only` utility** — new `srOnlySheet` (`@sheet srOnly` in `shared-styles.css`, + `.sr-only-focusable`), replacing the copy previously private to `b-kanban`
- **`b-breadcrumb`** rebuilt as `nav > ol > li` with `aria-label="Breadcrumb"` and `aria-current="page"` on the last crumb
- **`b-command-palette`** — polite `role="status"` live region announcing result counts / "Searching…" / "No results", plus `role="combobox"` + `aria-controls` + `aria-activedescendant` on the search input
- **`b-ribbon`** — fixed broken `aria-controls` (every tab pointed at a per-tab panel id, but only the active tab's panel exists; now a single static `ribbon-panel` id, omitted in `tabs-only` mode); mobile tab headers gained `aria-expanded` + `aria-controls` (kept in sync on toggle); mobile `<dialog>` gained `aria-labelledby`
- **Reference/keyboard sweep across nav + data + inputs** —
  - `b-tabs`: tab buttons had no `id`, so each panel's `aria-labelledby` pointed at nothing → added `id="tab-${id}"` (panels reference it)
  - `b-button`: now forwards `aria-label` / `aria-current` / `title` from the host to the inner shadow `<button>` (they were previously dropped, so e.g. `b-pagination`'s "Previous page" never reached AT)
  - `b-dropdown-menu`: trigger exposes `aria-haspopup`/`aria-expanded` immediately, not only after first open
  - `b-multi-select`: invalid `role="listbox"` wrapping checkboxes → `role="group"` + `aria-label` + `aria-controls` (it's a checkbox group, not a listbox)
  - `b-data-table`: select-all + per-row selection checkboxes gained `aria-label` (new `labels.selectAll` / `labels.selectRow`)
  - `b-table`: sortable headers were click-only → inner `<button>` makes them keyboard-operable while the `<th>` keeps `columnheader`/`aria-sort`
  - `b-segmented` + `b-option-group`: were `tab`/`tablist` (or plain buttons) with no panels and no keyboard nav → now proper `radiogroup`/`radio` with roving tabindex + arrow/Home/End selection
  - `b-inline-edit`: click-only display → `role="button"` + `tabindex` + Enter/Space
  - `b-file-upload`: the native input is `display:none`, and the dropzone wasn't focusable → keyboard users couldn't upload; dropzone is now `role="button"` + `tabindex` + Enter/Space (+ focus ring), new `bwc.fileUpload.browse` label
- **De-duplication** — extracted a shared `src/dom-utils.ts` (`escapeHtml` / `escapeAttr`, `isActivationKey`, `rovingIndex`). Replaced **16** per-component copies of `_escapeHtml`/`_escapeAttr`/`_esc` (and `cell-renderers.ts`'s standalone pair — whose `escapeAttr` didn't escape `&`, now fixed) and the duplicated radio-group / Enter-Space keyboard logic with imports from it
- **Smaller fixes** — `b-button` loading sets `aria-busy` (+ `aria-hidden` spinner); `b-confirm-dialog` gets `aria-labelledby`/`aria-describedby`; `b-tooltip` links trigger→tip via `aria-describedby` (cross-shadow slotted-content naming documented as a known limitation)
- New i18n keys `bwc.palette.resultsCount`, `bwc.breadcrumb.label`. Documented in `Birko.Web.Components` CLAUDE.md (new **Accessibility** section + checklist item) and README

## 2026-06-11 — Birko.Communication.gRPC + .Server — gRPC client/server primitives
New gRPC support split into a client and a server shared project, mirroring the existing `Birko.Communication.REST` / `.REST.Server` split (client over `Grpc.Net.Client`, server over `Grpc.AspNetCore` which pulls in ASP.NET). Closes [tasks/EPIC-009/STORY-019/TASK-026](tasks/EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md). Code generation (`.proto` → C#) is intentionally out of scope — consumers bring generated clients/services via `Grpc.Tools`; these primitives configure and wrap them.
- **`Birko.Communication.gRPC`** (client) — `GrpcSettings : RemoteSettings` (`Endpoint` aliases `Location`; `MaxReceiveMessageSizeBytes` / `MaxSendMessageSizeBytes` / `DeadlineSeconds` / `Credentials` / `ExtraMetadata`); `GrpcChannelPool` (endpoint-keyed `ConcurrentDictionary` cache of reusable `GrpcChannel`s — `GetChannel` / `Remove` / `Clear`, mirrors `RestClient.GetClient`); `GrpcClientFactory.CreateClient<TClient>()` (constructs any generated `ClientBase` over a pooled channel or explicit `CallInvoker`, applying interceptors via `CallInvoker.Intercept`); `GrpcAuthenticationInterceptor` (client `Interceptor` overriding all five call kinds, token-provider or raw `Action<Metadata>` constructor); `GrpcException` (wraps `RpcException` → `StatusCode` / `Detail` / `Trailers`, mirrors `GraphQLException`)
- **`Birko.Communication.gRPC.Server`** (server) — `GrpcServerSettings : Settings` (`EnableDetailedErrors`, message-size caps, `EnableReflection`); `AddBirkoGrpc(this IServiceCollection, GrpcServerSettings?)` DI extension (in `Microsoft.Extensions.DependencyInjection` namespace, returns `IGrpcServerBuilder`); `GrpcServerAuthenticationInterceptor` (server `Interceptor` overriding all four handler kinds, validates request metadata via a `Func<Metadata, ServerCallContext, Task<bool>>`, throws `RpcException(Unauthenticated)` on failure)
- **Settings** descend the `RemoteSettings` (client) / `Settings` (server) chain; shared projects carry no `PackageReference` — the importing csproj supplies `Grpc.Net.Client` / `Grpc.AspNetCore` (+ the `Microsoft.AspNetCore.App` framework reference for the server)
- **32 xUnit + FluentAssertions tests** (24 client + 8 server) — settings, channel-pool caching/eviction/guards, interceptor metadata injection (captured-continuation), client factory over an in-memory `CallInvoker`, exception mapping; server settings, `AddBirkoGrpc` DI registration, and the auth interceptor against an in-memory `ServerCallContext`
- Registered in `Birko.Framework.slnx` (Communication + Tests folders) and `Birko.Framework.code-workspace`

## 2026-06-10 — Birko.Web.Components — `b-table` uniform row height
Fixed the long-standing visual mismatch where the `b-table` / `b-data-table` header band rendered shorter than body rows. The header CSS was already balanced for *plain text* (the `th` carries `+1px` extra vertical padding and a `2px` bottom border to offset its smaller `--b-text-xs` font vs the body's `--b-text-sm`), so text-only tables matched within a pixel. The visible gap appeared in `b-data-table`, whose body cells carry **controls** — the `size="sm"` row-action `⋮` `b-button` (`b-data-table.ts:451`), selection checkboxes, badges — whose intrinsic height (~24–26px) inflates `tbody` rows past the ~33px header. `vertical-align: middle` centred them but couldn't shrink the band.
- **`--b-table-row-height` token** — both `th` and `td` now set `height: var(--b-table-row-height, var(--b-control-min-height, 2.375rem))` (38px) with `box-sizing: border-box`, `vertical-align: middle`, and vertical padding dropped to `0` (horizontal `--b-space-md` kept). The fixed band height now drives the row; single-line content — plain text, `size="sm"` *and* default `b-button`s (both fit under 38px), badges, checkboxes, inline-edit inputs — all centre within one consistent height, header included. Wrapped/multi-line content still grows the row gracefully; the empty-state `.empty` cell keeps its `--b-space-3xl` padding (class beats element selector)
- **Consumer override** — set `--b-table-row-height` on the `<b-table>`/`<b-data-table>` instance (or globally on `:root`) for denser/airier grids, e.g. `--b-table-row-height: var(--b-control-min-height-sm)` (28px). Works on `b-data-table` too since it wraps a `b-table`
- Documented in `Birko.Web.Components` README design-token table

## 2026-06-10 — Birko.Web — `finstat` theme + 5 new themeable tokens
Added a fourth built-in theme that reproduces the Finstat web app's brand look, so a Birko.Web app (e.g. a migrated Finstat admin SPA) can match the existing product visually. Adding it surfaced 5 design facets that had no `--b-*` slot — those are now first-class tokens (so any theme can set them), wired into the components with defaults that preserve the existing look.
- **`[data-theme="finstat"]` token block** in `Birko.Web.Components/css/tokens.css` — extracted from Finstat's LESS design tokens (`finstat/DevContent/less/`, catalogued in `finstat/LESS_TOKENS.md`). Brand green primary (`#25ba7a`, hovers *lighter* `#33db93` per Finstat's actual button behavior), warm-grey surfaces (page `#f3f2f0`, white cards), charcoal text (`#434040`), `"Roboto", Arial` font, blue focus glow (`#54b9e8`), and Finstat's signature **flat/square corners** (`--b-radius*: 0`, since its `@border-radius-*` are all `0px`). Shadows mirror Finstat's `@box-shadow` / `@box-shadow-dropdown`. `-light` tints and darker danger-hover are derived (Finstat had no token for those slots)
- **5 new tokens** (defined in `:root` with current-look defaults, overridden in `finstat`):
  - **`--b-font-heading`** — title/heading family (default `var(--b-font)`); wired into the card header (`b-card`) and the shared `overlayHeader` sheet (`b-modal` / `b-drawer` titles). Finstat → `"Roboto Condensed"`
  - **`--b-table-header-bg` / `--b-table-header-text` / `--b-table-header-text-hover`** — `b-table` `<th>` band (defaults `var(--b-bg)` / `var(--b-text-secondary)` / `var(--b-text)`). Finstat → dark `#434040` band + white text (`@table-header-bg` / `@table-header-color`)
  - **`--b-row-hover-bg`** — `:host([hoverable])` row hover in `b-table` (default `var(--b-bg-tertiary)`). Finstat → warm-yellow `rgba(253,222,129,.3)` (`@clr-hover`)
- Documented in `Birko.Web.Components` README + CLAUDE.md

## 2026-06-10 — Birko.Web — modular, per-project opt-in themes
Made themes pluggable so each consumer app ships only the theme tokens it actually uses, instead of every theme sitting in one `tokens.css`. **Behavior change:** the switcher no longer hard-codes light/dark/neon/finstat — apps register what they want.
- **CSS split** — `Birko.Web.Components/css/tokens.css` now holds only the base/light `:root` tokens (+ reduced-motion). Each alternate theme moved to its own file under `css/themes/` (`dark.css`, `neon.css`, `finstat.css`). A project links/bundles `tokens.css` + only the theme files it uses; unused theme bytes never load. A project-private theme is just a `[data-theme="my-brand"]` block in the app's own CSS — no framework edit
- **Theme registry** (`Birko.Web.Shell/src/shell/theme-registry.ts`) — `registerTheme()` / `registerThemes()` / `unregisterTheme()` / `getRegisteredThemes()` + `BUILTIN_THEMES` (ready-made `{id,label,icon}` for light/dark/neon/finstat — metadata only, CSS still opt-in). `'light'` is always present and can't be removed. Exported from `birko-web-shell`
- **Shell wiring** — `BCoreAppShell.getAvailableThemes()` now returns `getRegisteredThemes()` (was a hard-coded array). `renderThemeDropdown()` auto-hides the switcher when fewer than 2 themes are registered (a single-theme app has nothing to switch). App bootstrap: `registerThemes([BUILTIN_THEMES.dark, BUILTIN_THEMES.finstat])` + link the matching CSS
- **Migration** — apps that relied on the old default switcher must add a one-line `registerThemes([...])` in bootstrap (and link the theme CSS) to restore the entries they want
- Documented in `Birko.Web.Components` README/CLAUDE.md + `Birko.Web.Shell` README (new "Themes" section)
- **Base-token upstreaming** (from the Symbio consumer) — promoted genuinely-universal tokens into Birko base `:root` so consumers need fewer local overrides: a **status-alpha** system (`--b-color-{danger,success,warning,info}-alpha-bg/-border`), a **neutral overlay** system (`--b-overlay-subtle/light/medium`), and scale extensions (`--b-text-4xl`, `--b-icon-2xl`, `--b-border-width-thick`). `dark.css`/`neon.css` retune alpha+overlay for dark surfaces. Also fixed a latent z-index bug: `--b-z-dropdown` `100 → 220` so menus overlay sticky bars (`--b-z-sticky: 200`). Symbio now imports neon/finstat (which carry their own alpha/overlay) and a build-time token-parity guard warns when Birko base adds tokens Symbio's `:root` lacks

## 2026-06-08 — Birko.Web.Shell — `renderHeaderActions()` hook
Added a first-class extension seam for app-specific header controls, replacing the prior practice of overriding `renderThemeDropdown()` to smuggle in unrelated buttons.
- **`BCoreAppShell.renderHeaderActions(): string`** — new `protected` hook, returns `''` by default. Rendered at the left edge of the header action cluster (before the theme switcher and user area) in all three layouts: the core/minimal `renderHeader()`, the sidebar shell (reuses core's header), and `BAppShell`'s ribbon `after-tabs` slot. The header is not re-rendered by `refresh*()`, so subclasses wire returned controls once in `onMount()` and keep mutable state in sync from store subscriptions
- **Why** — `renderThemeDropdown()` is documented as rendering only the theme switcher, and the shell's `refreshThemeMenu()`/`_setupThemeDropdown()` assume its markup *is* the switcher. The old hack (prepend custom markup, chain `super.renderThemeDropdown()`) silently dropped the custom controls whenever `showThemeSwitcher` was false, and hid the extension point from the base class
- **Consumer migration** — the gameshow control shell (`gs-control-shell`, the only consumer that was overriding `renderThemeDropdown`) now overrides `renderHeaderActions()` for its key-color picker + reload button; its `onMount` wiring is unchanged. `renderThemeDropdown()` remains for legitimately restyling the switcher itself. Documented in `Birko.Web.Shell` CLAUDE.md + README

## 2026-06-08 — Birko.Web.Components — b-color-picker
New `inputs` component backported from the Gameshow control surface (its chroma-key backdrop picker, previously a raw native `<input type="color">`).
- **`<b-color-picker>`** — pairs a native color swatch (reskinned with `--b-*` tokens; the swatch opens the OS color dialog) with a monospace hex text field. Both stay in sync: dragging the swatch live-previews into the text field, typing a valid hex live-previews on the swatch, and either control commits on `change`.
- Accepts loose hex input (`#rgb`, `rgb`, `#rrggbb`, `rrggbb`); the canonical `value` and the `change` detail are normalized to lowercase `#rrggbb`. Bad hex snaps back to the current value on commit.
- **Opt-in `alpha`** — adding the attribute shows an opacity slider (native `<input type="range">`, track tinted over a token checkerboard so opacity reads visually) and switches the canonical value to 8-digit `#rrggbbaa`; the text field then accepts `#rgba`/`#rrggbbaa` too. The native swatch is sRGB-only, so RGB comes from the swatch and the alpha byte from the slider. Typing a 6-digit hex in alpha mode preserves the current slider alpha.
- Two-event contract mirroring native: `input` = ephemeral live preview during any drag/typing (no attribute reflection → no re-render storm), `change` = committed (attribute reflected). Standard form-input surface: `formFieldSheet` + `formControlSheet`, `label`/`hint`/`error`/`required`/`disabled`, `size` (vertical-footprint — swatch matches `--b-control-min-height*`), i18n via `bwc.colorPicker.*`.

## 2026-06-05 — Birko.Web.Components — b-button-group + b-toolbar
Two new layout components backported from the Gameshow control surface (its contest transport controls):
- **`<b-button-group>`** — bordered, padded, rounded cluster (`--b-bg-secondary` fill, `--b-radius-lg`) that makes related `b-button`s read as one unit (e.g. Start/Pause/Stop). Purely presentational: `role="group"`, optional `label` → aria-label, default slot only — slotted buttons keep their own variant/size/clicks.
- **`<b-toolbar>`** — flex row (`--b-space-lg` gap, wraps) laying out the clusters; `role="toolbar"`, optional `label`. An `end` slot pushes content to the far edge — the conventional spot for destructive/exit actions (the end container hides itself when empty so no phantom trailing gap, same slotchange pattern as `b-card`).

## 2026-06-05 — Birko.Web.Shell — user area hides for anonymous apps
`BCoreAppShell.renderUserDropdown()` (inherited by `BSidebarAppShell` + `BAppShell`/ribbon) now follows the shell's return-value feature-toggle convention instead of always rendering a dropdown:
- **`getUserName()` returns `''`** → the whole user area (avatar + name + dropdown) is omitted — for anonymous apps (kiosks, public dashboards, ribbon apps without auth)
- **`getUserMenuItems()` returns `[]`** → static avatar + name badge (`.user-trigger.is-static`, no pointer cursor/hover) instead of a dropdown that opened an empty list
- `refreshUserMenu()` no-ops when items are empty; switching anonymous ↔ signed-in needs a full `update()` (apps already re-render on auth change since the username is baked into `render()`)

## 2026-05-30 — Birko.Web — `neon` theme + header theme switcher
Added a third built-in theme and a built-in theme switcher to all shells.
- **`neon` theme** — new `[data-theme="neon"]` token block in `Birko.Web.Components/css/tokens.css`: dark navy base (`#0b0f1a`), neon green primary (`#8cffb0` with dark `#06301a` text), magenta danger (`#ff4466`), cyan info (`#66e0ff`), amber warning (`#ffaa44`). Mirrors the `dark` theme's override set plus neon focus glows and an `--b-input-thumb-bg` flip
- **`--b-bg-gradient` token** — only defined in the `neon` theme (`radial-gradient(circle at 50% 30%, #16306a 0%, #0b0f1a 70%)`); the shell content area uses `var(--b-bg-gradient, var(--b-bg-secondary))` so light/dark are unaffected and any theme can opt into a radial backdrop
- **Theme switcher** — `BCoreAppShell` now renders a theme dropdown in the header (inherited by `BSidebarAppShell` + `BAppShell`/ribbon). New API: `setTheme(id)` (applies `data-theme`, persists to `{storagePrefix}-theme`, emits `theme-change`), `currentTheme` getter, `getAvailableThemes()` (override to add/localize), `themeMenuLabel`, `showThemeSwitcher`, `renderThemeDropdown()` helper, `refreshThemeMenu()`. Active theme shown with a checkmark; trigger shows the active theme's glyph. Reuses the existing `data-theme` restore path — no new persistence wiring
- New `ThemeOption` type in `shell-types.ts`. Switcher works out-of-box with English labels (no consumer i18n keys required)

## 2026-05-29 — Birko.Security.OAuth.Server — OAuth2 authorization server
New shared project that issues tokens — complements the existing `Birko.Communication.OAuth` client. Pure handler library (no ASP.NET dep); a host wires the four handlers to whatever HTTP framework it's running. Closes [tasks/EPIC-009/STORY-020/TASK-027](tasks/EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md).
- **Four grant types** — `client_credentials`, `authorization_code` (+ PKCE), `refresh_token`, RFC 8628 `urn:ietf:params:oauth:grant-type:device_code`. `password` and implicit are intentionally not supported (deprecated by OAuth 2.1)
- **Composition root** — `OAuthServer` owns one handler per endpoint: `Token`, `Authorize`, `DeviceAuthorization`, `ClientRegistration`. Hosts route their HTTP endpoints to those handlers
- **Persistence via `Birko.Data.Stores`** — five new entity models (`OAuthClient`, `AuthorizationCode`, `RefreshTokenRecord`, `DeviceCodeRecord`, `ConsentRecord`) plus matching `IXxxStore : IAsyncStore<T>` interfaces with default-interface-method lookups (`GetByClientIdAsync`, `GetByCodeAsync`, `GetByHashAsync`, …). Any backend that implements `IAsyncStore<T>` works
- **Security defaults** — PKCE required for public clients (`RequirePkceForPublicClients` = true), refresh-token rotation enabled (`RotateRefreshTokens` = true, per RFC 6819 §5.2.2.3). Client secrets and refresh tokens stored as SHA-256 hashes with `CryptographicOperations.FixedTimeEquals` verification — plaintext only exists in transit. Authorization codes are single-use (`Used` flag set on redemption)
- **Settings** — `OAuthServerSettings : Settings` (extends `Birko.Configuration.Settings`) — `AccessTokenLifetimeSeconds` (3600), `RefreshTokenLifetimeSeconds` (14d), `AuthorizationCodeLifetimeSeconds` (60), `DeviceCodeLifetimeSeconds` (600), `DeviceCodePollingIntervalSeconds` (5), `Issuer`, `RotateRefreshTokens`, `RequirePkceForPublicClients`, `SupportedScopes`
- **Token signing** — composes with existing `Birko.Security.Jwt` `JwtTokenProvider` (or any `ITokenProvider`). The handler builds the claims dictionary (`sub`, `client_id`, `iss`, `scope`) and delegates to the provider
- **RFC 8628 device flow** — `DeviceAuthorizationHandler` issues `device_code` + ambiguity-free `user_code` (alphabet `BCDFGHJKMNPQRSTVWXYZ23456789`); `ApproveAsync(userCode, userId, approved)` is the consent-UI bridge. Token endpoint enforces poll interval as `slow_down`, `authorization_pending` until the user clicks Allow, `access_denied` on Deny, `expired_token` after `DeviceCodeLifetimeSeconds`
- **Dynamic client registration** (RFC 7591) — `ClientRegistrationHandler.RegisterAsync` returns the plaintext secret once at registration; subsequent `GetAsync` calls omit it. `ClientType` is immutable post-creation. Host gates this endpoint behind its own admin-only policy
- **43 xUnit + FluentAssertions tests** — one success + one failure path per grant type plus PKCE/redirect-URI/replay/expiry edges, full device-flow lifecycle, all four `OAuthErrorCodes` callsites exercised
- **Registered in** `Birko.Framework.slnx` (Security folder + Tests folder), `Birko.Framework.code-workspace`, and `Birko.Framework.csproj` aggregator

## 2026-05-28 — Task tracking — `tasks/` folder + `/tasks` skill
Introduced hierarchical task tracking (Epics → Stories → Tasks) as markdown files under `tasks/`. Pilot import migrated all open work from the former `TODO.md` into a structured backlog: **12 EPICs, 22 STORIES, 34 TASKs**, organized by area of concern (Web.Components polish, Data.Redis, Caching.NCache, Storage cloud providers, Messaging/MessageQueue expansion, Telemetry exporters, Health checks, Communication protocols, RavenDB index ergonomics, Test coverage gaps, MQTT v5). Each TASK is self-contained (Context / Acceptance criteria / Out of scope) so a human or AI agent can pick it without re-discovery. Old `TODO.md` removed (history preserved in git).
- **Skill location** — `~/.claude/skills/tasks/` (global, reusable across projects)
- **Verbs** — `/tasks new`, `/tasks triage`, `/tasks pick`, `/tasks close <ID>`, `/tasks import <file|--github|--jira>`, `/tasks export <ID> --to github|jira`, `/tasks migrate --to github|jira`
- **Modes** — `local` (file-only, default) and `hybrid` (files + GitHub Issues / Jira export via `gh` CLI / Atlassian MCP). Configured per project in `tasks/.config.yml`
- **Shape detection** — meta-repo (Birko.Framework) uses per-sub-project `Birko.X/tasks/` for project-local work and root `tasks/` for cross-cutting epics with `affects: [Birko.AI, Birko.Data, ...]`; consumer solutions use solution-root `tasks/`
- **Lifecycle** — status-only, no archiving. Dashboard hides done by default; epics/stories are often open-ended areas of concern
- See [tasks/README.md](tasks/README.md) for the live dashboard

## 2026-05-28 — Birko.AI.Agents — Prompt convention realignment
Audited all 20 agents in `Birko.AI.Agents` against Anthropic's "Building Effective Agents" principles (simplicity, transparency, well-documented tools) and the helper pattern in `Agent.cs` (`GetDepthGuidance()` virtual + `GetFileOperationGuidelines()` + `GetCommonBestPractices()` statics). Realigned 5 outlier agents so the catalogue is consistent.
- **`RefactorAgent` / `TestAgent` / `DebugAgent` / `DocumentationAgent`** — were inlining a local `Options.ModelDepth switch` directly in `SystemPrompt`'s getter, bypassing the `protected virtual string GetDepthGuidance()` hook in `Agent.cs:126`. Now each `protected override`s the method (same shape `OrchestratorAgent` already uses) and the prompt interpolates `{GetDepthGuidance()}` like every other agent. Depth-behavior tuning is now in one method per agent instead of buried in a string literal
- **`TestAgent` guideline list** trimmed 24 → 9 bullets (removed redundant phrasing — AAA pattern, behavior-over-implementation, boundary values, mocking, pyramid, regression tests, clean code). System prompt down from ~100 lines to ~70
- **`RefactorAgent` guideline list** trimmed 22 → 9 bullets (consolidated duplicate "preserve behavior", "small steps", "no bug fixes during refactor" mentions). System prompt down from ~95 lines to ~65
- **`{GetCommonBestPractices()}`** now interpolated by `RefactorAgent`, `TestAgent`, `DebugAgent`, `DocumentationAgent`, `DiagrammingAgent` — previously these 5 silently dropped the shared best-practices block (test, retry on failure, be methodical). Replaced redundant ad-hoc "be systematic and methodical" closers with the helper
- **`HtmlCodingAgent` / `CssCodingAgent`** — step 4 was "Test your changes by viewing rendered output", which the agent loop cannot do (no browser tool). Replaced with "Validate by re-reading the file (structure/syntax/specificity)" — reachable with existing tools
- Reference for future audits: [`design-agent` skill](~/.claude/skills/design-agent/SKILL.md) encodes both the Anthropic pattern ladder and the Birko.AI prompt template + review checklist

## 2026-05-26 — Birko.Web.Components — b-date-range-picker

Added `<b-date-range-picker>` as the 21st input component (total components now 55). Selects a date range with two endpoints in one panel; the API mirrors existing `b-*` input conventions (single `value`, `inputValue` contract, `change` event with `{name, value: {start, end}}` payload — same shape as `b-range` range mode).

- **Two-month side-by-side panel** by default; `months-visible="1"` for narrow viewports. Single `value="start/end"` ISO interval attribute (uniform with all other inputs); `getRange()` / `setRange({start, end})` typed accessors mirror the `getSelected()` / `setSelected([])` family. Static `BDateRangePicker.setLocale({months, days, today, clear, apply, cancel, presets})` matches the other date components.
- **Two commit modes** — default is instant commit on second click; `confirm` boolean attribute switches to an Apply-button footer (Apply enabled only when both endpoints set, Cancel reverts pending range).
- **Range painting via `data-range` attribute** (`start | end | in | hover-in | hover-end`) — JS only mutates the attribute, CSS does all the visual work via `::before` pseudo-elements. Smooth hover preview as the user mouses over potential end dates without `innerHTML` thrash. Emits `range-preview` `{ start, end }` during pick, `change` `{ name, value: { start, end } }` on commit.
- **Constraints** — `min` / `max` (hard date bounds), `min-days` / `max-days` (range length; auto-extend / clip on second click). `min-days="0"` default allows same-day ranges; auto-swap if user picks `end < start`.
- **Opt-in presets** — `presets='[{"label":"bwc.daterange.preset.last7days","start":"-7d","end":"today"}]'` JSON; no footer if `presets` attribute absent (no default list). Token resolver handles `today`, `yesterday`, `month-start`/`-end`, `year-start`/`-end`, `quarter-start`, relative `±Nd/w/m/y`, and any ISO date.
- **Native fallback** (`native` attribute) renders two `<input type="date">` posting as `${name}-start` / `${name}-end`.
- **b-form integration** — new `date-range` `FieldType`, `_getFieldValue` returns `{ start, end } | null`, `_setFieldValue` accepts object or interval string. `FormField` gains `minDays`, `maxDays`, `monthsVisible`, `confirm`, `presets`, `separator` props.
- **i18n** — new `bwc.daterange.*` keys (`placeholderStart`, `placeholderEnd`, `apply`, `presets`, `nightsCount`, `daysCount`, `preset.*`) in `locales/en.json`.

## 2026-05-24 — Birko.Models.SQL Split into Framework + Domain Siblings

Decoupled the fluent SQL mapping framework from the canonical domain mappings. `Birko.Models.SQL` now contains only `Mapping/` (ModelMap, FieldBuilder, IModelMapping, ModelMapRegistry — ~150 LOC). Consumers can pick exactly the domains they persist.

- **5 new sibling shared projects** — `Birko.Models.Users.SQL` (8 mappings: User, UserLogin, UserProfile, UserRole, UserTenant, Role, RolePermission, Tenant), `Birko.Models.Customers.SQL` (Address+InvoiceAddress+ContactPerson, Customer), `Birko.Models.Inventory.SQL` (StockItem, StorageLocation, InventoryDocumentLine), `Birko.Models.Pricing.SQL` (Currency, Tax, PriceGroup), `Birko.Models.Product.SQL` (MeasureUnit, UnitConversion, ProductPartnerCode)
- **`CurrencyMapping.cs` split** — the old file mixed Pricing (Currency/Tax/PriceGroup) with Product (MeasureUnit/UnitConversion). Now in their respective domain projects; new `MeasureUnitMapping.cs` in `Birko.Models.Product.SQL`
- **Namespaces renamed** — `Birko.Models.SQL.Mappings` → `Birko.Models.{Domain}.SQL.Mappings` (e.g. `Birko.Models.Users.SQL.Mappings.TenantMapping`). Consumers using `RegisterFromAssembly(typeof(SomeMapping).Assembly)` need to update the type anchor (Symbio updated in same change)
- **Aggregator imports** — `Birko.Framework.csproj` and `Symbio.Birko.csproj` now `<Import>` the 5 new projitems alongside the existing `Birko.Models.SQL.projitems`. Consumers that don't persist a given domain can simply omit that domain's `.SQL` import
- **Why** — importing `Birko.Models.SQL` previously forced you to also import `Birko.Models.Users` + `.Customers` + `.Inventory` + `.Pricing` + `.Product` (the 5 domain projects whose canonical mappings live there), regardless of which models you actually use. Split removes that coupling

## 2026-05-19 — Birko.Serialization.Yaml

Added YAML serializer sibling project alongside `.Newtonsoft` / `.MessagePack` / `.Protobuf`.

- **`YamlDotNetSerializer`** — implements `ISerializer` over YamlDotNet; `ContentType` = `application/yaml`, `Format` = `SerializationFormat.Yaml`
- **`SerializationFormat.Yaml`** added to the enum
- Constructor accepts optional `YamlDotNet.Serialization.ISerializer` / `IDeserializer` to override the default pipeline (camelCase + `IgnoreUnmatchedProperties()`)
- Stream overloads wrap UTF-8 `StreamReader`/`StreamWriter` (`leaveOpen: true`); async methods are sync-wrapped since YamlDotNet has no async API
- 13 new tests in `Birko.Serialization.Tests/Yaml/` (xUnit + FluentAssertions)

## 2026-04-28 — Birko.Security.AspNetCore — Per-Request Permissions + JWT-from-Query

ASP.NET Core integration gained an opt-in path for hosts whose effective permission sets are too large to embed in a JWT, plus query-string token retrieval for SSE/WebSocket clients that cannot set headers.

- **`IUserPermissionResolver`** (`Authorization/IUserPermissionResolver.cs`) — host-supplied service returning the effective permission set for `(userId, tenantId)`; typically backed by the app's role store + cache
- **`PermissionResolutionMiddleware`** — runs after `UseAuthentication`, invokes the resolver once per request, stashes the set in `HttpContext.Items`
- **`ResolvedPermissionsCurrentUser`** — `ICurrentUser` that reads identity claims like `ClaimsCurrentUser` but pulls `Permissions` from the middleware slot (sync getter, no per-call DB hit)
- **Opt-in DI** — `services.UseResolvedPermissions()` swaps the registration; `app.UseBirkoPermissionResolution()` inserts the middleware. Default `ClaimsCurrentUser` behavior unchanged unless host opts in
- **JWT from query string** — `JwtBearerExtensions` now wires `OnMessageReceived` to extract `?token=…` for `/api/sse` and `/ws` paths (EventSource cannot set custom headers)
- **Comma-joined claim values** — `ClaimsCurrentUser.Permissions`/`Roles` now split each claim value on `,` so both shapes work: multiple same-name claims and a single comma-joined claim (fixes superadmin `*` bypass when multiple permissions packed into one claim)
- **`TenantId → TenantGuid`** rename across `ICurrentUser` and related types (originally 2026-03-15)

## 2026-04-24 — Birko.Web — Unified i18n

All three Birko.Web.* packages share a single global i18n singleton — no more per-component `this.attr('label-X', 'English')` islands or one-off `setTranslate` hooks.

- **`birko-web-core` exports** `i18n` (default `I18n` instance), `t(key, params?, fallback?)`, `useI18n(instance)` (swap in an app-owned instance), `onI18nChange(fn)` (subscribers auto re-wire on swap), plus the existing `I18n` class, `createFormatter`, `getFormatter`
- **`BaseComponent.label(attrName, i18nKey, fallback, params?)`** — new helper: explicit attribute wins > global i18n lookup > English fallback; all `bwc.*`-prefixed keys interpolate `{param}` placeholders; `BaseComponent` auto-subscribes to `onI18nChange` so components re-render on `setLocale()`
- **`BaseComponent.listen<T extends Event>(...)`** — now generic so consumers can pass `(e: KeyboardEvent) => void` without casts
- **~150 call sites migrated** across command-palette, ribbon, sidebar, tree-menu, pagination, toast, empty, confirm-dialog, modal, drawer, spinner, file-upload, search-input, json/xml-viewer, object-tree, table, markdown-editor, datetime-picker, time, date-picker
- **Canonical key namespaces** — `bwc.*` for Components (`bwc.common.close`, `bwc.palette.placeholder`, `bwc.pagination.prev`, etc., shipped in `Birko.Web.Components/locales/en.json`); `bws.*` for Shell (`bws.common.new`, `bws.common.confirmDelete`, `bws.pagination.items`, `bws.ribbon.selectModule`). Shell's `t()` auto-interpolates `{entity}` with `this.entityLabel` so bundle entries like `"bws.common.new": "Nový {entity}"` produce localized entity-specific strings
- **`b-app-shell.ts` simplified** — no longer passes `label-*` attributes to `<b-ribbon>` / `<b-command-palette>`; those components pull from `bwc.*` global i18n directly
- **Backward-compatible shims preserved** — `BForm.setTranslate(fn)` still works (forwards to legacy path), `BDatePicker.setLocale(...)` / `BDatetimePicker.setLocale(...)` / `BTime.setLocale(...)` still win over global i18n for per-class month/day overrides, `base-crud-page.t(key)` still returns English defaults and can still be overridden
- **Library ergonomics tuned** for strict-mode consumer apps: `TableColumn.render` now accepts `any`-typed callbacks, `FormGroupDef.layout`/`TableColumn.align`/`FieldType`/`RuleType` widened via `(string & {})` so inline object literals type-check
- **Consumer migration** — one line: `useI18n(mineI18n)` in app bootstrap. Existing `label-*` attributes keep working unchanged

## 2026-04-24 — Provider-Specific Settings Classes

Created typed settings descendants for all store providers, replacing hardcoded configuration with per-instance settings. Stores and connectors now read from typed settings instead of static properties or inline constants.

**New settings classes:**
- `SqlSettings` (Birko.Data.SQL) — `CommandTimeout`, `ConnectionTimeout`, abstract `GetConnectionString()`
- `MSSqlSettings` (Birko.Data.SQL.MSSql) — `MultipleActiveResultSets`, `TrustServerCertificate`; overrides `GetConnectionString()`
- `MySqlSettings` (Birko.Data.SQL.MySQL) — `BulkInsertBatchSize` (previously hardcoded `const`); overrides `GetConnectionString()`
- `PostgreSqlSettings` (Birko.Data.SQL.PostgreSQL) — `UseBinaryImport`; overrides `GetConnectionString()`
- `SqLiteSettings` (Birko.Data.SQL.SqLite) — extends `PasswordSettings` (not `SqlSettings`), `CommandTimeout`; virtual `GetConnectionString()`
- `Birko.Data.CosmosDB.Stores.Settings` — `PartitionKeyPath`, `RequestTimeout`, `AllowBulkExecution`, `GetCosmosClientOptions()`; `CreateDocumentStore()` helper
- `Birko.Data.RavenDB.Stores.Settings` — `RequestTimeout`, `CreateDocumentStore()` helper

**Settings hierarchy (final):**
```
Settings → PasswordSettings → RemoteSettings → SqlSettings → MSSqlSettings / MySqlSettings / PostgreSqlSettings
                                                      → CosmosDB Settings / RavenDB Settings
PasswordSettings → SqLiteSettings
SqlSettings → TimescaleDBSettings
```

**Store changes:**
- CosmosDB stores: `ISettingsStore<RemoteSettings>` → `ISettingsStore<Settings>`, removed static `PartitionKeyPath`/`RequestTimeout`
- RavenDB stores: `ISettingsStore<RemoteSettings>` → `ISettingsStore<Settings>`, removed static `RequestTimeout`
- SQL connectors: `CreateConnection` checks for typed settings first, uses `GetConnectionString()` when available
- TimescaleDB `Settings`: now extends `SqlSettings` instead of `RemoteSettings`
- Migration settings: `SqlMigrationSettings` extends `SqlSettings`; `CosmosMigrationSettings`/`RavenMigrationSettings` extend their provider `Settings`

**Downstream consumers updated:**
- BackgroundJobs (SQL, CosmosDB, RavenDB) — switched to typed settings
- Workflow (SQL, CosmosDB, RavenDB) — switched to typed settings

**Bug fix:** `AsyncRavenDBStore` previously ignored `RequestTimeout` entirely — now reads from `_settings.RequestTimeout` via `CreateDocumentStore()`.

---

## 2026-04-23 — Platform-Agnostic Migrations + FieldDescriptor Unification

Rewrote the migration system so migrations are written once and run against any provider. Unified `PropertyMap` (Birko.Models.SQL) with `FieldDescriptor` (Birko.Data.Patterns) into a single type.

**Migration system:**
- `IMigration` now has `Up(IMigrationContext context)` / `Down(IMigrationContext context)` — no more provider-specific base classes
- `IMigrationContext` provides `Schema` (ISchemaBuilder), `Data` (IDataMigrator), `Raw(Action<object>)`, `ProviderName`
- Schema abstractions in Birko.Data.Patterns: `FieldType` enum, `FieldDescriptor`, `ISchemaBuilder`, `ICollectionBuilder`, `IIndexBuilder`
- Each provider implements IMigrationContext: SQL (wraps DbConnection + AbstractConnector), MongoDB (IMongoDatabase), ElasticSearch (ElasticClient), RavenDB (IDocumentStore), CosmosDB (Database), InfluxDB (InfluxDBClient), TimescaleDB (extends SQL)
- NoSQL providers silently skip inapplicable operations (AddField/DropField are no-op on schema-less databases)
- Runner constructors take the store's native connector: `new SqlMigrationRunner(store.Connector)`, `new MongoMigrationRunner(store.Client)`
- Deleted provider-specific base classes: SqlMigration, MongoMigration, ElasticSearchMigration, RavenMigration, CosmosMigration, InfluxMigration

**FieldDescriptor unification:**
- `PropertyMap` (Birko.Models.SQL.Mapping) deleted — `FieldDescriptor` (Birko.Data.Patterns.Schema) now serves both model mapping and migrations
- `PropertyMapBuilder<T>` renamed to `FieldBuilder<T>` — wraps FieldDescriptor with fluent API
- Added to FieldDescriptor: ColumnName, IsIgnored, IndexName, IndexOrder, IndexDescending
- Changed FieldDescriptor from `init` to `{ get; set; }` to support the builder pattern
- `ModelMap<T>` and `ModelMapRegistry` updated to use FieldDescriptor
- All 31 consumer mapping files unchanged (fluent API surface is identical)

---

## 2026-04-23 — Birko.Communication.GraphQL

New GraphQL client project following Birko.Communication.OAuth patterns. Zero external NuGet dependencies — uses HttpClient for queries/mutations, ClientWebSocket for subscriptions, and Birko.Serialization (SystemJsonSerializer) for JSON.

**Components:**
- `GraphQLSettings` extends `RemoteSettings` (Endpoint = Location alias). Adds SchemaPath ("/graphql"), UseSubscriptions, SubscriptionProtocol enum (WebSocket/SSE), TimeoutSeconds (30), EnableAutoPersistedQueries, ExtraHeaders.
- `IGraphQLClient` interface with `QueryAsync<T>`, `MutateAsync<T>`, `SubscribeAsync<T>`, `ExecuteAsync<T>` plus OnRequest/OnResponse/OnError events.
- `GraphQLClient` implementation — static `GetClient(endpoint)` caching (RestClient pattern), optional HttpClient injection, thread-safe via SemaphoreSlim. Uses `ISerializer` for all JSON operations.
- `GraphQLRequest` — serializable request model with Query, Variables, OperationName, Extensions. Serialize via ISerializer.
- `GraphQLResponse<T>` — typed response with Data, Errors, Extensions. Static Deserialize factory.
- `GraphQLError` — error model with Message, Locations (line/column), Path, Extensions.
- `GraphQLSubscription<T>` — IObservable<T> over ClientWebSocket using graphql-ws protocol. IDisposable.
- `GraphQLRequestBuilder` — fluent API: Query(), Mutation(), Variables(), OperationName(), WithExtension(), Build().
- `GraphQLException` — mirrors OAuthException with Errors list and StatusCode.

**Tests:** 49 tests in Birko.Communication.GraphQL.Tests (xUnit + FluentAssertions).

---
## 2026-04-22 — Birko.Web.Components — Markdown Editor Formatting

Extended `b-markdown-editor` with all missing formatting options:
- **Heading dropdown** — single H button replaced with dropdown panel showing H1–H6 levels with markdown hints (`#` through `######`); positioned below button, closes on outside click
- **Table insertion** — toolbar button inserts 2-column GFM table template (`| Header | Header |`); renderer already handled GFM tables
- **Task list** — toolbar button inserts `- [ ] task` checkbox item; renderer converts `- [ ]` / `- [x]` to `<li class="task-list-item">` with styled checkbox inputs; handled before general unordered list regex to avoid conflicts
- **Highlight** — `==text==` wraps in `<mark>` tag; pandoc extension; styled with `--b-color-warning-light` background; added to Word HTML cleanup (`<mark>` → `==text==`)
- **Superscript** — `^text^` wraps in `<sup>` tag; pandoc extension; added to Word HTML cleanup (`<sup>` → `^text^`)
- **Subscript** — `~text~` wraps in `<sub>` tag; pandoc extension; single-tilde syntax doesn't conflict with double-tilde strikethrough (`~~`); added to Word HTML cleanup (`<sub>` → `~text~`)
- **Preview CSS** — `.preview-content mark` with warning-light background, `sup`/`sub` with 0.75em sizing, `.task-list-item` with no bullet and styled checkbox using `--b-color-primary` accent; all values use `--b-*` tokens

## 2026-04-22 — Birko.Web.Components — b-kanban Card Nesting

Extended `b-kanban` with recursive card nesting support:
- **Data model** — `KanbanCard` gains `parentId`, `collapsed`, and `children` fields; `KanbanConfig.renderCard` signature updated to `(card, depth) => string` for depth-aware custom rendering; `maxNestingDepth` config option limits recursion
- **Expand/collapse** — `_expanded` Set tracks parent card state across re-renders; toggle button (`.card-toggle`) follows b-tree-menu pattern; public API: `toggleCard`, `expandCard`, `collapseCard`, `expandAll`, `collapseAll`
- **3-zone drag-and-drop** — Cards support `drop-before` / `drop-inside` / `drop-after` zones (top 25% / middle 50% / bottom 25% of card height, same as b-tree-menu); dropping inside a card sets `parentId` on the moved card and nests it; descendant-drop prevention
- **Nested DnD** — All nested cards are draggable; `moveCard` accepts optional `targetParentId` parameter for nesting operations; card-move/card-reorder events include parent context
- **Keyboard navigation** — ArrowRight on parent: expand or focus first child; ArrowLeft: collapse or focus parent; ArrowLeft on root-level card: move to previous column; flat up/down/home/end across all visible cards
- **Nesting API** — `addSubCard(parentId, card)`, `getChildren(cardId)`, `removeCard` removes card and all descendants recursively
- **CSS** — `.card-children` container with dashed `border-left` guide (using `--b-border` token), `.card-toggle` expand/collapse button, `.card-child-count` badge, `.card-header` flex row, `.drop-inside` outline highlight; all spacing, colors, radii, transitions use existing `--b-*` tokens

## 2026-04-22 — Birko.Web.* — Design Token Audit + Tokenization Cleanup

Swept `Birko.Web.Core`, `Birko.Web.Components`, `Birko.Web.Shell` for bare CSS values that should be tokens and filled the gaps:

- **New tokens in `tokens.css`** — `--b-space-2xs: 0.125rem` (2px fixed spacing); `--b-input-thumb-bg: #ffffff` (always-white thumbs/glyphs on colored active states); popover / picker / dropdown dimension tokens: `--b-date-picker-width` (17rem), `--b-time-picker-width` (11rem), `--b-tooltip-max-width` (16rem), `--b-dropdown-min-width` (10rem), `--b-kanban-col-min-width` / `--b-kanban-col-max-width` (16/22rem), `--b-dropzone-icon-size` (3rem), `--b-file-thumb-size` (5rem), `--b-filter-chip-width` / `--b-filter-chip-width-lg` / `--b-filter-chip-width-xl` (12/16/24rem), `--b-app-brand-max-width` / `--b-app-user-max-width` (8/12rem).
- **Tokenized bare `#fff` on colored surfaces** — `b-ribbon` notification badges, `b-chat` outgoing bubble + send button, `b-checkbox` checkmark + indeterminate dash now use `var(--b-text-inverse)`; `b-switch` / `b-range` thumbs now use `var(--b-input-thumb-bg)`. Not a dark-mode bug (colored surfaces are theme-agnostic), but makes intent explicit and overridable per-theme if desired.
- **Tokenized rem/px dimensions** — `b-date-picker`, `b-datetime-picker`, `b-time`, `b-tooltip`, `b-kanban`, `b-file-upload`, `shared-styles.css` dropdown-panel, shell app-bars, and CRUD-page filter row now reference named dimension tokens instead of bare values.
- **Tokenized small spacing** — `b-badge`, `b-inline-edit`, `b-tree-menu`, `b-chat`, `b-table`, `b-checkbox`, `b-file-upload` now use `--b-space-2xs` for 2px offsets instead of raw `2px` / `0.125rem` literals.
- **Tokenized transition** — `base-crud-page.ts` sub-row border/shadow transition now uses `var(--b-transition)` instead of hardcoded `0.15s ease`.
- **Structural borders/outlines left as-is** — 2px/3px `border-bottom`, `outline`, `border-left` accents and focus-ring fallbacks are visual design constants, not spacing; tokenizing them would add indirection without benefit.
- **Form-control sizing unified** — new `--b-control-min-height: 2.375rem` (≈38px) token applied to `input`/`select`/`textarea` via `formControlSheet`, `.combo-container` via `comboControlSheet`, and `b-tag-input`'s container. Before: three different heights (`~2.375rem` / `2.25rem` / `2rem`). Now: `b-input` / `b-select` (plain + searchable) / `b-multi-select` / `b-tag-input` / `b-textarea` all share the same vertical footprint, border, radius, focus ring (`var(--b-border-focus)` + `var(--b-focus-ring)`), error ring, and disabled-state opacity (`var(--b-disabled-opacity)`).
- **Form-control `size="sm"` / `size="lg"` variants** — added tokens `--b-control-min-height-sm: 1.75rem` (≈28px, dense grids/toolbars) and `--b-control-min-height-lg: 2.75rem` (≈44px, touch targets). Applied via `:host([size="sm|lg"])` rules in `formControlSheet` / `comboControlSheet` / `b-tag-input`. Opt-in: `<b-input size="sm">`, `<b-select size="lg">`, etc. — no `observedAttributes` changes needed (pure CSS-attribute switch).
- **`size` attribute semantics documented** — `Birko.Web.Components/CLAUDE.md` now has a five-category convention table (vertical-footprint / text-scale / width / shape-weight / inline-chip) so new sizeable components pick the right interpretation. `b-button` normalized from class interpolation (`class="${size}"`) to the shared `:host([size="sm|lg"])` pattern used by every other component — fixed a latent bug where `loading=true` emitted two `class` attributes. `b-badge` gained a `sm` variant for symmetry with `lg`.

## 2026-04-22 — Birko.Web.Components — Sticky Headers + Shared Viewer Sheets

Extended the display-widget set added earlier on 2026-04-22 with unified sticky-header behavior and extracted shared CSS:

- **New attributes on `b-object-tree`** — `show-header` (opt-in card chrome + Expand/Collapse/Copy toolbar), `header-title` (default `Tree`), `no-copy`, `no-expand-actions`. When the header is shown the component gets the same card look as `b-json-viewer` / `b-xml-viewer` (bg-tertiary, border, radius).
- **New attributes on `b-object-tree`, `b-json-viewer`, `b-xml-viewer`, `b-code-block`** — `max-height` (internal scroll; body/pre becomes the scroll container) and `sticky-header="page"` (card overflow flips to visible so the `position: sticky` header pins to the page viewport instead). The two modes are mutually exclusive: `sticky-header="page"` takes precedence and ignores `max-height`.
- **New shared `@sheet` sections in `src/shared-styles.css`** — `dataViewerCard` (card shell + `.sticky-page` modifier), `dataViewerHeader` (compact sticky toolbar header with `.title` + `.actions`), `toolbarBtn` (small bordered action button with `.copied` state). Exported as `dataViewerCardSheet`, `dataViewerHeaderSheet`, `toolbarBtnSheet`.
- **Refactored viewers** — `b-object-tree` (when `show-header` is on), `b-json-viewer`, `b-xml-viewer`, and `b-code-block` now consume the three shared sheets via `static get sharedStyles()`; each component's local `styles` shrank by ~40–50 lines (removed duplicated card shell, header flex row, and toolbar button CSS).
- **Design rationale** — `b-card` is intentionally different (elevated bg, semibold text-lg header) so reusing it would misrepresent data-inspection widgets as content cards. A separate `dataViewerCard`/`dataViewerHeader`/`toolbarBtn` family keeps the two visual languages distinct while eliminating per-component duplication.

## 2026-04-22 — Birko.Web.Components — Display & Inspection Widgets

Added 7 new Shadow DOM components (42 → 50):

- **`b-pre`** (`src/data/b-pre.ts`) — preformatted text block with `wrap`, `max-height`, `size` attributes. Slot-based content, monospace, tokenized colors and spacing.
- **`b-code-block`** (`src/data/b-code-block.ts`) — syntax-highlighted code display with built-in lightweight highlighter for `json`, `js`, `ts`, `html`/`xml`, `css`, `sql`, `csharp`, `bash`. Supports `language`, `code`, `wrap`, `show-line-numbers`, `no-copy`, `max-height`, `size`. Emits `copy` event after clipboard write.
- **`b-definition-list`** (`src/data/b-definition-list.ts`) — semantic `<dl>` wrapper with `layout` variants (`stacked` default, `inline`, `horizontal`, `grid`). `setItems([{term,description}])` or slot-based usage.
- **`b-object-tree`** (`src/data/b-object-tree.ts`) — generic recursive property tree for any JS value. Lazy expansion via `expanded-depth`, upper bound via `max-depth`, optional `show-types`. Methods: `setData`, `getData`, `expandAll`, `collapseAll`. Emits `toggle` with path + expanded state.
- **`b-json-viewer`** (`src/data/b-json-viewer.ts`) — composes `<b-object-tree>` with JSON-specific UX: header with Expand/Collapse/Copy buttons, parse-error panel, `src` attribute for string input, accepts both strings (parsed) and objects via `setData`.
- **`b-xml-viewer`** (`src/data/b-xml-viewer.ts`) — parses XML via `DOMParser` and renders the DOM as a collapsible tree with distinct coloring for elements, attributes, text, CDATA, comments, and processing instructions. Header with Expand/Collapse/Copy. `setSource(xml)` or `setDocument(doc)`.
- **`b-tag-input`** (`src/inputs/b-tag-input.ts`) — freeform multi-value input. Supports Enter-to-create, Tab-to-commit, Backspace-to-remove, paste-split on delimiters (default `,`, newline, tab; configurable via `separators` attribute). Attributes: `label`, `name`, `value`, `placeholder`, `max-count`, `allow-duplicates`, `error`, `disabled`, `required`, `hint`. Events: `change`, `add`, `remove`, `reject` (duplicate/max-count). Methods: `setTags`, `getTags`, `clear`. Fills the gap between `b-input` (plain comma-string) and `b-multi-select` (dropdown-driven creatable).

All components use existing `--b-*` design tokens and shared stylesheets (`formFieldSheet`, `formControlSheet`) where applicable; no new shared sheets required. `b-tag-input` replaces/avoids the need for `<b-multi-select>` `creatable` mode when no predefined option list is available.

## 2026-04-16 — Store-Level Aggregation & Shared Helpers

Centralized aggregation abstractions in Birko.Data.Stores and refactored all view platform implementations to use shared helpers:
- **New in Birko.Data.Stores** — `AggregateFunction` enum (moved from Birko.Data.Views), `AggregateField` record, `AggregateQuery<T>` (filter, group-by, time bucketing, ordering, paging), `AggregateResult` (dictionary-backed with typed accessors), `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` (optional store interfaces for server-side aggregation), `AggregateHelper` (LINQ fallback implementation), `TimeIntervalParser` (human-readable interval → TimeSpan), `OrderByHelper` (dynamic `OrderBy<T>` applicator for IQueryable/IEnumerable)
- **Birko.Data.Views** — `AggregateFunction.cs` deleted; `AggregateClause` and `ViewDefinitionBuilder` now import `AggregateFunction` from `Birko.Data.Stores`
- **Birko.Data.SQL.Views** — `SqlViewTranslator` delegates to `AbstractConnectorBase.GetSqlFunctionName()` and `FunctionField.CreateFunctionField()` instead of local dictionary/helpers
- **Birko.Data.SQL.View** — `FunctionField.CreateFunctionField()` static method added for creating typed function fields from function name and source field
- **Platform view refactoring** — MongoDB Views uses `StoreAggregationHelper.BuildGroupStageFromPaths()`; ElasticSearch Views uses `StoreAggregationHelper` for metric creation/extraction; CosmosDB Views uses `CosmosAggregationHelper.BuildAggregateSqlParts()`; RavenDB Views uses `OrderByHelper.ApplyTo()`; all platforms removed hardcoded camelCase field name conversions
- **Design rationale** — Establishes layered aggregation: Birko.Data.Stores (portable abstractions) → Birko.Data.Views (fluent builder using shared types) → platform translators (native aggregation via shared helpers)

## 2026-04-15 — Birko.BackgroundJobs.XML

Added XML-file backend for BackgroundJobs to achieve parity with `Birko.Workflow.XML` and `Birko.Data.Sync.Xml`:
- Uses `AsyncXmlStore` from `Birko.Data.XML` with `[XmlRoot]`/`[XmlElement]` attributes on the model
- Nullable `DateTime?` fields use `IsNullable = true` for proper `xsi:nil` handling
- Job metadata uses a `SerializableMetadata` wrapper (`System.Xml.Serialization` has no native `Dictionary<TKey, TValue>` support)
- Registered in `Birko.Framework.slnx`, `Birko.Framework.code-workspace`, and `Birko.Framework.csproj`

## 2026-04-10 — Store Lazy-Init with Template Method Pattern

Refactored all abstract store base classes to auto-initialize on first CRUD operation:
- **AbstractStore/AbstractAsyncStore** — Public CRUD methods (`Create`, `Read`, `Update`, `Delete`, `Count`) call `EnsureInitialized`/`EnsureInitializedAsync` (double-checked locking, thread-safe) then delegate to `protected abstract *Core` methods
- **AbstractBulkStore/AbstractAsyncBulkStore** — Same pattern for bulk methods (`Create(IEnumerable)`, `Read(filter,orderBy,limit,offset)`, `Update(IEnumerable)`, `Delete(IEnumerable)`)
- **SQL Bulk Stores** — `DataBaseBulkStore`/`AsyncDataBaseBulkStore` also use template method with `protected virtual *Core` methods
- **Breaking change** — Concrete stores must override `*Core` methods instead of public CRUD methods (e.g., `CreateCoreAsync` instead of `CreateAsync`)
- **Cleanup** — Removed duplicate `_initialized`/`EnsureInitializedAsync` boilerplate from 12 Workflow + BackgroundJobs stores (~150 lines removed)
- `Init()`/`InitAsync()` is now idempotent — safe to call multiple times or never (auto-called on first CRUD)
- `Destroy()`/`DestroyAsync()` not affected — still explicit

## 2026-03-31 — AI/LLM Infrastructure

Extracted reusable AI agent framework from DraCode into Birko.AI.* projects:
- **Birko.AI.Contracts** — ILlmProvider interface, Message/ContentBlock/TokenUsage models, Tool base class, AgentOptions, LlmProviderFactory (registration-based, `Birko.AI.Factories` namespace)
- **Birko.AI** — LlmProviderBase (retry, SSE, OpenAI-style helpers), Agent base class (run loop, streaming, tool execution), AgentFactory (registration-based, `Birko.AI.Factories` namespace), 9 default tools
- **Birko.AI.Providers** — 11 LLM providers: Claude, OpenAI, AzureOpenAI, Gemini, Ollama, OpenAiCompatibleBase, LlamaCpp, Vllm, Sglang, GitHubCopilot, ZAi + ProviderRegistration (registers all providers with LlmProviderFactory)
- **Birko.AI.Agents** — CodingAgent base, 10 language agents, 4 task agents (Debug, Refactor, Test, Documentation), 4 media agents, OrchestratorAgent + AgentRegistration (registers all agents with AgentFactory, convenience Create)
- **Birko.AI.Resilience** — ProviderRateLimiter (sliding window), ProviderCircuitBreaker (3-state), CostTrackingService (budget enforcement), TrackedLlmProvider (decorator)
- **Birko.AI.Orchestration** — ITaskDispatcher, DirectTaskDispatcher, ImplementationPlan/Step models, StepDependencyAnalyzer (parallel groups, topological sort), EscalationAlert
- **Birko.Communication.OAuth.Providers** — GitHubOAuthProvider (pre-configured device flow factory using Birko.Communication.OAuth)
- **Birko.Contracts** — RetryPolicy extended with BackoffMultiplier and AddJitter
- **Birko.Helpers** — Added PathHelper (IsPathSafe, IsUnderDirectory, GetCanonicalPath)

## 2026-03-30 — ViewModel Repository MapToModel Refactor

Removed circular `ILoadable<TViewModel>` constraint from `TModel` in all ViewModel repositories:
- **Breaking change** — `TModel` no longer requires `ILoadable<TViewModel>`; Models have no knowledge of ViewModels
- **MapToModel** — New abstract method `MapToModel(TViewModel source, TModel target)` on `AbstractViewModelRepository` and `AbstractAsyncViewModelRepository`; consumer concrete repositories must override it
- **Abstract platform repos** — All platform ViewModel repositories (SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB, JSON, InfluxDB, TimescaleDB) made abstract; consumers must subclass
- **DeleteAsync bug fix** — `AbstractAsyncViewModelRepository.DeleteAsync` no longer creates from `data.GetType()` (wrong); uses `CreateModelInstance()` + `MapToModel`
- Migration notes in [MIGRATION-VIEWMODEL-MAPTOMODEL.md](MIGRATION-VIEWMODEL-MAPTOMODEL.md)

## 2026-03-30 — Phase 1 Test Coverage

Completed core data layer test coverage:
- **Birko.Validation.Tests** (new) — 122 tests: rules (Required, Email, Length, Range, Regex, Custom), fluent AbstractValidator, ValidationResult, store wrapper integration (sync, async, bulk)
- **Birko.Data.Tests** (expanded) — 181 tests: added async soft-delete/audit/timestamp decorators, DefaultStoreWrapper, SluggableStoreWrapper, SlugGenerator, SoftDeleteFilter, UnitOfWork exceptions, PagedResult
- **Birko.Data.Sync.Tests** (new) — 21 tests: SyncProvider (initial/download/upload), SyncQueue (serialization, concurrency), model defaults

## 2026-03-26 — Filter-Based Bulk Operations

Added native filter-based Update/Delete to all bulk stores and repositories:
- **PropertyUpdate\<T\>** — Fluent builder for partial property updates, translated natively by platforms
- **Native implementations** — SQL (`UPDATE SET WHERE`/`DELETE WHERE`), MongoDB (`UpdateMany`/`DeleteMany`), ElasticSearch (`UpdateByQuery`/`DeleteByQuery`)
- **Action\<T\> overload** — Read-modify-save fallback for complex mutations
- All decorators (SoftDelete, Timestamp, Audit, Tenant, EventSourcing, Localization, Telemetry, Validation) updated
- All repositories (AbstractBulk, AsyncBulk, ViewModel) delegate to stores

## 2026-03-23 — Birko.Data.CosmosDB

New Azure Cosmos DB (NoSQL API) store provider:
- **Birko.Data.CosmosDB** — Stores (sync/async), Repositories, UnitOfWork (TransactionalBatch), IndexManagement
- **Birko.Data.Sync.CosmosDB** — Sync knowledge store for Cosmos DB
- **Birko.Data.Migrations.CosmosDB** — Migration framework for Cosmos DB (container, indexing policy, document ops)
- **CosmosDbHealthCheck** added to Birko.Health.Data
- Uses Microsoft.Azure.Cosmos SDK v3 with bulk execution enabled

## 2026-03-22 — Birko.Models Restructuring

Three-phase restructuring of the model layer:
- **Birko.Models.Contracts** — Domain interfaces: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument/IDocumentLine, IContactable, IAddressable
- **Birko.Models (Value Objects)** — Money, MoneyWithTax, Percentage, PostalAddress, Quantity
- **Birko.Models.Inventory** — Clean replacement for Warehouse: StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument, InventoryDocumentLine
- **Birko.Models.Pricing** — Pricing domain: Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount
- **Birko.Models.SQL** — Fluent SQL mapping framework: ModelMap\<T\>, IModelMapping\<T\>, ModelMapRegistry
- Existing models implement contracts additively (Product→ICatalogItem+ISluggable, Item→ICatalogItem+ICategorizeable, Address→IAddressable+IContactable, ValueData→IPriceable, AbstractTree→IHierarchical, Category→IHierarchical+ISluggable)

## 2026-03-06 — New Model Projects

Extracted reusable models from FisData.Stock:
- **Birko.Models.Customers** — Address, Customer, InvoiceAddress
- **Birko.Models.Users** — User, Tenant (formerly Agenda), UserTenant
- **Birko.Models** — Added AbstractPercentage, AbstractTree, ValueData
- *(Birko.Models.Accounting was merged into Birko.Models.Pricing during the 2026-03-22 restructuring)*

## 2026-03-05 — Recent Fixes

- Replaced `NativeAsyncDataBaseStore` with `AsyncDataBaseStore` in async stores/repos
- Fixed `AbstractAsyncStore.CreateAsync` return type: `Task` → `Task<Guid>`
- Changed `Connector` property from `private set` to `protected set` in DataBaseStore/AsyncDataBaseStore
- Added parameterless constructor to `DataBaseRepository`
- Fixed PostgreSQL/MySQL stores settings handling
