---
id: TASK-252
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-248, TASK-249, TASK-149, TASK-254, TASK-472]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL, Birko.Data.Migrations.SQL, Birko.Data.TimescaleDB]
---

# Six latent per-provider gaps found while closing the index-DDL thread

## Context

The five-task index-DDL thread ([[TASK-245]] → [[TASK-246]] → [[TASK-247]] → [[TASK-248]] → [[TASK-249]])
surfaced six adjacent gaps that were each correctly kept out of the task in hand — and each ended up
recorded as **prose in a closed task's out-of-scope section**.

That is the defect [[TASK-149]] describes, arriving in the shape it warns about: only `status: todo`
**tasks** are ranked by `pick`, the `Next up` snapshot or `fix-next`, so six paragraphs inside finished
tasks are filed but **not scheduled**. This task exists so they are schedulable, not so they are urgent —
nothing here blocks a consumer today, and several may be closed as *decided not to fix* after a
measurement.

⚠ **Amended 2026-08-18: that is no longer uniformly true of #2.** It has since acquired a measured,
consumer-visible consequence and a dependent task — see § Item 2 below. The sentence above still holds for
the other five.

They are grouped rather than filed six times because they are one family (per-provider capability gaps in
SQL schema/DDL), all small, and splitting them would bury the connection that makes them cheap to do
together.

## The six

| # | Gap | Recorded in |
|---|---|---|
| 1 | **`RENAME COLUMN` is not universal.** `SqlSchemaBuilder.RenameField` emits it directly (it is the one schema operation with no connector equivalent). MySQL supports it only from 8.0; older versions need `CHANGE`. | TASK-247 |
| 2 | **Composite `PRIMARY KEY (a, b)` is unsupported through the schema builder — ⚠ NO LONGER LATENT, see § Item 2 below.** `AbstractConnector.CreateTable` renders `PRIMARY KEY` per column from each field's flag; the raw-SQL fallback that emitted a composite clause was deleted with the rest of the fallbacks. | TASK-247, measured by TASK-472 |
| 3 | **`IIndexBuilder.Sparse()` and `WithProperty()` are silent no-ops** on the SQL builder (`=> this`). § SH-H037's question: should a backend that cannot express something refuse rather than ignore it? | TASK-246 |
| 4 | **MySQL's 3072-byte index-key ceiling applies to *bounded* columns too.** A composite over several `VARCHAR(1000)` columns exceeds it, and nothing checks. Separate from the `LONGTEXT` problem TASK-248 fixed. | TASK-248 |
| 5 | **`byte[]` (`LONGBLOB`) is still unindexable on MySQL** — same error 1170 as the unbounded string, and no bound is applied because nothing in the tree declares such an index. | TASK-248 |
| 6 | **`AsyncDataBaseStore.InitCoreAsync` is sync-over-async** — it calls the *sync* `Connector.CreateTable` inside a `Task.Run`, so `CreateIndexesAsync` has **no store-level caller** and the async schema-ensure loop is reachable only via an explicit `CreateTableAsync`. | TASK-245, and `Birko.Data.SQL/CLAUDE.md` |

## ⚠ Item 2 has its measurement, and it is load-bearing (2026-08-18)

Filed as a capability gap with no known consequence. Consumer Symbio **TASK-472** measured one while
verifying the bulk-write transaction boundary on TimescaleDB, so #2 is the one item here that should
**not** wait for a survey — it already has one.

Three facts compose, and the third is what makes the gap bite:

1. `AbstractConnector.CreateTable` emits `PRIMARY KEY` **per column** from each field's flag, so two
   `HasPrimary` calls emit two clauses — measured on PostgreSQL 16 as
   `42P16 multiple primary keys for table "T" are not allowed`. A composite key is therefore not merely
   unsupported through the schema builder; it **cannot be declared at all** through `ModelMap`.
2. Bulk update and delete key on `Table.GetPrimaryFields()` and **do nothing without a primary key**, so
   "declare no key" is not a workaround for a store that needs those verbs.
3. TimescaleDB **refuses a unique index that omits the partitioning column** (`TS103`).

Together those force **the time column to carry the primary key** on any Birko hypertable, and make
`(Guid, Ts)` — the natural shape for telemetry, and what a consumer would reach for first — inexpressible.
That is the root cause underneath [[TASK-254]]: a Guid-keyed entity cannot be a hypertable, and after
TASK-472's identifier fix it now throws `TS103` out of lazy schema-ensure rather than silently degrading to
a plain table.

**Per this task's own last criterion, #2 should be split out as its own task when picked** — it has a
consumer-visible consequence, a measurement, and a dependent task, which is exactly the "genuinely
load-bearing" test. The remaining five stay grouped.

Note what this does *not* claim: no consumer is blocked today (Symbio has `TimeSeriesRecord` and no
subclass of it), and the per-column rendering is correct for every non-hypertable entity. The gap is that
the framework cannot express a shape one of its own providers requires.

## Why each needs a measurement before a fix

The thread that produced these also produced the rule that ought to govern them: **TASK-248's honest-looking
fix was measured and rejected**, because refusing an unhonourable declaration would have broken seven live
consumer entities on a provider where they work. Apply the same discipline here — for each of the six, the
first step is *how many real declarations does this affect*, not *what would the fix look like*.

Specific traps already visible:

- **#1** — check whether any consumer calls `RenameField` at all before hardening it. A 2026-08-18 sweep of
  all 16 consumer repos found **0 uses of `ISchemaBuilder`**, so this may be entirely theoretical.
- **#3** — refusing `Sparse()` is a breaking change for a caller that passes it harmlessly today. § SH-H037
  requires the opt-out to exist *and* be checked; a silent no-op that nobody relies on may be the right
  answer, recorded as such.
- **#4** — MySQL rejects an over-long key at `CREATE INDEX` time, so this already fails loudly rather than
  silently. That makes it a *diagnostics* question (does the framework explain it?) not a correctness one —
  confirm before treating it as a defect.
- **#6** — the `Task.Run` is not obviously wrong: it keeps a sync connector off the caller's thread. The
  reportable part is that a revert of the async index loop fails **0** tests because nothing reaches it,
  which is a coverage fact worth recording even if the code stays.

## Acceptance criteria

- [x] Each of the six carries a recorded verdict: **fixed**, or **decided not to fix** with the measurement
      that settled it. A verdict with no measurement behind it does not count.
- [x] **#2 split out as its own task on pick** — its measurement is already recorded above and it has a
      dependent ([[TASK-254]]), so it no longer belongs in a grouped latent-gaps task. Do not re-survey it;
      do decide whether the fix is a table-level `PRIMARY KEY (…)` clause in `CreateTable` or an explicit
      refusal of a second `HasPrimary` (today's `42P16` is the DDL layer refusing, not the mapper).
- [x] Anything fixed ships with a regression test and a revert count, per this epic's standing practice.
- [x] Anything declined is written into the relevant `CLAUDE.md` so the next reader finds the decision
      rather than rediscovering the gap — the point of this task is that these stop being invisible.
- [x] #6's coverage fact (an async loop with no production caller) is recorded wherever the outcome lands,
      whether or not the `Task.Run` changes.
- [x] If any item turns out to be genuinely load-bearing for a consumer, split it out as its own task
      rather than growing this one — the same rule that produced these six in the first place.

## Out of scope

- The index-DDL work itself — all five tasks are closed and landed.
- Spec regeneration for the affected areas — [[TASK-251]].
- The `_loose` pile / DV5 ×17, and the story-level scheduling defect that made this task necessary
  ([[TASK-149]]).

---

## Worked 2026-09-08 — six verdicts, and half the list had already been closed by other work

### ⚠ Step 0's headline: three of six were resolved by later tasks, and this file did not know

Filed 2026-08-18. Between then and now, TASK-266 and TASK-274 landed in the same area. Checked against the
current code before designing anything:

| # | Gap | Verdict |
|---|---|---|
| 1 | `RENAME COLUMN` not universal | **Declined, measured** — see below |
| 2 | Composite `PRIMARY KEY` inexpressible | **Split out → [[TASK-303]]**, per criterion 2 |
| 3 | `Sparse()` / `WithProperty()` silent no-ops | **Already fixed by TASK-274** — real implementations in all six schema builders, compound `Sparse()` refused rather than given one of two readings, covered by `SparseIndexBuilderTests` |
| 4 | MySQL's 3072-byte ceiling on *bounded* columns | **Already answered by TASK-266** — pinned rather than guarded, with the measurement (4 × `VARCHAR(255)` = 4080 bytes is `ERROR 1071` on MySQL, while SQL Server creates it with a warning and fails only on wide values, so a framework guard would duplicate one server and regress the other). `BinaryAndWideCompositeIndexLiveTests` records it |
| 5 | `byte[]` unindexable on MySQL | **Already fixed by TASK-266** — `MySQLConnector.ConvertType` reads `IsInIndexKey` and bounds the column; `BinaryIndexKeyColumnTypeTests` covers it |
| 6 | `InitCoreAsync` sync-over-async | **Code unchanged; the coverage claim corrected** — see below |

So the substantive work here was two verdicts and a split, not six fixes. **A grouped latent-gaps task
should be re-checked item by item before being worked** — its whole premise is that the items sat still,
and adjacent tasks moving is exactly what makes them stop sitting still.

### #1 — declined, with the measurement that settles it

- **0 callers.** `ISchemaBuilder.RenameField` is called from nowhere in the framework, its tests, or any of
  the 16 consumer repos (re-measured 2026-09-08). Only the six schema builders *implement* it.
- **The fallback is not a dialect swap.** `Birko.Data.SQL.MySQL/CLAUDE.md` declares support from **5.7**
  while `RENAME COLUMN` needs **8.0+**. Measured on 8.4.11: `ALTER TABLE t CHANGE b b2` without a type is
  `ERROR 1064`; only `CHANGE b b2 VARCHAR(50)` works. So a 5.7 path must read the column's **full
  definition** from the catalogue and restate it — which also risks silently altering a column that a
  rename should leave alone.
- Recorded **on the method** and, more importantly, **on the MySQL `CLAUDE.md` beside the version claim it
  contradicts**, so it is discoverable from the promise rather than only from the implementation.

### #6 — ⚠ this task's own premise was wrong, and the correction is the finding

It predicted *"a revert of the async index loop fails **0** tests because nothing reaches it"*. Measured by
making `CreateIndexesAsync` throw and running five suites: **5 failures**, in three of them.

```
SqLite      UnbuildableIndexEndToEndTests.TheAsyncSchemaEnsurePathDegradesTheSameWay
PostgreSQL  DeclaredIndexLiveTests.Declared_indexes_are_created_by_the_async_schema_ensure_too
MySQL       PartialIndexPolicyLiveTests.An_explicit_async_create_indexes_call_throws_for_a_where_null_declaration
MySQL       DeclaredIndexLiveTests.Declared_indexes_are_present_after_async_schema_ensure
MySQL       DeclaredIndexLiveTests.A_unique_index_over_violating_data_is_recorded_not_thrown_async
```

The claim was true when written; **TASK-273's close gate added the async-funnel coverage it said was
missing**, and TASK-245's own note ("an async store's schema-ensure runs the *sync* `CreateTable`") remains
true of the store path while these tests reach the loop through explicit `CreateTableAsync`. The
`Task.Run` is unchanged — it keeps a sync connector off the caller's thread, and this task itself said it
is "not obviously wrong". **A recorded coverage fact has an expiry date exactly like a blast radius does.**

### Verified

Doc and comment changes only, plus the split. `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16,
MySQL 8.4 and SQL Server 2022: Migrations.SQL **87**, MySQL **119**, SQL **678** — 0 failed.
