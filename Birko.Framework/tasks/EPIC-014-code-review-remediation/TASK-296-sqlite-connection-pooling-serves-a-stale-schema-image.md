---
id: TASK-296
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-276, TASK-285, TASK-286, TASK-287, TASK-288, TASK-290, TASK-294]
findings: []
pr: e37c871 (Birko.Data.SQL.SqLite) + baa1ffd (SqLite.Tests) + 766aef8 (PostgreSQL.Tests)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL.SqLite]
---

# SQLite connection pooling serves a stale schema image, so a freshly created table reads as missing

## The measurement this exists to act on

[[TASK-290]] Round 2 named the mechanism behind consumer Symbio's schema-ensure escapes: **a statement on
a pooled `sqlite3` handle is answered from a schema image older than a `CREATE TABLE` another connection
has already committed.** The single-variable control is in the tree
(`ColdTableStormTests`, opt-in via `BIRKO_STORM`):

| variant | runs | escapes | `created` | thrown failures | duration |
|---|---|---|---|---|---|
| pooled — the framework's default connection string | **7** | **2, 8, 8, 6, 9, 8, 5** | 200/200 | **0** | 34-41 s |
| `Pooling=False`, nothing else changed | **4** | **0, 0, 0, 0** | 200/200 | **0** | **15-16 s** |

And the failure is a *stale read*, not a missing table: an `OnSchemaEscapeDetected` handler that opens its
own connection **synchronously at detection** reports `presentNow=True` every time.

⚠ **Each escape serves a silently wrong `0`** — no exception, no log line unless a host subscribes — because
TASK-285 answers a missing table on a count with `0`. This is not a diagnostic nicety; it is a wrong answer
on a list route's total count, on the provider the consumer deploys.

## Why this is filed rather than done in TASK-290

`SqLiteSettings.GetConnectionString()` emits only `Data Source`, an optional `Password` and
`Default Timeout`, so pooling is on by default. Appending `;Pooling=False` is one line and eliminates the
defect in the measurement above. But it changes the shipped connection behaviour of **every** SQLite
consumer, and TASK-290's job was to name the mechanism, not to spend its blast radius. The numbers that
make the change look free are from one workload on one machine.

## What has to be measured before choosing

1. **Steady-state cost, not storm cost.** The storm is 2.4× *faster* unpooled, which is the opposite of the
   usual assumption and is exactly why it should not be trusted from one workload. Measure a warm,
   low-concurrency read/write mix — the ordinary case — and file-handle churn over a long run.
2. **WAL as the alternative.** `journal_mode=WAL` has a genuinely different snapshot mechanism and is the
   other candidate remedy; it may fix this *and* keep pooling. It is **not** a connection-string keyword,
   so it needs a PRAGMA issued on open — real framework plumbing, and a decision about whether the
   framework should be setting a journal mode at all. Note the framework currently sets none, so every
   Birko SQLite database runs on the `delete` journal.
3. **Whether both are wanted.** They are not exclusive. If WAL fixes it, pooling can stay.
4. **The `ClearAllPools` interaction.** [[TASK-276]] measured that this project's 24 process-wide
   `SqliteConnection.ClearAllPools()` teardown calls cause cross-class `SQLITE_BUSY` at suite scale.
   Disabling pooling makes every one of those calls a no-op, which may quietly resolve that flake — or
   mask it. Worth measuring in the same pass, and worth not conflating.
5. **Does it affect the other providers?** The mechanism is SQLite's schema cache plus
   Microsoft.Data.Sqlite's pooling. PostgreSQL, MySQL and SQL Server pool too, but their catalogue
   visibility is transactional and server-side, so the same staleness should not arise — **should not** is
   a prediction, and since [[TASK-295]] the escape channel now works on all of them, so it is cheap to
   check with the same storm shape ported to one of them.

## What must not happen

⚠ **Do not "fix" this by making a count of a missing table throw again.** TASK-285 removed that
deliberately, and a saturated or stale read faulting instead of answering is [[TASK-294]]'s separate
decision. The defect here is the stale image, not the answer given to it.

⚠ **Do not silently rely on TASK-288's healing to cover it.** The heal works — an escape bumps
`SchemaGeneration` and the next operation re-schema-ensures — but the operation that hit the stale image
already returned a wrong count, and healing does not retract it.

## Acceptance

- [x] The remedy is chosen against a measurement of the **steady state**, not only the storm — and the
      steady state **inverted** the storm's verdict on pooling.
- [x] WAL evaluated and chosen, with the reason: it fixes the defect, keeps pooling, and is 5× faster
      warm where `Pooling=False` is 1.5× slower.
- [x] The reproduction shows 0 escapes on the shipped default, and the control pair now includes a
      rollback-journal variant that **still fires** — which is what stops that 0 being a broken repro.
- [x] PostgreSQL checked with the same shape: 0 escapes, 0 failures, 2 of 2. MySQL and SQL Server
      explicitly **not** measured, with the reason stated rather than implied.
- [x] The opt-out is a settable `SqLiteSettings.JournalMode` (`"DELETE"`, or null/empty for "leave the
      file alone"), and both have tests. `GetConnectionString()` is untouched and still overridable.

---

## Closed 2026-09-02 — the remedy is WAL, and the steady state is what chose it

### The measurement that decided it, and it inverted the storm

The storm made `Pooling=False` look free — 2.4× *faster*. That is a contention artefact. Measured on the
ordinary case instead (warm store, sequential, 200 × write + count + filtered read,
`ConnectionModeSteadyStateTests`):

| configuration | run 1 | run 2 | vs default | escapes (storm) |
|---|---|---|---|---|
| pooled + rollback journal — the old shipped default | 1,801 ms | 1,819 ms | — | **7 of 7 runs, 2-9 each** |
| **unpooled** + rollback journal | 2,731 ms | 2,791 ms | **1.52× slower** | 0 of 4 |
| pooled + **WAL** | **351 ms** | **369 ms** | **0.19× — 5× faster** | **0 of 5** |

So WAL wins on every measured axis: it removes the defect, keeps pooling, is 5× faster warm and 20-40×
faster under the storm. `Pooling=False` removes the defect and costs 52% in the ordinary case.

### The fix

`SqLiteSettings.JournalMode`, defaulting to `"WAL"`, applied **once per connector** by
`SqLiteConnector.ApplyJournalMode` — once is enough because WAL is persistent in the database file, and it
runs on a connection of its own because `CreateConnection` returns an unopened connection by contract.

⚠ **The whitelist is two values, and that is a measurement, not caution.** SQLite persists a journal mode
in the file **only for WAL**; the rollback modes are a *per-connection* property. Measured
(`Which_journal_modes_persist_across_connections`, kept in the tree): set `TRUNCATE`, `PERSIST`, `MEMORY`
or `OFF` on one connection and a new connection reports `delete`. Since this seam applies the PRAGMA once,
on its own connection, accepting those four would take the value and **silently do nothing** — the
silent-drop shape § SH-H037 forbids. So they are refused, with a message that says why. `DELETE` is kept
because it is meaningful: it takes a database back *out* of WAL, persistently.

Three properties, each tested:

- **The value is whitelisted**, because it is interpolated into `PRAGMA journal_mode=…`, which takes no
  parameter — § Conventions' identifier family at a fourth kind of sink, a bare keyword in statement
  position, where refusal is the only containment (TASK-255's reasoning). A payload never reaches the
  database.
- **A mode that cannot be applied is RECORDED, not thrown** — `JournalModeInEffect` /
  `JournalModeFailure`, on the same terms as `IndexCreationFailures` (TASK-204) and `SubscriberFailures`
  (TASK-289). A journal mode is a concurrency property, not the caller's operation, so a database that
  cannot take WAL must still be usable. ⚠ And `JournalModeInEffect` must be **read** rather than assumed:
  WAL needs shared memory and does not engage on most network filesystems, and SQLite reports the mode
  actually in force rather than failing.
- **Applied once, proved by poisoning the setting afterwards.** An earlier version of that test asserted
  the mode was still right after twenty operations, which a per-statement implementation would also have
  satisfied — it pinned the outcome, not the once-ness.

### The cross-provider answer

**PostgreSQL: 0 escapes, 0 failures, 2 of 2 runs** (`ColdTableStormLiveTests`, 60 distinct cold tables ×
3 concurrent callers in waves). Its catalogue is server-side and its visibility transactional, so the
SQLite mechanism has no analogue — measured rather than predicted, as the acceptance asked.

⚠ **It was only askable because of [[TASK-295]].** Before that, `TablesCreated` was permanently empty on
this provider, so a run against that code would have reported a clean 0 for entirely the wrong reason.
MySQL and SQL Server were **not** measured: same server-side transactional catalogue, and the fix is
SQLite-only, so they are unaffected either way. Stated rather than implied.

### Measurements

`BIRKO_REQUIRE_LIVE` set, live PostgreSQL 16 and on-disk SQLite: **1,269 tests, 0 failed, 0 skipped**
across eight suites — `Birko.Data.SQL` 667, SqLite **331** (310 → 331), PostgreSQL **98** (96 → 98),
Migrations.SQL 54, SqLite.View 9, InMemory 69, JSON 23, XML 18. The SQLite suite is **4 of 4 clean** on
repeat runs, and identical with `BIRKO_STORM` set (331 either way).

**Mutations, disjoint:**

| mutation | red |
|---|---|
| default back to `"DELETE"` | **3** guards **and the storm produces 15 escapes** and fails — the fix's proof in both directions |
| whitelist removed (raw value interpolated) | **3** — the payload and per-connection-mode cases |
| throw instead of recording an unusable mode | **3** — the store must survive a journal mode it cannot have |

### ⚠ Two fixture faults of my own, both worth recording

1. **A test that was flaky by construction, twice.** It set the file to `TRUNCATE` and expected to find it
   there later. It passed in isolation only because connection pooling happened to hand the store the same
   handle that had set it in memory, and failed at random in a full parallel run. The cause is the very
   fact this task is about — only WAL persists — which is why that probe is now a committed test rather
   than a note. **A mode that does not persist cannot be used as a fixture's distinctive marker.**
2. **`An_explicit_DELETE_is_honoured` was vacuous as first written.** It asserted `delete` on a fresh
   database, where delete is the default — so it passed however the code behaved. It now starts the file
   in WAL and shows it reverted, which is a real assertion about a persistent change.

### Deliberately not done

- **`Pooling=False` was not shipped.** It fixes the defect and costs 52% in the ordinary case; WAL fixes it
  and pays back 5×. The unpooled storm variant stays in the tree as the control that *named* the
  mechanism, labelled as not being the remedy.
- **MySQL and SQL Server storms**, per the reason above.
- **Nothing about the count path.** [[TASK-294]] still owns the fact that a count under lock contention
  faults; WAL reduces that contention sharply but does not change the decision.
- **No consumer-side change.** Symbio picks this up by rebuilding — see the report.
