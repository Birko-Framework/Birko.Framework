---
id: TASK-270
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-204, TASK-240, TASK-259]
findings: []
pr: "Birko.Data.SQL 16ae547 + Birko.Data.SQL.Tests 021b2c3"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# `DataBase.GetConnector` shares one connector process-wide, and three separate features have put per-caller state on it

## Context — spawned by TASK-259, which removed the third instance

`DataBase.GetConnector<T>(settings)` is a **static, process-wide, unbounded cache keyed by (type, settings
id)**. There is no lifetime, no eviction, no disposal and no DI seam. The caching itself is defensible —
ADO.NET pools connections, a connector is mostly behaviour (SQL emission, type mapping), and a stable
identity per database is exactly what `AmbientSqlTransaction.Find(settingsId)` needs. **The problem is that
the object is reachable, shared and long-lived, so it keeps attracting state that belongs to one caller or
one operation.**

Three independent instances have shipped, all on the same object:

| Instance | State put on the shared connector | Symptom | Fixed by |
|---|---|---|---|
| stores' unit-of-work enlistment | one caller's `DbTransaction` | concurrent callers silently enlisted in each other's transaction | TASK-240 (`AmbientSqlTransaction`) |
| index-creation reporting | an append-only `List<IndexCreationFailure>` | grew one entry per HTTP request forever, re-firing its event each time | keyed, per § Conventions |
| migrations' schema builder | one migration's connection **and** transaction | published, never cleared; the runner then disposed both, and the next store's lazy schema-ensure ran on the dead connection and threw — leaving that store permanently uninitialised | **TASK-259** (deleted the mechanism) |

Three developers independently reached for "just put it on the connector". That is a design signal, not three
coincidences.

## What remains — audited at TASK-259's close, and nothing is currently firing

State the shared connector still exposes:

- **`RetryPolicy { get; set; }`** (`AbstractConnectorBase.cs:32`) — **publicly settable on a shared object**,
  so one caller's retry policy would silently become every caller's, against the same database. Exactly the
  `SetExternalTransaction` shape. **Latent: measured 0 assignments anywhere in the framework or in any of the
  16 consumer repos.** It is a trap rather than a bug, and it is one line from being a bug.
- **`IsInitializing { get; protected set; }`** (`AbstractConnectorBase.cs:21`) — a mutable flag on an object
  shared by every concurrent caller.
- **Four events** — `OnInit`, `OnException`, `OnExecute`, `OnIndexCreationFailed`. Both stores do
  `Connector.OnInit += onInit`. A symmetric `RemoveOnInit` exists, so this is opt-in rather than a framework
  leak, but a scoped store per HTTP request that forgets it adds a handler per request for the process
  lifetime — the same accumulation the index-failure list already demonstrated.
- **`_indexCreationFailures`** — keyed now, so no longer unbounded, but still process-global accumulation that
  no host subscribes to.

Also worth stating plainly: the cache key is `settings.GetId()`, so correctness rests entirely on that being a
faithful identity. Two different settings producing the same id would share a connector silently.

## What to decide

1. **Is the connector's contract "immutable configuration + behaviour"?** If yes, say so on the type and make
   it true: `RetryPolicy` becomes constructor/settings-supplied rather than settable, `IsInitializing` stops
   being observable state, and per-operation context travels with the flow (which is where
   `AmbientSqlTransaction` already put it). That is the direction the framework has been drifting for three
   tasks; this would finish it.
2. **Should there be a DI seam?** Today a host cannot substitute, scope or dispose a connector, two
   configurations of the same provider+settings-id cannot coexist, and tests cannot isolate. An injectable
   factory alongside the static cache is the usual answer, but `DataBase.GetConnector` is public static and
   used by every store plus consumers directly (`WorkoutTracker/RepsMigrator`, Symbio's
   `SqlConnectionFactory`), so **this is a breaking change and needs its own blast-radius measurement**, not
   a drive-by.
3. **The cheapest high-value piece may be an enforcement test rather than a refactor.** A test asserting that
   `AbstractConnector`/`AbstractConnectorBase` expose no settable public/protected instance state beyond
   configuration would make instance four fail loudly at the point someone adds it. That is worth doing even
   if 1 and 2 are declined, and it is the part that actually prevents recurrence — "I didn't add mutable
   state" is construction, not evidence.

Prefer 3 first, then 1; treat 2 as its own task if it survives the measurement.

## Acceptance criteria

- [x] A decision recorded on each of the three questions, with reasons.
- [x] Whatever is chosen, **recurrence is prevented mechanically** — a test that fails when per-caller or
      per-operation state is added to a connector. A § Conventions entry alone does not satisfy this; three
      instances shipped while the relevant reasoning was already written down.
- [x] `RetryPolicy`'s settability resolved (removed, or documented as deliberately global with the reason).
- [x] If a DI seam is added, the blast radius of `DataBase.GetConnector` measured across all 16 consumer
      repos first, and the static path kept working or its removal staged.
- [x] Proven able to fail.

## Out of scope

- The three historical instances — all fixed (TASK-240, § Conventions' keyed-failures entry, TASK-259).
  This task is about the pattern that produced them.
- Whether schema-ensure belongs in a caller's unit of work — [[TASK-244]].
- Anything about `AmbientSqlTransaction`'s own semantics — TASK-240/242/243 settled those.

## Human test plan

- [x] N/A — mechanical; the proof is the enforcement test failing when mutable per-caller state is added, and
      the existing suites staying green.

---

## Step 0 — measured 2026-09-07, before a line changed

### (a) The audit in this file was three weeks stale, and one of its counts was wrong

| claim in this file | re-measured 2026-09-07 |
|---|---|
| `RetryPolicy` — *"0 assignments anywhere in the framework or in any of the 16 consumer repos"* | **2** — `RetryTests` and `RewrapClassificationTests`. Still **0** in production and in all 16 consumer repos, so the conclusion survives; the claim as written did not. |
| *"nothing is currently firing"* | **false** — see (b) |
| four connector events | **five** now (`OnSchemaEscapeDetected`, TASK-287) |
| `GetConnector` blast radius, unmeasured | **29** call sites in consumer repos, 19 in the framework, 80 in its tests |

Third time this session a stale count needed correcting before it could be used (§ TASK-283's rule).

### (b) ⚠ Instance FOUR was already in the code, and it was live

`IsInitializing` was `{ get; protected set; }` — a plain mutable flag on the process-wide connector,
guarding `DoInit`:

```csharp
if (!IsInitializing) { IsInitializing = true; OnInit?.Invoke(this); IsInitializing = false; }
```

Two defects, and the second is the one that matters:

1. The check-then-set is unsynchronised, so two flows can both enter.
2. **Worse: a second flow that sees the flag set has its initialisation SILENTLY DISCARDED** — not
   deferred, not retried. It returns and carries on believing init ran. That is precisely the
   "one caller's state silently changes another caller's outcome" shape of instances one to three.

And a third, found while fixing it: **a throwing `OnInit` handler left the flag stuck `true` forever**,
because the reset was a bare assignment rather than a `finally`. That permanently suppressed `DoInit`
for every caller of that database for the life of the process — the same "a subscriber can wedge the
framework" family as TASK-283 and TASK-289.

Latent today only because `OnInit` has **0** consumer subscribers (measured: the single consumer
subscription in all 16 repos is Symbio's `OnSchemaEscapeDetected`). Latent is not fixed.

## Decisions (criterion 1)

### Q3 first, as this file advised — a mechanical guard, because prose demonstrably is not enough

`ConnectorSharedStateTests`. Instances **two, three and four all shipped after** § Conventions already
said not to do this, so *"I didn't add mutable state"* is construction and this is the evidence. Two
rules plus three controls:

- **No settable public/protected instance property on a connector**, except a justified ledger.
- **No public/protected `Set*` method taking a `DbConnection`/`DbTransaction`** — instance three's exact
  shape, as a regression guard now that TASK-259 has deleted it.
- Plus a **behavioural** pair for instance four (one flow must not suppress another's init; a throwing
  handler must not wedge the connector) and a **scan control**, because the type filter could otherwise
  exclude everything and make the whole file vacuous in silence.

### Q1 — `IsInitializing` FIXED; `RetryPolicy` kept and justified in the ledger

`IsInitializing` is now an `AsyncLocal<bool>` **per instance**, exposed read-only, entered through a
scope that restores on exception. Flow-scoped *and* instance-scoped, so a handler re-entering this
connector still short-circuits — which is what the guard is for — while another flow, or another
connector in the same flow, is unaffected. Same mechanism TASK-240 introduced for the transaction.

**`RetryPolicy` stays settable, deliberately, and the obvious fix was measured and rejected.** Moving it
onto `Settings` looks like the clean answer and is not one: `Settings.GetId()` is
`Location:Name(:UserName:Port)` and carries neither it nor `CommandTimeout`, so two settings objects
differing only in retry policy **already share one connector and the first caller's value wins** — the
hazard TASK-276 pinned for `CommandTimeout`. Relocating it would hide the sharing rather than remove it.
One policy per database is the correct scope anyway, since the connector *is* per database. It is
therefore a ledger entry with that reasoning attached, which criterion 3 explicitly permits — and the
ledger has its own test so an entry that stops being needed must be deleted rather than quietly
covering something else.

### Q2 — DI seam DEFERRED, now with a number

**29 `GetConnector` call sites across the consumer repos** (Symbio's `SchemaEscapeReporter`,
WorkoutTracker's `IStoreFactory` / `RepsStorageBootstrapper` / `SqliteStoreFactory`, plus tests). That is
a breaking change needing its own blast-radius work and staged removal, exactly as this file said. Not
started, and deliberately not half-started.

## Verified

`BIRKO_REQUIRE_LIVE` set, against live PostgreSQL 16, MySQL 8.4, SQL Server 2022 and on-disk SQLite:
**1,479 tests, 0 failed** across seven suites — SQL 678 (+6), SQLite 341, PostgreSQL 107, MySQL 119,
MSSql 132, Migrations.SQL 87, Health.Data.SQL 16.

### Mutations — four, disjoint

| mutation | reds |
|---|---|
| add a settable `CurrentCallerTag` (instance five) | **1** — the shape rule |
| reintroduce `SetExternalTransaction(conn, tx)` (instance three) | **1** — the connection/transaction rule |
| revert `IsInitializing` to a shared settable flag | **3** — the shape rule *and both behavioural tests* |
| add a stale ledger entry | **1** — the ledger-currency test |

The third is the one worth noting: the behavioural tests fail against the *old* code, so they are
regression provers rather than pins — the old `DoInit` really did discard a concurrent flow's init.

## ⚠ Found during the sweep, and it identifies another open task

The MSSql suite failed 1 of 132, and this time the identity was captured with a trx logger:
`SchemaEnsureRollbackResidueLiveTests.A_write_to_a_missing_table_fails_instead_of_reporting_success`.
8/8 clean alone, ~1 in 5 in-suite. Mechanism: every class in that suite shares one cached connector, so
another class's deliberate schema escape bumps `SchemaGeneration`, this test's store re-initialises and
**re-creates the table it had just dropped**. Full evidence written to [[TASK-276]], which has been
waiting for exactly this. Not a product defect — TASK-288's healing is correct — and **not a fifth
ledger entry**, because `SchemaGeneration` *should* be shared: it is about the database, not about a
caller. What it demonstrates is the other half of this task's thesis, that even correctly-shared
connector state has cross-caller reach.

## Out of scope, restated

- The DI seam (Q2) — deferred with its measurement above.
- Fixing TASK-276's test isolation — recorded there with a proposed fix, not applied here.
- Provider connectors' own state — the enforcement tests see only the abstract types compiled into
  `Birko.Data.SQL.Tests`, which is where all four instances lived. Said on the test class rather than
  implied.
