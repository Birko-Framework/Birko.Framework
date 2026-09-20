---
id: TASK-480
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-20
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-010]
pr: null
github-issue: null
jira-key: null
---

# CosmosDB live tests have never run in CI — there is no Cosmos job

## Context

The 2026-09-20 gate audit compared every `BIRKO_*` name read under `tests/` against the 16 the
workflows set. `BIRKO_COSMOS_CONNECTION` and `BIRKO_COSMOS_CONNECTION_MODE` are read by
`tests/Birko.Data.CosmosDB.Tests/CosmosFilterMatrixLiveTests.cs` and `CosmosSpanContainsTests.cs`,
and **set by no workflow**. Grepping `.github/workflows/` for "cosmos" returns nothing: unlike
PostgreSQL, MySQL, SQL Server, TimescaleDB, MongoDB, RavenDB, Redis and InfluxDB, Cosmos has **no
job at all**.

The gates then return silently, so the suite reports **Passed** having touched no server. The code
says so itself, in two places:

- `CosmosFilterMatrixLiveTests.cs:26` — *"Gated on `BIRKO_COSMOS_CONNECTION`; no-op pass when absent
  so CI stays green."*
- `CosmosSpanContainsTests.cs:24` — a defect went unnoticed because the suite *"had never run, which
  is exactly how this went unnoticed"*.

**This is the one finding from that audit that is costing coverage now rather than risking it later.**
[[TASK-479]] covers 19 gates that do run today and would only go wrongly-green if a container died;
this suite has never run in CI at any point.

The emulator is the open question and the reason this is a task rather than a one-line fix. The
`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator` image is heavy, slow to become ready, and
self-signed — `BIRKO_COSMOS_CONNECTION_MODE=Gateway` already exists in the test code for exactly that
reason (`CosmosFilterMatrixLiveTests.cs:114` — *"what makes the Docker emulator reachable"*), which
suggests someone got it working locally once. Whether it is reliable enough for CI is what this task
has to settle, and **"it is not" is a legitimate outcome** — recorded, with the suite's gate promoted
so the situation stops being invisible.

Follow `live-tests.yml`'s readiness discipline, which was earned twice already: *wait on a real
query, never a readiness probe* — the header records `pg_isready` answering during `initdb` and
turning a whole TimescaleDB suite red with 15 of 17 failures being pure fixture noise.

## Acceptance criteria

- [ ] A decision is recorded either way: a Cosmos job exists in `live-tests.yml`, **or** the task
      records the measurement that says the emulator is not viable in CI and what would change that
- [ ] If a job is added: it waits on a **real query**, not a readiness probe, and sets
      `BIRKO_COSMOS_CONNECTION` (+ `_CONNECTION_MODE`) for `tests/Birko.Data.CosmosDB.Tests`
- [ ] Both gated files honour `BIRKO_REQUIRE_LIVE` — which, once a job exists, is what stops this
      regressing to "silently green" the moment the emulator flakes
- [ ] **The suite is proven to have actually run**, from the servers' own state or a duration that
      cannot be explained by a no-op — not from a green tick. The tick is what has been wrong here
      all along
- [ ] If no job is viable: the suite's gate still reports its skip visibly, and `CLAUDE.md` or the
      test project's own `CLAUDE.md` records Cosmos as a **declared, measured gap** rather than an
      unremarked one
- [ ] `CosmosFilterMatrixLiveTests.cs:26`'s *"no-op pass when absent so CI stays green"* comment is
      corrected — whichever way the decision goes, that sentence stops being the design

## Out of scope

- The 19 non-Cosmos silent-return gates and the PostgreSQL View `SkippableFact` trio — [[TASK-479]].
- Writing new Cosmos assertions. If the emulator comes up and the existing suite fails, that is a
  real product finding: **spawn it, do not fix it here**, or this task becomes unbounded. The two
  files have never run, so some failure is a plausible outcome rather than a surprise.
- A paid/real Azure Cosmos account in CI. The emulator or nothing.

## Human test plan

- [ ] Start the emulator locally, export `BIRKO_COSMOS_CONNECTION` (+ `_CONNECTION_MODE=Gateway`),
      run `dotnet test tests/Birko.Data.CosmosDB.Tests`, and record what happens — pass, fail, or
      the emulator never becoming usable. Expected: unknown, and finding out is the task.
- [ ] Time it. If it is viable, the CI cost is part of the decision to add the job.

## Implementation plan

_Populated by `/tasks plan TASK-480` — leave empty until then._
