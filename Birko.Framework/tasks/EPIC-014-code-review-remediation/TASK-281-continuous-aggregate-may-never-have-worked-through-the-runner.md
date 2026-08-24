---
id: TASK-281
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-255, TASK-259, TASK-243]
findings: []
pr: "Birko.Data.Migrations.TimescaleDB 2eec9a3 · Birko.Data.Migrations.TimescaleDB.Tests 57c8a08"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# A continuous aggregate cannot be created or refreshed inside a transaction — so it may never have worked through the runner at all

Found by `code-review` at **[[TASK-255]]**'s close gate, and rated the most valuable thing that review
produced. Out of scope there (TASK-255 changed the bucketing column, not the execution model) and
pre-existing — both emitters predate it.

## What is wrong — a hypothesis with a mechanism, to be measured before it is believed

PostgreSQL/TimescaleDB refuse two of this class's statements inside a transaction block:

- `refresh_continuous_aggregate` cannot run in a transaction block.
- `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` **without `WITH NO DATA`** performs an initial
  refresh, which carries the same restriction.

`ExecuteScript` sets `command.Transaction = transaction`, and `SqlMigrationSettings.UseTransaction`
**defaults to true**. If the restriction holds as described, then a migration calling
`CreateContinuousAggregate` or `RefreshContinuousAggregate` through `TimescaleDBMigrationRunner` in its
**default configuration fails** — i.e. the feature has never worked on the only path a real migration takes.

**Why nothing caught it.** Every test of these two emitters — including the live ones TASK-255 added — calls
`Exec(BuildContinuousAggregateSql(...))` on a **fresh connection outside the migration context**, so the
transactional path is exercised nowhere in the tree. That is a coverage shape worth naming: the suite tests
the *SQL* thoroughly and the *execution model* not at all. Compare TASK-246, where a feature worked in the
branch nobody used and failed in the branch everybody used, and a green suite said nothing.

Note the class remarks currently assert the opposite as a blanket guarantee — *"These statements run on the
migration's own connection and transaction, deliberately … PostgreSQL's DDL is transactional, so a migration
that fails rolls its hypertable conversion back with it."* True of the hypertable and policy emitters; not of
these two, if the hypothesis holds.

## Step 0 — MEASURED 2026-08-24, hypothesis CONFIRMED

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15**, raw `psql` probes against the server (framework not
involved, deliberately — this establishes the *premise* before any framework path is blamed):

| # | Statement | In a transaction block | Result |
|---|---|---|---|
| A | `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` | yes | **`25001`** — *"CREATE MATERIALIZED VIEW ... WITH DATA cannot run inside a transaction block"* |
| C | same **+ `WITH NO DATA`** | yes | **succeeds**, and survives the `COMMIT` |
| — | rows in the view created by C | — | **0** — empty until refreshed, exactly the predicted cost |
| B | `CALL refresh_continuous_aggregate(…)` | yes | **`25001`** — *"refresh_continuous_aggregate() cannot run inside a transaction block"* |
| D | same, autocommit | no | **succeeds**, view populated (2 rows) |

Both refusals are **SQLSTATE 25001** (`ACTIVE SQL TRANSACTION`), and
`SqlMigrationSettings.cs:29` confirms `UseTransaction` defaults to `true`.

**What this establishes, precisely:** the two statements cannot run inside a transaction block, and the
runner opens one by default. **What it does NOT yet establish:** that the framework path reaches that
error — `ExecuteScript` sets `command.Transaction`, so it follows, but this epic's rule is measure rather
than infer. The runner-path test (criterion 5) is therefore a **prover**, not a formality, and criterion 1
is only half-discharged until it runs.

**Claim discipline:** the title says *"may never have worked through the runner"*. What is measured is
*"cannot work on the default configuration"* — "never" is a claim about history that was not checked. Use
the measured phrasing.

**Both remedies are validated and priced by the same probes:**
- Creation **is** fixable in place: `WITH NO DATA` is legal inside a transaction (probe C), costing an
  empty view until a refresh happens.
- Refresh is **not** fixable in place: there is no `WITH NO DATA` equivalent, so it must be issued off the
  boundary (probe D is the only shape that works).

## Acceptance criteria

- [x] **Step 0, before any fix: measure it.** Run a real migration through `TimescaleDBMigrationRunner` with
      `UseTransaction = true` (the default) that calls `CreateContinuousAggregate`, then one that calls
      `RefreshContinuousAggregate`, against live TimescaleDB. Record the exact error and SQLSTATE, or record
      that it succeeds — **the hypothesis must be falsifiable and may be false.** § Conventions (TASK-276):
      a hypothesis you cannot reproduce gets falsified or recorded, never quietly adopted.
- [x] If it fires, the fix distinguishes the two statements rather than treating them alike: `CREATE … WITH
      NO DATA` makes creation transactional-safe at the cost of an unpopulated view, whereas a refresh cannot
      be made transactional at all and must be issued **off** the boundary. Say which mechanism each gets and
      why — the `AmbientSqlTransaction.Suppress()` precedent (TASK-243) is the shape, but note these emitters
      deliberately bypass the connector, so suppression is not directly available to them.
- [x] `WITH NO DATA` changes observable behaviour — the view is empty until refreshed. If that is chosen,
      state it on the method and assert it, rather than letting a caller discover an empty aggregate.
- [x] **Added to scope at the plan grill (2026-08-24), by explicit decision rather than drift:** an
      `AddContinuousAggregatePolicy` emitter, because `add_continuous_aggregate_policy` is measured
      transaction-safe (probe E) and is the idiomatic way to populate a continuous aggregate. Without
      it, this fix would document a remedy the framework cannot perform — TASK-263's recorded failure,
      *"named an escape hatch that did not open"*.
- [x] The class-level transaction remark is corrected to name the exception, in the same change.
- [x] A test exercises these two emitters **through the runner**, not through `Exec` — otherwise the gap
      that hid this stays open for the next defect in the same place.
- [x] Proven able to fail: the new runner-path test must go red against today's code (if step 0 confirms the
      defect) and green after, with the existing `Exec`-based tests unchanged as the control.

## Implementation plan

_Drafted after Step 0 rather than before it, then grilled. The grill changed the task's shape: it found a
transaction-safe population path the plan had missed entirely, which turned "option A cripples the feature"
into "option A plus one new emitter is the idiomatic TimescaleDB workflow"._

### The asymmetry, and the path the first draft missed

Step 0 measured three statements, and the grill added three more. Together they settle the design:

| Statement | Inside a transaction | Source |
|---|---|---|
| `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` | **`25001`** | probe A |
| same **+ `WITH NO DATA`** | **legal**, survives commit, view empty | probe C |
| `CALL refresh_continuous_aggregate(…)` | **`25001`** | probe B |
| same, autocommit | legal, populates | probe D |
| **`add_continuous_aggregate_policy(…)`** | **legal**, job survives commit | **probe E** |
| `create_hypertable(…)` | **legal**, survives commit | probe F |

**Probe E is the one that matters.** The first draft treated manual refresh as the only way to populate an
aggregate and concluded a transactional migration could never do it. That was wrong: a **refresh policy** is
transaction-safe, and it is what TimescaleDB intends for the job. So the complete workflow is

```
CREATE MATERIALIZED VIEW … WITH NO DATA      -- legal in a transaction
add_continuous_aggregate_policy(…)           -- legal in a transaction
→ the background job populates it
```

and manual refresh is the *immediate-backfill* escape hatch, not the mechanism.

**This class emits no `add_continuous_aggregate_policy`** — only the four compression/retention policies.
That gap is why option A looked crippling.

### Step 1 — the runner-path test FIRST, and it must go red for the right reason

Write it before touching production code, watch it fail with `25001`. If it does not fail, stop and rebuild
the plan on whatever that reveals (§ TASK-243: a fix measured as *not working* because four overrides
bypassed the funnel it patched).

**The fixture risk is already cleared by measurement**: `create_hypertable` is transaction-safe (probe F), so
the migration's earlier statements succeed and the failure lands on the aggregate — not on a fixture fault
wearing the defect's costume (§ TASK-259, where a probe entity's inherited timestamp columns produced a
missing-column error indistinguishable from the bug under test).

The runner reaches `ExecuteScript` with a real transaction: `SqlMigrationRunner.ExecuteWithTransaction` →
`connection.BeginTransaction()` → `ExecuteSingleMigration(migration, direction, connection, transaction)`.

### Step 2 — creation: always `WITH NO DATA` (option A)

Uniform, not conditional on whether a transaction happens to be present. § Conventions (TASK-274): *two doors
onto one feature must give one answer*, and *the tell is always that both doors look correct in isolation*.
Under the conditional alternative the identical migration yields a populated or an empty view depending on a
settings flag, with nothing at the call site saying which.

**Cost, stated rather than waved through:** a caller who created-and-read without refreshing now gets an
empty view. **Re-measured for this task, not inherited: 0 callers of either emitter across all 16 consumer
repos** (§ TASK-259 — a stale blast radius is how a wrong claim reaches a commit message).

Document on the method that the view is empty until a policy or a refresh populates it, and **assert it** —
so nobody "fixes" the emptiness by deleting `WITH NO DATA` and silently reintroducing `25001`.

### Step 3 — the new emitter: `AddContinuousAggregatePolicy`

Mirrors `BuildCompressionPolicySql` exactly, which is the shape criterion-style reasoning should follow here
(and note TASK-255's lesson: imitate the right neighbour — this one is a *policy* emitter, so the policy
sibling is correct):

- the view is a **`regclass` inside a literal** → `connector.RegclassLiteral(viewName)`
- `start_offset`, `end_offset`, `schedule_interval` are **expression fragments** inside literals →
  `SqlLiteral.EscapeLiteral` only. Not identifier-validated: an INTERVAL is a legitimate expression, exactly
  as `compress_orderby`'s `ts DESC` is.
- Nullable offsets are legitimate in TimescaleDB (`NULL` start_offset = from the beginning). Decide and state
  whether the emitter supports that or requires both; **measure before choosing**, do not assume.

Included in this task rather than spawned because shipping A while documenting a remedy the framework cannot
perform is precisely TASK-263's recorded failure — *TASK-256's rule named an escape hatch that did not open*.

### Step 4 — refresh: refuse, naming BOTH doors

`RefreshContinuousAggregate` refuses when `transaction != null`. The message names the policy emitter **and**
`SqlMigrationSettings.UseTransaction = false`.

**Justified only on routing.** The server's own message is already good —
*"refresh_continuous_aggregate() cannot run inside a transaction block"* — so do **not** claim in the doc
comment that it is unclear; that would be an overclaim. What the server cannot know is `UseTransaction` or
this framework's policy emitter, and routing is the framework's sole added value here (§ SH-H037 / TASK-215:
a refusal names the door THIS caller has).

**Guard narrowly, and write the version down.** Condition is exactly `transaction != null`; the remark
records SQLSTATE `25001` and *measured on TimescaleDB 2.29.2 / PostgreSQL 16.15*, per TASK-261's
catalogue-drift rule. If TimescaleDB ever relaxes the restriction this guard becomes a **false refusal**,
which this codebase rates worse than the hole (`PredicateScope`: *a false refusal breaks working code*) — the
version stamp is what makes that findable rather than mysterious.

**The opt-out is part of the fix and needs its own test.** Verified reachable, not assumed:
`SqlMigrationRunner.cs:132` passes `null`, so `UseTransaction = false` reaches autocommit. § Conventions is
explicit that a guard whose opt-out throws is *a wall wearing a door's label* — assert that the door opens,
not merely that the refusal fires.

Refuse rather than silently skip: a skipped refresh is an empty aggregate reporting success, the
§ *a write that cannot be applied must never report success* family (TASK-277).

### Step 5 — the class remark that currently promises the opposite

The remarks assert *"These statements run on the migration's own connection and transaction, deliberately …
PostgreSQL's DDL is transactional, so a migration that fails rolls its hypertable conversion back with it."*
True of the hypertable and policy emitters (probe F confirms the hypertable half); **false for the two
continuous-aggregate statements**. Correct it in the same change, naming `25001` and both statements.

### Step 6 — tests

- **Runner-path prover** (step 1): a real migration through `TimescaleDBMigrationRunner`,
  `UseTransaction = true`, calling `CreateContinuousAggregate` — red before, green after.
- **Policy emitter, through the runner, inside a transaction** — the probe-E path, proving the workflow end
  to end rather than only the SQL string.
- **Opt-out test**: same migration with `UseTransaction = false` — refresh runs, aggregate populated. This is
  what proves the door opens.
- **Refusal test**: refresh inside a transaction throws, message naming both doors.
- **`WITH NO DATA` behaviour test**: the view exists and is **empty** until populated.
- **Offline emitter tests** for the new policy SQL, matching the compression-policy tests' shape, plus an
  injection test for the view name (`RegclassLiteral` containment).
- **Controls**: TASK-255's `Exec`-based live tests stay green untouched — `bucketsRowsItCanBeReadBackFrom`
  already refreshes explicitly, so A should not disturb it. **Confirm by running, do not assume.**

**Mutations, each must red ≥ 1** (§ TASK-245/248 — a revert that fails nothing is a missing test): drop
`WITH NO DATA` → runner-path test reds with `25001`; drop the refusal → refresh test reds with the raw server
error instead of the routed message; run the opt-out path with `UseTransaction = true` → reds; drop
`RegclassLiteral` from the policy emitter → injection/containment test reds.

### Step 7 — commits (polyrepo, production before aggregator, no `Co-Authored-By`)

1. `Framework/Birko.Data.Migrations.TimescaleDB` — `fix(TASK-281): …`
2. `Framework.Tests/Birko.Data.Migrations.TimescaleDB.Tests` — `test(TASK-281): …`
3. `Framework/Birko.Framework` — `tasks(TASK-281): …` + § Conventions entry + Recent Updates + dashboard.

### Dropped from the first draft, deliberately

- **The separate-connection refresh design.** Superseded by the policy path — and note it is dropped on
  *that* ground, not on the first draft's unmeasured MVCC claim about an uncommitted view being invisible.
  That claim was never verified and is not relied on.
- **The framing that a transactional migration can never populate an aggregate.** False, as probe E shows.

### Deferred

- A runner-level **run-after-commit** hook, the only design that would let one migration create *and*
  immediately backfill. Unblock condition: a real caller needing immediate backfill inside a transactional
  migration. Not built speculatively (§ TASK-262 on speculative API).

## Out of scope

- Routing these emitters through the connector generally so they could reuse `DoDdlCommand` and the provider
  capabilities — **[[TASK-282]] owns it**, given an id at this task's close-gate sweep after floating as prose
  since TASK-259. That task reopened the option (the `SetExternalTransaction` obstacle is gone) and recorded it
  on the class as a decision awaiting its own measurement; it is a larger behaviour change than this fix.
- The bucketing column, which **[[TASK-255]]** fixed, and the sibling's `orderByColumn` default, which
  **[[TASK-279]]** owns.

### Results — measured 2026-08-24

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15**, `BIRKO_REQUIRE_LIVE=1`:
`Birko.Data.Migrations.TimescaleDB.Tests` **71 passed, 0 failed, 0 skipped** (61 → 71, **+10**), no new
nullable warning.

**Criterion 1 is fully discharged, in two halves.** The server halves were probes A–F. The framework half
was the prover, watched red *before* a production line changed:

```
Npgsql.PostgresException : 25001: CREATE MATERIALIZED VIEW ... WITH DATA
cannot run inside a transaction block
   at SqlMigrationRunner.ExecuteWithTransaction(...) line 114
```

and it failed on the **aggregate** statement, not on `create_hypertable` or the raw `CREATE TABLE` — which
is what probe F was measured for.

**Mutations — all red ≥ 1, so nothing here is a test that cannot fail:**

| # | Mutation | Predicted | Measured |
|---|---|---|---|
| M1 | drop `WITH NO DATA` | 1 | **5** — all four runner-path tests (each creates the aggregate in a transaction) plus the offline tail assertion |
| M2 | disable the refusal | 1 | **1** ✓ — the refusal test alone |
| M3 | drop `RegclassLiteral` from the policy emitter | 1 | **3** — the offline shape test, the injection containment test, **and the live policy test**, because a bare PascalCase view folds and the policy cannot find its aggregate |

M3's live red is the useful one: it shows the `regclass` treatment matters in reality, not merely in a
string comparison.

**⚠ A fixture fault was found, and it is the reason to report the ordering rather than a single number.**
`Reset()` dropped `__BirkoMigrations`; the real version table is **`__Migrations`**
(`SqlMigrationSettings.FullTableName`). So version 1 survived between runs, `Migrate()` found nothing to do,
and returned **success having created nothing** — a **false green** in the full-suite ordering and a **false
red** in isolation. Exactly § TASK-259's *a fixture fault is indistinguishable from the defect*. Fixed, and
the class is now green in isolation **twice consecutively**, which is what distinguishes a fix from
first-run luck.

**⚠ A prediction of mine was wrong: the CR-H071 tests did NOT stay green.** The plan listed them as
untouched controls. They assert `GROUP BY bucket;` — the semicolon adjacent to the clause — and
`WITH NO DATA` now sits between, so the string match broke while the behaviour did not. The assertions were
**re-expressed to preserve CR-H071's intent** (clause present, no dangling comma, statement terminated after
the tail) rather than weakened or deleted. Worth recording because "the controls stay green" was stated
confidently and was false.

**Scope changed once, deliberately.** `AddContinuousAggregatePolicy` was added at the plan grill after
probe E measured `add_continuous_aggregate_policy` transaction-safe. Without it this fix would have
documented a remedy the framework cannot perform — TASK-263's recorded failure, *"named an escape hatch that
did not open"*.

**Claim discipline:** the title says *"may never have worked through the runner"*. What is measured is
**"cannot work under the default configuration"**. "Never" is a claim about history that was not checked and
is not made.

### Close gate — 2026-08-24, two blockers found and fixed

Four passes ran (the diff interpolates caller text into SQL, so `security-review` triggered). Standards and
correctness each produced a blocker; fidelity and security were clean.

**Blocker 1 — register-on-introduce.** The change adds a new standing rule (*a statement the server refuses
inside a transaction is a provider limit the framework must route around, and family members need different
treatments*). Added to `CLAUDE.md § Conventions` plus a `### Recent Updates` entry.

**Blocker 2 — `code-review` finding 3, and it is a defect in this task's own new API.** The reviewer
observed that `add_continuous_aggregate_policy` refreshes a **moving window**, so a non-null `startOffset`
never materialises older history — while the refusal message and three doc blocks called the policy *"the
transaction-safe way to populate an aggregate"*. **Measured before accepting it**: a hypertable with one row
400 days old and one 2 days old, `startOffset = "30 days"`, job run → exactly **one** bucket, the old one
permanently absent, no error. Confirmed.

That is **TASK-263's failure — "named an escape hatch that did not open" — committed in the same change that
adds a § Conventions rule warning against it.** Worth recording plainly: writing the rule is not the same as
following it. The message and docs now distinguish *keep current* from *backfill history*, and
`A_refresh_policy_covers_a_moving_window_and_leaves_older_history_unmaterialised` pins the semantics.

**Finding 6 — fixed, and it was one of two.** `BuildCompressionPolicySql`'s remark said `QuoteIdentifier`
where the code uses `QualifiedIdentifier`. The reviewer named that instance; the **class-level doctrine
bullet** carried the same error and is the more dangerous one, since it is what the file tells authors to
reason from — following it reintroduces TASK-262's regression, whose `42P01` is swallowable. Both corrected.

**Findings 1 and 2** (schema-blind `IsHypertable` / `GetChunkInterval`) — already owned by **[[TASK-280]]**,
spawned at TASK-255's close. The reviewer re-found them independently, which is evidence that task is worth
doing rather than a reason to fold them in here.

**Finding 4** (the `UseTransaction = false` path silently yields an empty view where it used to populate) —
deliberate, that is option A, blast radius measured at 0 callers, documented on the method and in Recent
Updates. No action.

**Finding 5** (the TASK-255 parameter-rebinding hazard) — already measured and recorded there. No action.

**Fourth mutation, added after the gate:** force `start_offset` to `NULL` unconditionally → **3 red**,
including the new window pin, so the pin is a real test rather than decoration.

Final: **72 passed, 0 failed, 0 skipped** (61 → 72, **+11**), live TimescaleDB 2.29.2 / PostgreSQL 16.15,
no new nullable warning.

## Human test plan

- [x] N/A — mechanical; the proof is a migration run through the real runner in its default configuration,
      which is precisely what no existing test does.
