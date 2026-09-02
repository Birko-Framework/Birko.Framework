---
id: TASK-296
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-276, TASK-285, TASK-286, TASK-287, TASK-288, TASK-290, TASK-294]
findings: []
pr: null
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

- [ ] The remedy is chosen against a measurement of the **steady state**, not only the storm.
- [ ] WAL is evaluated as the alternative, and the choice between the two (or both) is stated with its
      reason.
- [ ] The reproduction in `ColdTableStormTests` shows 0 escapes on the shipped default afterwards, and its
      pooled/unpooled control pair is updated to say what the new default is.
- [ ] The other three providers are checked with the same storm shape, now that TASK-295 makes their
      escape channel work — a "should not arise" is a prediction until measured.
- [ ] Whatever is chosen, `SqLiteSettings.GetConnectionString()` remains overridable so a consumer can opt
      back out, and that opt-out has a test (§ SH-H037).
