---
id: TASK-480
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
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
workflows set. `BIRKO_COSMOS_CONNECTION` and `BIRKO_COSMOS_CONNECTION_MODE` are **set by no workflow**. Grepping `.github/workflows/` for "cosmos" returns nothing: unlike
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

- [x] A decision is recorded either way: a Cosmos job exists in `live-tests.yml`, **or** the task
      records the measurement that says the emulator is not viable in CI and what would change that
- [x] If a job is added: it waits on a **real query**, not a readiness probe, and sets
      `BIRKO_COSMOS_CONNECTION` (+ `_CONNECTION_MODE`) for `tests/Birko.Data.CosmosDB.Tests`
- [x] The gated file honours `BIRKO_REQUIRE_LIVE` — which, once a job exists, is what stops this
      regressing to "silently green" the moment the emulator flakes
- [x] **The suite is proven to have actually run**, from the servers' own state or a duration that
      cannot be explained by a no-op — not from a green tick. The tick is what has been wrong here
      all along
- [x] ~~If no job is viable~~ — n/a, a job is viable: the suite's gate still reports its skip visibly, and `CLAUDE.md` or the
      test project's own `CLAUDE.md` records Cosmos as a **declared, measured gap** rather than an
      unremarked one
- [x] `CosmosFilterMatrixLiveTests.cs:26`'s *"no-op pass when absent so CI stays green"* comment is
      corrected — whichever way the decision goes, that sentence stops being the design

## Out of scope

- The 19 non-Cosmos silent-return gates and the PostgreSQL View `SkippableFact` trio — [[TASK-479]].
- Writing new Cosmos assertions. If the emulator comes up and the existing suite fails, that is a
  real product finding: **spawn it, do not fix it here**, or this task becomes unbounded. The two
  files have never run, so some failure is a plausible outcome rather than a surprise.
- A paid/real Azure Cosmos account in CI. The emulator or nothing.

## Human test plan

- [x] Start the emulator locally, export `BIRKO_COSMOS_CONNECTION` (+ `_CONNECTION_MODE=Gateway`),
      run `dotnet test tests/Birko.Data.CosmosDB.Tests`, and record what happens — pass, fail, or
      the emulator never becoming usable. Expected: unknown, and finding out is the task.
- [x] Time it. If it is viable, the CI cost is part of the decision to add the job.

## Implementation plan

_Populated by `/tasks plan TASK-480` — leave empty until then._

---

## ⚠ Correction, 2026-09-20 — the scope is ONE test, not two files

Checked at pick time, before touching anything. This task's Context said the two env vars are *"read
by `CosmosFilterMatrixLiveTests.cs` and `CosmosSpanContainsTests.cs`"*. **Only the first reads them.**
`CosmosSpanContainsTests` names `BIRKO_COSMOS_CONNECTION` once, in a doc comment, to explain that the
suite which *would* have caught its defect had never run — it is itself an **offline** suite: a
throwaway endpoint, the public emulator key, and a `NoNetworkHandler` that guarantees no connection.
Its 4 facts run everywhere and always have.

So the real scope is **one `[Fact]`** — `FilterShapes_MatchCompiledDelegateOracle` — which has never
run in CI. That is smaller than this task claimed and **not less important**, because it is a *matrix*:
**27 filter shapes**, each compared against a compiled-delegate oracle, in the one provider with no
hand-rolled parser (the raw `Expression` goes straight to `GetItemLinqQueryable().Where(filter)`). One
test id, 27 translation behaviours, zero coverage.

Recorded rather than silently narrowed: the miscount came from grepping which *files mention* the
variable instead of which *files read* it, which is the same shape of error as counting a doc comment
as a gate. The `## Out of scope` reference in [[TASK-479]] to "two gated files" is likewise off by one.

---

## Outcome — 2026-09-20: a job exists, and the documented image is the one that does not work

**The emulator is viable — but not the one the task assumed.** Both were measured on this machine
against the real suite, which is the only reason the answer is the second row:

| image | size | outcome |
|---|---|---|
| `:latest` (the documented Linux emulator) | **4.94 GB** | `ReadAccountAsync` OK in **0.6 s**; `CreateDatabaseIfNotExistsAsync` **hung past 60 s**, every time |
| `:vnext-preview` + `PROTOCOL=http` | **2.31 GB** | ready ~15 s, **61/61 in 3 s** |

### ⚠ The `:latest` failure is this repo's own readiness rule, arriving from a new direction

`live-tests.yml`'s header says *wait on a real query, never a readiness probe*, and records
`pg_isready` answering during `initdb`. Cosmos produced a **sharper** version: the emulator logged
`Started`, reported all 4 partitions up, answered `/` with a correct `401` in 0.09 s, and returned
account properties through the SDK in 0.6 s — **while being unable to create a database at all.**
A probe cannot see that, and neither can a read. **"The endpoint answers" and "the emulator works" are
different claims, and here they had different truth values.** Recorded on the job, because the next
person to debug a Cosmos hang needs it.

Two smaller measurements from the same session, also written onto the job:

- **`PROTOCOL=http` removes the certificate problem rather than solving it.** `:latest` failed with
  `AuthenticationException: UntrustedRoot`, and `Settings.GetCosmosClientOptions()` exposes no
  `HttpClientFactory` and no validation callback to work around it. That is **correct** and was left
  alone: a product API must not grow a trust-anything switch so a test can pass. Installing the pem
  into the runner's trust store was the fallback; HTTP made it unnecessary.
- **A probe with TLS validation disabled was used only to isolate the cert**, and is not in the repo.
  It is what proved reads worked and writes did not, which is what sent this to `vnext-preview`
  instead of to a day of certificate debugging.

### What the 27 shapes actually said

**All 27 matched the compiled-delegate oracle. No product defect surfaced.** Worth stating plainly,
because the pessimistic reading was live in this task's `## Out of scope` — *"some failure is a
plausible outcome rather than a surprise"* — and it did not happen. The coverage was missing; the
behaviour underneath it was sound.

### Proven it ran, three ways

| state | result |
|---|---|
| no connection, no `BIRKO_REQUIRE_LIVE` | 61 passed — the developer-without-Docker path |
| no connection, `BIRKO_REQUIRE_LIVE=1` | **1 failed** / 60 passed — the gate refuses |
| connection + `BIRKO_REQUIRE_LIVE=1` | 61 passed in 3 s |

And the measurement that matters most, on the single matrix test: **1 s with a connection, 28 ms
without — both reporting `Passed`.** That 35× gap is the whole defect in one line. From the outside
the two runs were indistinguishable, which is exactly how this stayed invisible.