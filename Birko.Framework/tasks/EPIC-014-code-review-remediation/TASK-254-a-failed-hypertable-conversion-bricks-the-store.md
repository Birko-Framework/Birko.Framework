---
id: TASK-254
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-204, TASK-243, TASK-244, TASK-252, TASK-472]
findings: []
pr: "Birko.Data.SQL b9a7ab5 · Birko.Data.TimescaleDB 5d3ddc7 · Birko.Data.TimescaleDB.Tests 406881e"
github-issue: null
jira-key: null
affects: [Birko.Data.TimescaleDB, Birko.Data.SQL]
---

# A hypertable conversion that cannot succeed now bricks the store instead of degrading

Spawned by Symbio **TASK-472**. Fixing the identifier defect there turned a silent no-op into a thrown
exception, which is an improvement — but it lands in the one place this repo has already decided a throw is the
wrong answer.

## What changed, and why it is now a problem

Before TASK-472, `create_hypertable` emitted a bare table literal, raised `42P01`, and
`PostgreSQLConnector.IsMissingTableException` classified that as a missing table — so `OnException` ran
`DoInit()` and returned. **Every** conversion failure was swallowed, including the legitimate ones.

With the identifier fixed, the statement reaches the server and a genuinely impossible conversion now raises
its real error. The reachable case: TimescaleDB refuses a unique index that omits the partitioning column, so a
Guid-keyed entity raises `TS103` — measured on TimescaleDB 2 / PostgreSQL 16.

**The root cause one layer down is [[TASK-252]] item 2**, which is why this task is about the *reporting* and
not about making Guid-keyed hypertables work: a composite `(Guid, Ts)` primary key — the shape that would make
such an entity legal — cannot be declared at all, because `AbstractConnector.CreateTable` emits `PRIMARY KEY`
per column and two `HasPrimary` calls produce `42P16`. Fixing that is the separate, larger change; degrading
gracefully when the declaration cannot be honoured is this one.

That error propagates out of `TimescaleDBConnector.CreateTable`, hence out of `InitCore`, and stores set
`_initialized` only **after** schema-ensure returns. So the entity's whole surface — **reads included** —
throws on every subsequent operation. That is precisely the failure mode **TASK-204** removed for indexes:

> *Lazy schema-ensure degrades and reports; an explicit schema call throws. […] Degrade only what is a
> constraint or an optimisation — never correctness — and report rather than swallow.*

A hypertable conversion is squarely in that category: the table exists and is fully usable as a plain
PostgreSQL table without it. Partitioning is an optimisation, not correctness.

## Why TASK-472 shipped the throw anyway

Blast radius measured before shipping, per § SH-H037: **0 concrete consumer entities** and **0 framework domain
models** hit it. Symbio has `TimeSeriesRecord` with no subclass, and its `TimescaleDbStore` passes the
lowercase `"timestamp"`. So nothing breaks today, and the throw is strictly better than the silence it
replaced. It is pinned by `HypertableSchemaLiveTests.A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so`
so the behaviour is deliberate and visible rather than accidental — but the pin records the current state, it
does not argue the state is right.

**The window closes the moment anyone declares a real time-series entity.** That is what makes this worth
filing now rather than noticing it from a production incident.

## The design question this actually opens

`AbstractConnector`'s degrade-and-report channel is index-shaped: `IndexCreationFailure` (keyed by table +
index), `IndexCreationFailures`, `OnIndexCreationFailed`, `RecordIndexCreationFailure`,
`ClearIndexCreationFailure`. Reusing it verbatim for a hypertable would mean putting a non-index failure into a
type whose name says index — so decide deliberately between:

1. **Generalise** the channel to a schema-ensure failure with a kind discriminator, keeping the existing names
   as a thin compatibility surface. Best if more non-index schema-ensure steps are coming (compression and
   retention policies are the obvious next ones).
2. **A parallel TimescaleDB-specific record**, mirroring the pattern locally. Cheaper, and honest about scope —
   but it is the second copy of a mechanism, and this epic keeps learning that the second copy is where things
   drift (TASK-245, TASK-246).

Whichever is chosen, TASK-204's invariants carry over and are not negotiable: the record is **current state
keyed by identity, not an append-only log** (connectors are cached process-wide while `_initialized` lives on
the store, so a scoped store re-runs schema-ensure per request), the event fires on the **transition** into
failure, and the record **clears** when the condition no longer holds.

## Acceptance criteria

- [x] A hypertable conversion that cannot succeed leaves the store **initialised and usable as a plain table**,
      with the failure recorded and an event raised — not swallowed, and not fatal.
- [x] An **explicit** `CreateHypertable` / `CreateHypertableAsync` call still throws. Same split TASK-204 drew
      and TASK-245 later narrowed: a caller asking for the conversion *now* gets the error; lazy schema-ensure
      degrades.
- [x] The record is keyed, transition-fired and cleared when repaired — asserted, not asserted-by-construction.
      TASK-204's own regression was a list that grew one entry per HTTP request forever.
- [x] `HypertableSchemaLiveTests.A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so` **updated rather
      than deleted** — it currently pins the throw, and it should end up pinning the degrade plus the report.
      That test is the record of this decision changing.
- [x] Verified against live TimescaleDB, and proven able to fail by revert.
- [x] Decide whether the same treatment is owed to the *other* TimescaleDB schema steps (compression, retention)
      before they acquire the same shape independently.

## Implementation plan

_Drafted 2026-08-24 after a pre-planning measurement, then grilled. The grill settled every branch by
measurement rather than taste, and two of its findings changed the plan: criterion 6 dissolved on a false
premise, and the justification for degrading turned out to be stronger and different from the one the task
gave._

### Step 0 — measurements. All five are done.

- **M1 · The index channel is NOT unconsumed.** Symbio references `IndexCreationFailures` /
  `OnIndexCreationFailed` in **production code** (`Symbio.DataAccess/Sql/UniqueIndexDataCheck.cs`), its host
  (`Symbio.Api/Program.cs:758`), **two test files**, and as a documented contract in its `CLAUDE.md` (§182,
  §193) and `docs/specs/core-kernel.md` (§342, §355) — including the explicit *"not an inventory"* property.
  **This rules out the task's option 1**: reshaping that type is consumer-visible.
- **M2 · One site, no async twin.** `TimescaleDBConnector.CreateTable(string, IEnumerable<string>)` (line 92)
  calls `CreateHypertable` unguarded; there is **no** `CreateTableAsync` override. TASK-245's recurring trap
  (*patch the async twin, fail 0 tests*) does **not** bite here — stated explicitly because the reader's
  prior is that it does.
- **M3 · The pin test targets the right layer.**
  `HypertableSchemaLiveTests.A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so` calls
  `connector.CreateTable(new[] { typeof(GuidKeyedRow) })` — the **schema-ensure** path. So it is exactly the
  test to invert, and the explicit-call half needs a **new** test.
- **M4 · `TS103` is still reachable**, and is *"cannot create a unique index without the column `ts` (used in
  partitioning)"*. Measured on TimescaleDB 2.29.2 / PostgreSQL 16.15.
- **M5 · THE LOAD-BEARING PREMISE, and it holds.** The plain table **survives** the failed conversion
  (present in `pg_tables`, absent from `timescaledb_information.hypertables`) and is **fully usable** — two
  rows written and read back. Had it not survived, degrading would leave the store initialised over a table
  that does not exist, which is **worse** than the current throw, and the task would have been invalid.
  Measure this again if the ordering inside `CreateTable` ever changes.

### Step 1 — why degrading is right, and it is NOT the reason the task gave

The task argued "partitioning is an optimisation, not correctness". True, but the stronger and more specific
reason is that **nothing ever declares an entity to be a hypertable**:

```csharp
if (_timescaleSettings != null && !string.IsNullOrEmpty(_timescaleSettings.TimeColumn))
    CreateHypertable(name, _timescaleSettings.TimeColumn, _timescaleSettings.ChunkTimeInterval);
```

`TimeColumn` is a single `TimescaleDBSettings` property (default `"timestamp"`) applied to **every** table the
connector creates, and there is **no per-entity attribute** — no `[Hypertable]`, nothing. So a failed
conversion is a **connector-wide default that did not apply**, not a broken per-entity contract. That is
squarely TASK-204's *"degrade only what is a constraint or an optimisation — never correctness"*.

**And the noise objection was measured away.** `AddTimescaleStore<T>` registers **one deliberately-chosen
entity per call**, so the feared "mixed database where most tables fail conversion and the report fills with
expected entries" cannot arise from this API. Record every failure; do **not** add discrimination logic to
suppress "expected" ones — that would be complexity guarding a shape the API does not produce.

### Step 2 — the reporting channel: extract the MECHANISM, not the type

| | Approach | Verdict |
|---|---|---|
| 1 | Generalise `IndexCreationFailure` with a kind discriminator | **Rejected** — consumer-visible (M1). TASK-248/256 precedent: the obvious fix vetoed by measurement |
| 2 | A parallel TimescaleDB-specific record | Two implementations of logic that was **got wrong once already** |
| 4 | Reuse `RecordIndexCreationFailure` with an `indexName = "(hypertable)"` sentinel | **Rejected** — injects foreign entries into a collection Symbio reads in production and documents |
| **3** | **Extract the mechanism; leave the index surface byte-identical** | **Chosen** |

A small helper owns the dictionary, the lock, the key, the **transition** detection, the **clear** and the
stable ordering. `IndexCreationFailures`, `OnIndexCreationFailed`, `RecordIndexCreationFailure`,
`ClearIndexCreationFailure` keep their exact signatures and semantics and delegate to it.
`TimescaleDBConnector` gets its own instance.

**Justification narrowed by the grill, and stated honestly:** the helper is used **exactly twice, and no more
callers are coming** (criterion 6 below removes the expected third and fourth). It is chosen not for future
reuse but because the parts that drift — keyed-not-list, transition-fire, clear-on-repair, lock, ordering —
are precisely the parts TASK-204 got wrong first time, shipping an append-only list that grew one entry per
HTTP request. One implementation beats two even at n=2 when the logic is subtle.
**If the helper grows configuration or a type hierarchy, that is the signal option 2 was right.**

**The compatibility claim must be TESTED, not asserted.** Symbio documents current-state-not-history, at most
one entry per index, and empty ≠ "all indexes exist". Assert those against the existing channel.

### Step 3 — degrade at schema-ensure, throw on the explicit call

- `TimescaleDBConnector.CreateTable(string, IEnumerable<string>)` wraps its `CreateHypertable` call: record +
  raise, **do not rethrow**.
- The public `CreateHypertable(...)` (both overloads, incl. `CreateHypertable(Type, …)` at line 179) keeps
  throwing.

**This is TASK-204's split on the reason, not the shape:** a caller asking for the conversion *now* wants the
error, which is why `CreateIndexes` still throws there. That caller exists here — `CreateHypertable` is
public.

### Step 4 — criterion 6 is ANSWERED, not spawned

Compression and retention are **not in `Birko.Data.TimescaleDB` at all** (the only match is a doc comment).
They live in `Birko.Data.Migrations.TimescaleDB` as `AddCompressionPolicy` / `AddRetentionPolicy` — the
**migration** path, which is explicit by definition and therefore *should* throw. They are never in
schema-ensure, so they cannot brick a store and nothing is owed to them. The criterion assumed they were
"the obvious next non-index schema-ensure steps"; they are not schema-ensure steps at all.

**Do not spawn a task for this** — it would file work that does not exist.

### Step 5 — out of scope, recorded as decisions

- **Making Guid-keyed hypertables work.** Root cause is [[TASK-252]] item 2 — a composite `(Guid, Ts)`
  primary key cannot be declared, because `CreateTable` emits `PRIMARY KEY` per column and two `HasPrimary`
  calls give `42P16`. Still `todo`. This task degrades when a declaration cannot be honoured; it does not
  widen what can be declared.

### Step 6 — tests

**Invert first, and watch it fail before touching production code.**

- **Invert** `A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so` → schema-ensure **degrades**: the
  table exists, is **written and read through the store** (not merely `_initialized == true`), is not a
  hypertable, and exactly one failure is recorded. Its own summary says it pins current behaviour rather than
  endorsing it, so this is the inversion it anticipates.
- **New:** the explicit `CreateHypertable` still throws.
- **New:** the record is **keyed** (two schema-ensure runs → one entry), **transition-fired** (event once, not
  per attempt), and **cleared** when repaired. TASK-204's own regression was an append-only list; asserting by
  construction is what let that ship.
- **Compatibility:** the index channel's documented properties still hold after the extraction.

**Mutations, each must red ≥ 1:** rethrow from the guard; drop the clear; fire the event on every attempt
instead of on the transition; use a list instead of a key.

### Step 7 — commits (polyrepo, production before aggregator, no `Co-Authored-By`)

Four: `Birko.Data.SQL` (helper), `Birko.Data.TimescaleDB` (guard), `Birko.Data.TimescaleDB.Tests`, aggregator.

## Out of scope

- The identical throwing-subscriber hole in the INDEX channel — **[[TASK-283]] owns it**, spawned at this
  task's close gate. Deliberately not fixed here: that channel has real consumers, so changing whether a
  handler's exception propagates is a behaviour change on consumed surface and wants its own measurement.
- Making Guid-keyed hypertables work — **[[TASK-252]] item 2** owns the composite `(Guid, Ts)` primary key
  that would make such an entity legal (`42P16`, still open).

### Results — measured 2026-08-24/25

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15**, `BIRKO_REQUIRE_LIVE=1`:
`Birko.Data.TimescaleDB.Tests` **53 passed, 0 failed, 0 skipped** (44 → 53, **+9**), no new nullable warning.

**Compatibility of the `SchemaEnsureFailureLog` extraction**, since it rewired a channel a consumer
depends on: `Birko.Data.SQL.Tests` **655**, SQLite **250**, Migrations.SQL **53**, View.Migrations **14**,
TimescaleDB.ViewModel **7** — all green, 0 skipped.

**Seven mutations, every one red ≥ 1:**

| # | Mutation | Measured |
|---|---|---|
| M1 | rethrow from the guard | **5** — the inverted pin plus all four bookkeeping tests |
| M2 | drop clear-on-success | **1** — the repair test alone |
| M3 | fire the event on every attempt | **1** — the transition test alone |
| M4 | key per attempt (i.e. a list) | **3** |
| M5 | degrade unconditionally again | **1** — the ambient-boundary test |
| M6 | emit `INTERVAL ''` again | **2** — both theory cases |
| M7 | let a subscriber exception escape | **1** |

M2 and M3 isolating to exactly one test each is the point: those two properties are what TASK-204 got wrong
first time, and they are now asserted rather than asserted-by-construction.

### Close gate — four verdicts, four findings, all resolved

**Standards** produced one blocker (register-on-introduce → § Conventions entry, placed after the TASK-204
rule it extends). **Fidelity** produced one: criterion 2 names `CreateHypertableAsync` and nothing asserted
it — the async door existed (`TimescaleDBConnector.cs:330`, `:390`) and was covered only by construction.
**Security** was not applicable, with the reason recorded. **Correctness** produced four:

- **⚠ HIGH, and it was a real defect in this change.** The degrade was unconditional, but the premise it
  rests on — *the plain table survives* — was measured **only on the own-connection path**. Inside a
  caller's ambient boundary the `CREATE TABLE` is not committed and the failed statement aborts the
  transaction: **measured, 0 rows in `pg_tables` afterwards**. Degrading there reports success over a table
  that will not exist, and costs the caller the real error — which the doc comment two paragraphs above had
  *already written down as worse than throwing*. TASK-244 made that path reachable by having `InitCore`
  enter the ambient scope. Now `catch (…) when (AmbientTransaction == null)`, with the qualifier stated in
  the remark. **A premise measured on one path is a sample, not a premise.**
- **MEDIUM:** a null/empty `ChunkTimeInterval` used to fail loudly out of `CreateTable`; after the degrade it
  would be caught and recorded, so **no** table on that connector would ever become a hypertable and nothing
  would surface unless the consumer subscribed. A regression this change introduced by widening what gets
  swallowed. The connector emitter now omits the argument, matching the sibling in `TimescaleDBMigration`.
- **MEDIUM:** a literal **NUL byte** written into `AbstractConnector.cs` by my own edit — `\0` instead of the
  `"U+0000"` escape. Measured: 1 NUL in the working tree, 0 in HEAD, and git reporting
  `Bin 31256 -> 31375 bytes` for the framework's largest connector file. No line diff, no blame, and a
  three-way merge would fail. Fixed; git shows text again.
  <br>**And then I did it a second time, in this very file**, writing the paragraph above: the prose meant
  to show the escape and contained a raw NUL instead, turning the task file binary to git. That is the
  useful evidence — the defect is not exotic, it is what happens whenever a tool writes a byte where a
  six-character escape was intended, and nothing in the edit path warns. Worth a habit:
  check for stray control bytes (`git diff --stat` reporting `Bin` is the loud tell) after any scripted
  edit whose text mentions one. It took three attempts to write this paragraph without one.
- **LOW:** a throwing subscriber defeated the degrade, since the event fires inside the `catch`. Hardened
  here (zero consumers, so free); the identical hole in the index channel is **[[TASK-283]]**, deliberately
  not touched because that channel has real consumers and changing exception propagation there is a
  behaviour change on consumed surface.

## Human test plan

- [x] N/A — mechanical; the proof is that a Guid-keyed entity still reads and writes after a failed conversion,
      with exactly one entry in the failure report.
