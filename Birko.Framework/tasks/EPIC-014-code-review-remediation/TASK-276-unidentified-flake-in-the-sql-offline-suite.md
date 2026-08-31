---
id: TASK-276
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: in-progress
priority: P2
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-273, TASK-259]
findings: []
pr: null
github-issue: null
jira-key: null
---

# One test in `Birko.Data.SQL.Tests` fails about 10% of full-suite runs, and its identity was never captured

## Context — found at TASK-273's close gate, and plausibly caused by it

Running the six SQL suites for TASK-273's final tally, `Birko.Data.SQL.Tests` reported **1 failed of 619**.
It has now been chased as far as repeated running can take it, measured 2026-08-22:

| Configuration | Result |
|---|---|
| full suite (619 tests), ~19 runs | **2 failures**, each 1 of 619 |
| full suite, 8 runs with `--logger trx` to capture the name | **8 clean** — never caught |
| `--filter FullyQualifiedName~IndexPredicateTests` alone (22 tests), 6 runs | 6 clean |
| `--filter FullyQualifiedName!~IndexPredicateTests` (597 tests), 5 runs | 5 clean |

So it needs the new class **and** the rest of the suite in one run, which is what makes a cross-class
interaction the leading hypothesis rather than a timing-sensitive test in isolation.

**The leading hypothesis, stated as a hypothesis.** `DataBase` caches loaded tables in a process-wide static
(`LoadTable` → `ConcurrentDictionary`), xUnit runs test *collections* in parallel by default, and this project
has no `xunit.runner.json` and no `CollectionBehavior` attribute — so everything shares one cache across
parallel classes. TASK-273 added **four deliberately-invalid entity types** (`IpUnmapped`, `IpNotNull`,
`IpPrimary`, `IpContradiction`) whose `LoadTable` is *supposed* to throw `TableAttributeException`. If the
cache can observe a partially-built table — populated before `LoadIndexes` runs, or shared `Fields`
dictionaries mutated during a concurrent load (`LoadIndexes` writes `field.IsIndexed`) — then a concurrent
reader could see a state that no single-threaded run produces. **Unconfirmed:** nothing in the evidence above
localises it to those types, only to "the new class plus the suite".

⚠ **Do not close this by re-running until it is green.** The failure rate is roughly 1 in 10 full-suite runs,
so a handful of green runs is the expected outcome whether or not anything was fixed. Either capture it, or
demonstrate the mechanism cannot occur.

## Second instance — identified, in a different suite (2026-08-23, while closing TASK-244)

`Birko.Data.Migrations.SQL.Tests` failed **1 of 49, once in 16 runs**, and this one was caught by name:

```
SchemaBuilderBoundaryLeakTests.Without_a_runner_transaction_nothing_is_published_either
System.ObjectDisposedException : Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
```

Measured against the pre-TASK-244 code (**0 failures in 10 runs**) and after it (**0 in a further 10**), so
it is not attributable to that change.

**Why it strengthens this task's hypothesis rather than being a separate one.** That test acquires its
connector through `DataBase.GetConnector<SqLiteConnector>(settings)` — the **process-wide cache**, keyed by
(type, settings id) — and a disposed `sqlite3` handle reached from it is precisely the shape of a connector
outliving the connection some other test gave it. Same mechanism family as the hypothesis above, now with a
named test, a named exception, and a suite where the shared object is explicit rather than inferred.
[[TASK-270]] owns the cache itself.

## Worked 2026-08-23 — NOT closed, and here is what changed

**Not reproduced in 95 runs, one hypothesis falsified, the mechanism still unestablished.** This section
exists so the next attempt starts from evidence instead of repeating today's.

### The rate has dropped, and the drop is statistically real

| Measurement | Result |
|---|---|
| `Birko.Data.SQL.Tests`, before today's work | **2** failures in ~19 runs |
| `Birko.Data.Migrations.SQL.Tests`, before today's work | **1** failure in 16 runs |
| `Birko.Data.Migrations.SQL.Tests`, today | **0** in 65 runs (25 + 40, every run's console output retained) |
| `Birko.Data.SQL.Tests`, today | **0** in 30 runs |

At the previously observed rate (~1 in 16) the chance of 95 clean runs is about **0.3%**, so the trigger is
very likely gone rather than merely quiet. What changed in between is the whole day's work: TASK-244, 265,
274, 275, 277 and 278.

⚠ **Do not read that as "fixed".** The mechanism was never captured, so nothing here explains *why* it
stopped, and this task's own warning applies in both directions: a run of green proves as little now as it
would have then.

### Falsified: `SqliteConnection.ClearAllPools()` interference

The leading hypothesis after the second sighting was that the **24 process-wide**
`SqliteConnection.ClearAllPools()` calls in per-class teardowns could dispose a connection another test class
was still using — which is the only obvious way to get `ObjectDisposedException: 'SQLitePCL.sqlite3'` rather
than a SQLite error code. **Measured, and it is wrong:**

- a live, open connection **survives** a foreign `ClearAllPools()`;
- a connection returned to the pool and reopened after a foreign clear works;
- 400 interleavings of `ClearAllPools()` against in-flight commands and pooled open/close cycles produced
  **no** failure.

So the pool clear is not the mechanism. Recorded so nobody spends the session on it again. (`ClearPool(connection)`
does exist as a targeted alternative, if a *different* reason to prefer it ever appears.)

### Also checked, and not the mechanism

- **A store disposing the cached connector.** There is no `Connector.Dispose()` call anywhere in
  `Birko.Data.SQL`, so a store's disposal does not kill the shared connector. (The project guide's example
  snippet shows one — that example is stale, not the code.)
- **Settings-id collision between parallel classes.** `GetId()` is `"{Location}:{Name}"` and every affected
  suite builds a fresh Guid-named temp directory per test, so parallel classes genuinely get distinct
  connectors.

### What was pinned instead

The criterion allows pinning whatever *is* deterministic when the interleaving cannot be forced. Every
hypothesis in this family rests on what `DataBase.GetConnector` shares, and **nothing asserted it** — several
suites reasoned about the keying in prose comments only. `Birko.Data.SQL.SqLite.Tests.ConnectorCacheTests`
(4 tests) now pins: same settings id → same instance; different location or name → different instance; the
sync and async caches hand out **two different instances of the same type** for one database; and the sharp
edge — two settings objects differing in anything *other* than location and name share one connector, so the
**first** caller's `CommandTimeout` wins for everyone and the second's is silently discarded. That last one is
[[TASK-270]]'s subject, which now starts from a measurement rather than a reading.

### Suggested next step

Do not loop these two suites again — that is now 95 runs of evidence. Watch for the **next sighting** during
ordinary work, and when it appears capture it immediately (`--logger "console;verbosity=detailed"` per run,
keep the artefacts). If it has not recurred after a few weeks of ordinary sweeps, cancel this task and say
which change is the likeliest cause rather than leaving it open indefinitely.

## Acceptance criteria

- [x] The failing test is **identified by name**, with its assertion message — for the second instance
      (`SchemaBuilderBoundaryLeakTests.Without_a_runner_transaction_nothing_is_published_either`,
      `ObjectDisposedException: 'SQLitePCL.sqlite3'`). The first instance in `Birko.Data.SQL.Tests` is
      still unidentified, and has not recurred in 30 runs. Suggested route, since a trx
      logger over 8 runs did not catch it: loop the suite until failure with the run's output retained
      (`dotnet test --logger "console;verbosity=detailed"` piped to a file per run, or
      `xunit.runner.json` → `"diagnosticMessages": true`), and keep the artefacts of the failing run.
- [ ] The mechanism is established, not guessed: either a concrete interleaving through `DataBase`'s static
      table cache, or a different cause entirely (a genuinely order-dependent pre-existing test that the new
      class merely perturbs by changing collection scheduling).
- [ ] Fixed at the mechanism. If it is the shared cache, the fix is in the framework or in the tests'
      isolation — **not** `"parallelizeTestCollections": false`, which would hide it here and leave every
      other suite exposed. If the cache can publish a table before its indexes are resolved, that is a
      production defect in its own right and gets its own task.
- [ ] A regression test that fails on the unfixed code. If the interleaving cannot be forced deterministically,
      say so and pin whatever *is* deterministic (e.g. that a failed `LoadTable` leaves nothing cached).
- [x] ⚠ Re-measure the failure rate over **at least 30 full-suite runs** before and after — done: 0 in 95
      runs today against 3 in ~35 before, which is inconsistent with the old rate at p≈0.3%. The
      remaining criteria stay open because a rate change is not a mechanism.

## Out of scope

- TASK-273's feature behaviour — green and mutation-proven; this is about suite stability.
- The unreproducible single SQLite failure recorded at TASK-259's close (228/229, seen once, green on four
  subsequent runs). Different suite, different provider, no evidence they share a cause — but worth a look if
  a shared-static mechanism is confirmed here.

## Human test plan

- [ ] N/A — the verification is a repeated-run measurement.

## Implementation plan

_Populated by `/tasks plan TASK-276` — leave empty until then._

---

## A consumer-side sighting on the same path, with the stack captured (Symbio, 2026-08-23)

Offered because this task's whole complaint is that the identity was never captured. This is **not** the
same test — it is in a consumer's suite, not `Birko.Data.SQL.Tests` — but it is the same **shape**, on the
path TASK-244 had just changed, at a comparable rate. Treat it as a second data point, not as a diagnosis.

**Where:** `Symbio.Tests.Unit.TransactionBoundaryTests.InitializeAsync` (the fixture, not a test body).
That fixture deliberately touches every table **outside** a boundary to warm the schema, so it is a
concentrated dose of exactly the schema-ensure path.

**Rate:** once in six consecutive full-suite runs (2096/2097). Passed **11/11 every time the class ran
alone** — it only appears under the full suite, which is the same signature this task describes.

**Stack, as printed:**

```
Symbio.DataAccess.RepositoryBase`1.CountAsync
  -> Birko.Data.Tenant.Stores.AsyncTenantStoreWrapper`2.CountAsync            (AsyncTenantStoreWrapper.cs:58)
  -> Birko.Data.Stores.AbstractAsyncStore`1.CountAsync                        (AbstractAsyncStore.cs:154)
  -> Birko.Data.Stores.AbstractAsyncStore`1.EnsureInitializedAsync            (AbstractAsyncStore.cs:43)
  -> Birko.Data.SQL.Stores.AsyncDataBaseStore`2.InitCoreAsync                 (AsyncDataBaseStore.cs:149)
  -> AbstractConnector.CreateTable(Type[])                                    (AbstractConnector_Create.cs:13 -> :20 -> :80 -> :87)
  -> AbstractConnector.DoDdlCommand                                           (AbstractConnector.cs:299)
  -> AbstractConnector.RunDdl                                                 (AbstractConnector.cs:310)
  -> AbstractConnector.DoCommandWithTransaction                               (AbstractConnector.cs:256)
  -> AbstractConnector.RunCommandTransaction                                  (AbstractConnector.cs:475)
  -> AbstractConnectorBase.ExecuteWithRetry                                   (AbstractConnectorBase.cs:119)
  -> Microsoft.Data.Sqlite.SqliteConnection.BeginDbTransaction                ← threw
```

⚠ **The exception type and message were NOT captured** — it fired once and the run was filtered to the
stack. Recorded as a gap rather than guessed at; the two candidates this tree has already seen on that line
are `SQLite Error 5: 'database is locked'` (TASK-243 R2, TASK-244 measurement 1) and
`ObjectDisposedException: 'SQLitePCL.sqlite3'` (TASK-244's own closing note). They imply different causes,
so it is worth capturing next time rather than assuming.

⚠ **Cannot be attributed to TASK-244.** Establishing whether it predates that change would mean reverting
committed framework work, which the consumer declined to do. The consumer's suite was green (2097/2097) on
one full run earlier the same day, before the change — that is n=1 and is not evidence either way.

**Reproduction hint if it helps:** the consumer runs the whole suite with `pwsh tools/build-and-test.ps1`
in `C:\Source\Birko\Consumers\Symbio`. It reproduced roughly one run in six there while never reproducing
under a class filter — consistent with this task's own standing hypothesis of the process-wide cached
connector (`DataBase.GetConnector`, TASK-270) being shared across parallel test classes.

## Three identities captured, 2026-08-31 (during TASK-287 / TASK-288)

The framework-side flake in `Birko.Data.SQL.SqLite.Tests` was hit four times while running that suite
repeatedly for unrelated work, and the **failing test names were captured** — which this task previously
had for the Migrations.SQL sighting only. Always exactly **one** test, never the same one twice:

| run | test |
|---|---|
| 1 | *(name not captured — grep filter dropped it)* |
| 2 | `ComputedContainsOperandTests.ABoundedComputedContains_StillDeletesExactlyItsOwnRows` |
| 3 | `DestructiveFilterEndToEndTests.ATranslatingFilter_DeletesExactlyTheMatchingRows` |
| 4 | `ComputedContainsOperandTests.AnUntranslatableOperand_ThrowsOnARead_RatherThanReturningTheWrongRows` |

**All three are end-to-end filter/delete tests against on-disk SQLite** — the family that opens a real
database per test and drives a destructive statement through it. That is a much narrower target than
"somewhere in the suite", and it is consistent with this task's standing hypothesis (the process-wide
cached connector from `DataBase.GetConnector`, TASK-270) being shared across xUnit's parallel collections.

⚠ **The exception message is still NOT captured**, on any of the four. Same gap as the Migrations.SQL
sighting; `-v q` prints the name and not the assertion. Next attempt should use a `trx` logger from the
start rather than a grep.

**Rate, and the reason it is worth writing down:** measured **1 failure in 16** full-suite runs against
*unmodified* code (with the new TASK-287 file removed), and **2 in 23** with the TASK-287/288 changes in
place. Statistically indistinguishable, so the flake is **pre-existing and not attributable to that work** —
which is the question that had to be answered before either task could report a clean suite.

⚠ **The first attempt at that control measured nothing**, and the way it failed is worth recording: it
stashed the framework change while leaving the new (untracked) test file in place, so the suite did not
compile — and counting runs that contained no `[FAIL]` marker scored a build failure as a **pass**. Zero
failures in sixteen runs, from a suite that never ran. **Count the greens, not the absence of reds.**

### ⚠ Correction to the section above, same day (during [[TASK-289]])

A **fourth** identity was captured, and it **falsifies the "end-to-end filter/delete family" reading**
written a few hours earlier:

| run | test |
|---|---|
| 5 | `LazyInitInsideBoundaryEndToEndTests.A_committed_boundary_around_a_stores_first_operation_still_persists` |

That is a lazy-init-inside-a-transaction-boundary test, not a filter or a delete. The honest common factor
across all four is narrower in one way and wider in another: **every one is an end-to-end test that opens a
real SQLite database file**, and they span at least three unrelated feature areas. So the correct
characterisation is the shared infrastructure, not the feature — which points harder at this task's own
standing hypothesis (`DataBase.GetConnector`'s process-wide cache, [[TASK-270]]) than the earlier reading did.

**Taken seriously rather than filed under the flake**, because this one landed in the area TASK-288 had just
changed (the store's init gate) and a "known flake" is exactly what a real regression would hide behind:

- **0 failures in 20 runs of that class in isolation** with the TASK-288/289 code in place.
- Full-suite rate with the changes is indistinguishable from the **1 in 16** measured on unmodified code
  (see the section above), and the failing identity moves from run to run, which a deterministic regression
  in the init gate would not.

⚠ **Residual uncertainty, stated rather than rounded off:** a full-suite pre-change control for *this
specific class* was not run, because the new test files do not compile against the pre-TASK-288 API and the
comparison would have needed three files removed and one reverted. The evidence above is strong but is not
that experiment. If this identity recurs, run that control before assuming the flake again.

## 2026-08-31, second pass — the message, a reproduction lever, and a hypothesis NOT confirmed

Worked directly rather than as a side-effect of another task. Three results, one of them the thing this
file has been asking for since it was opened, and one deliberately negative.

### 1. ⚠ The exception and stack are CAPTURED — the gap this task named twice

```
System.ObjectDisposedException : Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
   at SQLitePCL.SQLite3Provider_e_sqlite3...sqlite3_prepare_v2
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReaderAsync(...)
   at Birko.Data.SQL.Connectors.AbstractAsyncConnector.ReadOnAsync(
        DbConnection db, DbTransaction transaction, ...)   AbstractAsyncConnector.cs:376
   at Birko.Data.SQL.Connectors.AbstractAsyncConnector.RunReaderCommandAsync(...)   :336
```

Line 376 is `reader = await command.ExecuteReaderAsync(ct);`. So the **pooled inner `sqlite3` handle** is
disposed at the moment the reader is executed — not the `SqliteConnection` wrapper, which
`SqLiteConnector.CreateConnection` creates fresh every time (checked). It is therefore a lifetime race on
the *pool*, not on any object this framework holds.

This settles the two-candidate ambiguity recorded earlier: it is `ObjectDisposedException`, **not**
`SQLite Error 5: 'database is locked'`.

### 2. ⚠ The on/off rate has a trigger: MACHINE LOAD. That is a reproduction lever.

This file has twice concluded the flake "went quiet" and treated that as a mystery (0 in 95 after 3 in
~35). It is not a mystery — it is idleness. Measured today, same binary, same suite:

| condition | failures |
|---|---|
| idle machine, 40 runs | **0** |
| idle machine, 40 runs (repeat) | **0** |
| **8 CPU burners running**, 15 runs | **1** |
| **8 CPU burners running**, 40 runs | **1** |

0 in 80 idle is inconsistent with the ~1-in-15 rate seen while actively rebuilding, at p≈0.4%. Load widens
the window. **Every future experiment on this task must run under load**, or the control silently measures
nothing — which is exactly what happened in the first attempt below.

### 3. ⚠ The `ClearAllPools` hypothesis is NOT confirmed, and the numbers are recorded so nobody re-runs it blind

Seven test classes call `SqliteConnection.ClearAllPools()` in `Dispose()` while other classes run in
parallel, and a disposed *pooled inner handle* is exactly what that could produce. It looked compelling.

**First attempt, idle machine:** 0/40 without the calls — and then **0/40 with them**, i.e. the control
produced zero too. Reporting the first number alone would have been a false confirmation.

**Second attempt, under load:**

| arm | failures |
|---|---|
| with `ClearAllPools` | **2 / 55** |
| without `ClearAllPools` | **0 / 30** |

Fisher exact **p ≈ 0.53**. That is no evidence at all. At this rate the experiment needs roughly 200 runs
per arm to be worth anything. **Do not cite the 0/30 as support** — and do not remove those calls as a
"fix" on this basis; that is the guess-instead-of-measure failure this epic keeps recording.

### 4. Six identities now, and the earlier "family" readings were both too narrow

`ComputedContainsOperandTests` (×2 distinct tests), `DestructiveFilterEndToEndTests`,
`LazyInitInsideBoundaryEndToEndTests`, `SqLiteStoreCrudTests`, `RuleFieldResolutionEndToEndTests`,
`PrimitiveTypeRoundTripTests`. Filter/delete, lazy-init, CRUD, DDL-payload, type round-trip — the only
common factor is **an end-to-end test that opens a real SQLite file**, which is consistent with a pool
race and inconsistent with any per-feature explanation.

### ⚠ The question that matters more than the test flake, and is still open

**Can this happen in production, or does it need `ClearAllPools`?** Nothing in the framework calls it —
only these tests do. If the call is required, consumers are unaffected and this is test hygiene. If it is
not, then a concurrent web app sharing a cached connector can have a pooled handle disposed under an
in-flight async read, which is a real defect. The data above cannot separate those, and **that** is what
the next session should be designed to answer — not "make the suite green".

Suggested next experiment, since the lever now exists: under load, with `ClearAllPools` removed, ~200 runs
per arm; and separately a targeted harness that drives concurrent `RunReaderCommandAsync` against one
cached connector with **no** `ClearAllPools` anywhere, which answers the production question directly.
