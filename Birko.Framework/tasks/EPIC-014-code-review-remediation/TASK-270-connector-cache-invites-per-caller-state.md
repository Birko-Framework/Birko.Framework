---
id: TASK-270
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-204, TASK-240, TASK-259]
findings: []
pr: null
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

- [ ] A decision recorded on each of the three questions, with reasons.
- [ ] Whatever is chosen, **recurrence is prevented mechanically** — a test that fails when per-caller or
      per-operation state is added to a connector. A § Conventions entry alone does not satisfy this; three
      instances shipped while the relevant reasoning was already written down.
- [ ] `RetryPolicy`'s settability resolved (removed, or documented as deliberately global with the reason).
- [ ] If a DI seam is added, the blast radius of `DataBase.GetConnector` measured across all 16 consumer
      repos first, and the static path kept working or its removal staged.
- [ ] Proven able to fail.

## Out of scope

- The three historical instances — all fixed (TASK-240, § Conventions' keyed-failures entry, TASK-259).
  This task is about the pattern that produced them.
- Whether schema-ensure belongs in a caller's unit of work — [[TASK-244]].
- Anything about `AmbientSqlTransaction`'s own semantics — TASK-240/242/243 settled those.

## Human test plan

- [ ] N/A — mechanical; the proof is the enforcement test failing when mutable per-caller state is added, and
      the existing suites staying green.
