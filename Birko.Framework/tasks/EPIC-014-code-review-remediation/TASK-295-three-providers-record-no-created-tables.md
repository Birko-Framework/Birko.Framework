---
id: TASK-295
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-243, TASK-286, TASK-287, TASK-288, TASK-290, TASK-293]
findings: []
pr: 75a8a81 (Birko.Data.SQL) + 0e9cffd (PostgreSQL) + 22759a6 (MySQL) + b5dd2c7 (MSSql) + f444d8f (TimescaleDB) + e8f649e/bf53a21/f5c9395/ae6d6a7/92fbdfc/e7aa57f (tests)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.MSSql, Birko.Data.TimescaleDB]
---

# The escape and heal apparatus is SQLite-only: three providers record no created tables

## Measured

`AbstractConnector.RecordTableCreated` is called from exactly **one** place — the **base**
`AbstractConnector.CreateTable(string, IEnumerable<string>)` (`AbstractConnector_Create.cs:107`). And:

| provider | overrides `CreateTable(string, IEnumerable<string>)` | calls `RecordTableCreated` |
|---|---|---|
| SQLite | no | **yes**, via the base |
| PostgreSQL | yes | **no** |
| MySQL | yes | **no** |
| SQL Server | yes | **no** |
| TimescaleDB | yes, and calls `base.` — which is *PostgreSQL's* override | **no** |

Verified on live PostgreSQL 16, MySQL 8.4 and SQL Server 2022: after
`connector.CreateTable(new[] { typeof(Probe) })`, `TablesCreated` is **empty**; then, with the table
dropped, a count answers `0`, `SchemaEscapes` is **empty** and `SchemaGeneration` stays at **0**. Each
provider suite carries that as `TASK295_this_provider_records_no_created_tables_so_the_anomaly_is_unobservable_here`.

## What is therefore inert on three of four providers

Everything that hangs off `TablesCreated`, which since TASK-288 is a chain rather than a diagnostic:

- **TASK-286's annotation** — always the benign branch, *"this connector has NO recorded CREATE TABLE for
  any table named in the statement"*, even for a table it created seconds earlier;
- **TASK-287's `SchemaEscapes` / `OnSchemaEscapeDetected`** — never records, never fires. A host that
  subscribes gets silence that is indistinguishable from "the condition never happened";
- **TASK-288's healing** — `SchemaGeneration` never moves, so a store whose table vanished beneath it
  keeps its remembered `_initialized` and every write throws **until the process restarts**. That is the
  consumer-reported defect TASK-288 closed, still open on PostgreSQL, MySQL and SQL Server.

⚠ **This matters now rather than eventually.** Consumer Symbio deploys on SQLite today, which is the only
provider where any of this works — and TASK-256 records that a move to PostgreSQL is expected. On the day
that happens, all three land at once, silently, and the instrumentation built to investigate them goes
dark with them.

## Fifth instance of a rule this repo has already written down

§ TASK-243: *"a funnel with four overrides is not a funnel"* — after TASK-215 (the base's own wrappers),
TASK-242 (store `*Core` overrides), TASK-243 (the DDL emitters) and TASK-245 (the async twin nothing
calls). The instruction that came with it is *"when introducing a funnel, grep `override` on every method
that reaches it before believing the wiring, and confirm with a revert rather than a read"*. TASK-286
introduced a funnel and did not.

## The shape of the fix, and the one to avoid

⚠ **Do not add `RecordTableCreated(name)` to the three overrides.** That is a fourth copy of the rule and
it re-arms this exact defect for the next provider — which is how this file already has five instances.
Measure and choose between:

1. **Record in the dispatcher, not the leaf.** `CreateTable(IDictionary<string, IEnumerable<AbstractField>>)`
   loops and calls the virtual leaf; recording there covers every override by construction.
   ⚠ Cost, and it needs measuring: the leaf has one external direct caller,
   `Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:281`, which would stop being recorded. It is not
   recorded on three providers today either, so this is not a regression on them — but it is one on SQLite.
2. **Template-method the leaf**, matching the framework's own documented `*Core` convention: a non-virtual
   public `CreateTable(string, IEnumerable<string>)` that calls a `protected virtual CreateTableCore` and
   then records. Covers every caller including the migration builder, and a provider override cannot bypass
   it.
   ⚠ Cost: `CreateTable(string, IEnumerable<string>)` is `public virtual`, so any **consumer** override
   breaks loudly (`CS0506`/`CS0115`). Measure that across the 16 consumer repos first — loud is this
   repo's preference, but the count has to be known, and § TASK-278 records that changing a `public
   virtual`'s shape silently orphans overrides when the change is additive rather than a removal.

Whichever is chosen, the assertion in each provider suite **inverts** rather than being deleted — the
before/after pair on one test is the record, as TASK-277 did to TASK-244's pin and TASK-265 did to
TASK-257's.

## Acceptance

- [ ] `TablesCreated` is populated on all four providers plus TimescaleDB, asserted live per provider.
- [ ] The recording cannot be bypassed by adding a provider override — demonstrated, not asserted in
      prose (e.g. a test provider that overrides the emitter and still records).
- [ ] TASK-288's healing is asserted **live on PostgreSQL and SQL Server**, not only on SQLite: table
      dropped beneath an initialised store, first write reports, second write heals.
- [ ] TASK-286's annotation shows the anomalous branch on those providers.
- [ ] `SqlSchemaBuilder`'s direct call is either still recorded or the loss is stated with its reason.
- [ ] The three provider pins from [[TASK-293]] are inverted, in the same commit.
- [ ] Mutation-proven per provider.

## Out of scope

- [[TASK-293]] — which table an escape is about. Already fixed and independent: it reads the provider's
  error, not the record. It is what made this defect visible.
- [[TASK-290]] — the mechanism behind the consumer's escapes. This changes what its instrument can see on
  three providers, which is why it is P1 rather than a cleanup.

---

## Closed 2026-09-02

### Step 0 settled the design decision the filing had left open

The two candidate placements were priced before anything was written:

| measurement | result |
|---|---|
| overrides of `CreateTable(string, IEnumerable<string>)` across all 16 consumer repos | **0** |
| subclasses of any Birko connector across all 16 consumer repos | **0** |
| framework overrides | **4** — PostgreSQL, MySQL, MSSql, TimescaleDB (whose `base.` call went to PostgreSQL's) |
| external direct callers of the single-table overload | **1** — `Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:281` |

So the **template method** was free, and the dispatcher placement was not: it would have left every
migration-created table unrecorded. Both halves are now measured rather than argued — see the mutation
table.

### The fix

`AbstractConnector.CreateTable(string, IEnumerable<string>)` is now **non-virtual** and owns the
bookkeeping; providers override a new `protected virtual CreateTableCore(string, IEnumerable<string>)`.
All four provider overrides moved, and TimescaleDB's `base.CreateTable(name, fields)` became
`base.CreateTableCore(name, fields)`.

Three properties are deliberate and each has a test:

- **An override cannot bypass it.** Demonstrated rather than asserted in prose — a test connector that
  overrides the emitter (exactly the shape all four shipped providers use, which is what skipped the
  recording) records anyway.
- **The wrapper stays non-virtual.** A reflection pin, because a later edit making it virtual again — or
  moving the recording back into it — reopens this silently, and only a live per-provider run would notice.
- **TASK-286's contract survives the refactor.** A create that threw is still not recorded; a create inside
  a caller's transaction that later rolls back is still recorded, because the question is *"was this ever
  created, and when"*.

### What now works that did not

Per provider, live, and previously impossible because `TablesCreated` was empty:

- `TablesCreated` is populated on PostgreSQL, MySQL, SQL Server and TimescaleDB;
- TASK-286's **anomalous** annotation appears there — a table this connector created and that then
  vanished reads as the anomaly rather than as a benign first touch;
- TASK-287's `SchemaEscapes` records and `SchemaGeneration` moves;
- **TASK-288's healing works**: with the table dropped beneath an initialised store, the first write
  reports (TASK-277) and the **second succeeds**. Before this the store trusted its remembered
  initialization forever, so every write threw until the process restarted — the consumer-reported outage,
  open on every provider but SQLite.

⚠ **The degraded case is asserted too, on TimescaleDB.** A Guid-keyed entity cannot become a hypertable, so
the conversion is recorded and not thrown (TASK-254) — and the plain table is committed and usable, which
is that task's whole licence for degrading. So the create is a fact and is recorded. Otherwise a table that
later vanished would read as benign on exactly the entities that already have a schema problem.

### Measurements

`BIRKO_REQUIRE_LIVE` set throughout, against live **PostgreSQL 16**, **MySQL 8.4**, **SQL Server 2022**,
**TimescaleDB 2 / PG16** and on-disk **SQLite**: **1,579 tests, 0 failed, 0 skipped** across eleven suites
— `Birko.Data.SQL` 667 (663 → 667), SqLite 303, PostgreSQL 97 (96 → 97), MySQL 101 (100 → 101),
MSSql 111 (110 → 111), Migrations.SQL 54 (53 → 54), TimescaleDB 55 (53 → 55),
Migrations.TimescaleDB 81, InMemory 69, JSON 23, XML 18.

**Mutations, disjoint, and the third one is the design decision:**

| mutation | red |
|---|---|
| recording back inside the emitter — the pre-fix shape | **2 per provider** (the inverted pin + the healing test) on PostgreSQL, MySQL and MSSql, plus **2** offline; **SQLite green**, since it never overrode |
| the wrapper made `virtual` again | **1** — the reflection pin, and nothing else. That is the point: the regression is otherwise invisible offline |
| record in the `IDictionary` dispatcher — the rejected alternative | **1** of 54 in Migrations.SQL (the builder's direct call) + **2** offline, while **PostgreSQL stays green at 97**. So the alternative would have looked correct on every provider suite and silently dropped the migration path |

### Acceptance

- [x] `TablesCreated` populated on all four providers plus TimescaleDB, asserted live per provider.
- [x] The recording cannot be bypassed by a provider override — demonstrated with an overriding connector,
      plus a reflection pin on the wrapper's non-virtuality.
- [x] TASK-288's healing asserted live on PostgreSQL, MySQL **and** SQL Server: first write reports, second
      heals.
- [x] TASK-286's anomalous annotation shown on those providers.
- [x] `SqlSchemaBuilder`'s direct call is **still recorded**, and the test that proves it is what rules out
      the dispatcher placement.
- [x] TASK-293's three provider pins **inverted** rather than deleted — the before/after pair on one test
      is the record.
- [x] Mutation-proven per provider.

### Deliberately not done

- **The `IsInitializing`/`DoInit` interaction is untouched.** `RecordTableCreated` is now reached on every
  provider, which means `DoInit()`'s event and the `IsInitializing` flag are reached on more paths than
  before — but nothing about their behaviour changed here, and no test moved.
- **No new `SchemaEscape` shape.** `TableNames` is still a set and the record's public surface is
  byte-identical; this changes only whether it is ever populated off SQLite.
- **The consumer is not touched.** Symbio subscribes to `OnSchemaEscapeDetected` already, so on a move to
  PostgreSQL the channel now works with no consumer change — which was the point of doing this before the
  move rather than after.
