---
id: TASK-290
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-270, TASK-276, TASK-285, TASK-286, TASK-287, TASK-288, TASK-289]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.SqLite]
---

# Name the mechanism behind the schema-ensure escape

## The question, and why five landed changes have not answered it

> **Why does a statement report a table missing that this connector demonstrably created and committed,
> while the store's init gate had passed?**

This is the open half of consumer **Symbio TASK-602** (its criterion 4). Five framework changes have
landed on this thread and all of them work — [[TASK-285]] (a count of a missing table answers `0`),
[[TASK-286]] (the two-timestamp annotation), [[TASK-287]] (`AbstractConnector.SchemaEscapes` +
`OnSchemaEscapeDetected`), [[TASK-288]] (a vanished table heals on the next attempt), [[TASK-289]]
(`SubscriberFailures`). Symbio subscribes and the channel fires. Every one of them makes the *consequence*
correct or observable. None of them says what happens.

⚠ **Thirteen hypotheses have already died by code reading** (Symbio TASK-602, rounds 1-7). This task does
not add a fourteenth. It reproduces the condition **inside this repo** and then measures the three facts
that discriminate between the survivors.

## What is already MEASURED — do not re-derive it

Reproduced on the consumer side 2026-09-02, 4 fresh-database cycles out of 4, 12 annotated escapes,
8 tables, 5 modules (harness: Symbio `tools/reproduce-schema-escape.mjs`, commit 26686a52):

- **All twelve are `SELECT count(*)`.** WARNING: see the corrections below — this is an instrument
  artefact.
- **The trigger is LOAD, not timing.** ~190 *different* cold tables touched concurrently against one
  connector and one file. Same-table concurrency reproduces nothing (it serialises on that store's
  `_initLock`, `AbstractAsyncStore.cs:39`). An idle API reproduces nothing: cold-table schema-ensure costs
  1-5 ms there, so there is no window to aim at — the 219 ms window seen in the field was a property of
  the load, not of schema-ensure.
- **Escapes arrive in sub-microsecond pairs on DIFFERENT tables**, twice in one cycle:

      BreedingRecords  created ...00.8261120   missing ...00.8652698
      FeedingLogs      created ...00.8305398   missing ...00.8652691   <- 0.7 us apart
      Movements        created ...00.8394097   missing ...00.8698605
      HealthRecords    created ...00.8346542   missing ...00.8698618   <- 1.3 us apart

  Windows 28-66 ms (792 ms under heavier load).
- `PRAGMA journal_mode` is **`delete`**, not `wal`. `read_uncommitted` is `0`.
- **`SQLite Error 5` = 0 in all five cycles.** WARNING: see the corrections below — this does not mean
  what it was read to mean.
- Every escape came from a **GET list route whose service opens no `ITransactionBoundary`**, so no
  rollback was involved.
- The escapes served **zero 500s** — 200 with a silently wrong count, because TASK-285 answers a missing
  table with `0`. Watching for errors will not find this.

## Two corrections found by reading this repo, before any measurement

Both change how the existing evidence should be read. Neither is a new hypothesis about the cause.

### 1. "All twelve are counts" is a property of the INSTRUMENT, not of the anomaly

A read can never produce an escape record. `AbstractConnector.RunReaderCommand` and `RunReaderCommandOn`
catch the condition at the reader itself — `catch (Exception ex) when (IsMissingTableException(ex))` then
`yield break` — so a `SELECT` never reaches `InitException` and therefore never reaches `OnException`,
`EnsureSchemaAndReport`, TASK-286's annotation, TASK-287's record or TASK-288's generation bump. Only
**counts** (`DoCommand` then `RunCommand` then `InitException`) and **writes** can be seen at all.

So the escape population is (counts union writes), and a GET list route emits exactly one of those two:
the count. **The statement shape therefore carries no information about the mechanism** — a `SELECT` on
the same table in the same instant would have returned an empty list and left no trace. Any reasoning of
the form "it is specific to the scalar path" is unfounded, and TASK-602's own round-5 framing leans that
way.

### 2. `Error 5 = 0` is not evidence of an absence of lock contention

`SqLiteSettings.GetConnectionString()` emits `Default Timeout={CommandTimeout}` with `CommandTimeout = 30`,
and `RetryPolicy` defaults to `RetryPolicy.None` so `ExecuteWithRetry` does not retry. If
Microsoft.Data.Sqlite absorbs `SQLITE_BUSY` / `SQLITE_LOCKED` inside that timeout — **to be measured, not
assumed** — then contention is invisible by construction and a zero count is what a blind instrument
reports. That matters because `Error 5 = 0` is currently used *in support of* the stale-image hypothesis
("a lock-contention explanation would not fit"). If the driver swallows BUSY, the support is void and
lock contention returns to the candidate list.

### And one hazard that must be excluded before the pairs are read as one mechanism

`CreatedTablesNamedIn` matches a recorded table name against the statement by **substring**:
`commandText.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)`. Its own comment justifies that with
*"a false positive costs one extra line in an exception nobody sees unless something already went wrong"*
— and **that justification stopped being true at TASK-288**, which made the same answer drive
`SchemaGeneration` and hence every store's `CanTrustRememberedInitialization`.

Measured against Symbio's 244 `[Table("...")]` declarations, three of the eight escaped tables are proper
substrings of another table:

| recorded name | is a substring of |
|---|---|
| `Movements` | `StockMovements` |
| `Reservations` | `StockReservations`, `TableReservations` |
| `Events` | `AlarmEvents` |

So a count on cold `StockMovements` annotates as anomalous on the strength of `Movements` having been
created. The consumer's records quote only the created-name half for the storm cycles, so this is
**unexcluded for up to 5 of the 12 escapes**. The micro-second pair that matters survives it —
`BreedingRecords`, `FeedingLogs` and `HealthRecords` are substrings of nothing — so the multiplicity
finding stands. But two things follow:

- the escape **counts** are contaminated, and
- a *false* anomaly bumps `SchemaGeneration`, which invalidates the remembered init of **every** store on
  that connector, so ~190 stores re-run `CREATE TABLE IF NOT EXISTS` under `lock(_lock)` while their
  counts wait. That is a **positive feedback loop keyed on load**, which is exactly the profile the
  trigger has. Whether it is cause, amplifier or noise is what a controlled reproduction can separate —
  which is why the reproduction below uses fixed-width table names with no substring relations.

## The surviving hypotheses

| # | Hypothesis | What would distinguish it |
|---|---|---|
| A | One statement answered against a **schema image older than those `CREATE TABLE`s** — a reader holding SHARED across a writer's RESERVED reads the pre-commit page 1, so *every* table created since is absent, which makes several tables failing together natural | the failing connection's `PRAGMA schema_version` is **behind** the current value |
| B | Lock contention absorbed by the driver's busy handling, surfacing as something other than `Error 5` | reproduce with `Default Timeout` at 0/1 and see whether BUSY appears where the escape was |
| C | A connection carrying an **already-open transaction** (pooled handle, or an ambient entry) so the statement runs in a snapshot older than its own store's DDL | a transaction is open on the failing connection, and it started before the create |
| D | The substring matcher fabricating the anomaly, with TASK-288's invalidation amplifying it into a storm | fires with substring-colliding names and not with fixed-width ones |
| E | **Correct SQLite behaviour under concurrent DDL, and the consumer must not do that** | A survives measurement and no framework change can remove it |

WARNING: **E is a legitimate outcome and must be stated plainly if it is the answer**, not left implied —
it changes what Symbio should do rather than what this framework should ship.

## What to measure, in order

### Step 0 — reproduce it in this repo, before touching production code

`Birko.Data.SQL.SqLite.Tests`: N (~200) distinct entity types over **one** SQLite file and **one** shared
connector, every store first-touched concurrently, then `CountAsync` on each. Score on the **record**
(`connector.SchemaEscapes`), never on a log line or a thrown exception, and count the benign control
(unannotated `no such table`) in the same run — a zero-escape result means nothing without it.

Constraints already known to matter:

- **~190 *distinct* cold tables, not one table hit 190 times.** Same-table concurrency serialises on
  `_initLock` and reproduces nothing.
- **Fixed-width table names** (`Probe000` ... `Probe199`), so no name is a substring of another and
  hypothesis D cannot contaminate the result. A second run with deliberately colliding names is how D
  gets measured rather than assumed.
- Its own database file and its own connector-cache entry, per `ConnectorCacheTests` /
  `VanishedTableHealingTests` — a shared connector lets one test's escape change what the next measures.
- 200 model types are needed. Either a checked-in generated `.cs` or `TypeBuilder`; decide when writing it
  and record which and why.

**If Step 0 does not fire, stop and report that.** Everything below is speculative instrumentation until
it does, and shipping public surface to chase a condition this repo cannot produce is the wrong trade.

### Step 1 — the three probes, once there is something to point them at

Extend the escape record with the three facts that settle A / B / C:

1. **connection identity** — `RuntimeHelpers.GetHashCode` on the `DbConnection`, plus whether it came from
   the pool. Do the paired escapes share one connection?
2. **whether a transaction was already open on that connection**, and since when.
3. **`PRAGMA schema_version` on the failing connection at failure time vs. the current value.** The
   decisive one, and cheap: a cookie that is *behind* proves A outright instead of inferring it.

Two plumbing constraints, both already visible in the code:

- `EnsureSchemaAndReport` is reached through `InitException(ex, commandText)` and **has no access to the
  connection**. `InitException` is `public virtual` and `OnException` is a public delegate type a consumer
  subscribes to, so widening either signature is a breaking change — and per [[TASK-278]] adding a
  parameter to a `public virtual` silently orphans existing overrides. The capture therefore has to happen
  at the failure site (`RunCommand`'s catch, inside the `using`, where the connection is still alive) and
  travel to the handler by some other route. Shape decided in Step 1, not now.
- `PRAGMA schema_version` is SQLite-only, so it is a provider override returning null on the base — the
  `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` family. And it must run **only in the anomalous
  branch**: the benign path is ~245x more common per bring-up and has to stay free.

### Step 2 — name it, then decide whether anything should change

## Acceptance

- [ ] The mechanism is **named**, with a measurement that distinguishes it from the alternatives above —
      not an inference from code reading.
- [ ] A framework-level reproduction exists in the tree and fires reliably, with its benign control
      counted in the same run, and mutation-proven not to be vacuous.
- [ ] The two corrections above are either confirmed by measurement or withdrawn, in writing. Neither may
      be left standing as a plausible-sounding claim.
- [ ] Hypothesis D is measured, and if the substring matcher can fabricate an anomaly the consequence for
      `SchemaGeneration` is stated (and given an id if it is not fixed here).
- [ ] If a fix lands it does **not** re-introduce a 500 on the count path (TASK-285 removed that
      deliberately) and does **not** make writes quiet (TASK-277: a write against a missing table must
      keep reporting).
- [ ] If the answer is "correct SQLite behaviour under concurrent DDL, and the consumer must not do that",
      it is said plainly.

## Out of scope

- **`EnsureSchemaAndReport` rewraps a `TaskCanceledException` as an unhandled 500.** [[TASK-291]] owns it.
- Whether the connector cache should hold per-caller state at all — [[TASK-270]].
- The unidentified `Birko.Data.SQL.Tests` flake — [[TASK-276]], which also hypothesised about
  `DataBase.GetConnector` sharing.
