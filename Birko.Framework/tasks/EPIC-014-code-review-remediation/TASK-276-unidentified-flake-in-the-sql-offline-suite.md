---
id: TASK-276
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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
