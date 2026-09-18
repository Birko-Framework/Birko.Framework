---
id: STORY-042
parent: EPIC-014
status: planned
created: 2026-07-14
source: CODE-REVIEW-AUDIT-2026-06-17.md
theme: integration-test-tier
finding-count: 8
finding-ids: CR-M089, CR-M108, CR-M109, CR-M138, CR-M159, CR-M160, CR-M164, CR-M165
closed-offline: CR-M166 (via TASK-058 — SqLiteConnector DDL fix; no Docker needed after all)
---

# Integration-test tier — the Docker-gated remediation findings

## Why this is its own story

STORY-026 (medium findings) closed **267 / 275** entirely with **offline** tests — the Birko suites
are offline by design (fake `DbCommand`, `Birko.Data.InMemory`, lazy SDK clients, mock `IDatabase`,
loopback sockets). The remaining **8** findings (was 9 — CR-M166 closed offline via TASK-058) are not
"small leftovers": each requires observing
**real server behaviour** that no in-process fake reproduces — multi-node scroll paging, Flux
aggregation semantics, transactional rollback of a partial bulk write, cross-tenant document
filtering, `AUTOINCREMENT` DDL on a real engine, genuine-async deadlock behaviour.

Closing them is a **distinct effort**: stand up a Docker/Testcontainers integration tier, then work
four backend clusters. This story tracks that effort so the 9 findings are planned rather than
lingering as audit entries. It is a **theme** story (spans a few projects), not a severity partition
like STORY-024/025/026/027.

> ~~**Cannot be executed in the current environment** — no Docker. This story is the plan; execution
> requires a host with Docker (or a CI runner with the service containers).~~
>
> ⚠ **CORRECTED 2026-08-20 — that premise is stale, and had been for weeks.** The dev host has
> **Docker 29.7.2**, and containerised services have since been used routinely to close work in this
> epic. Proven runnable here, each having actually served a live suite: **PostgreSQL 16**, **MySQL 8.4**,
> **SQL Server 2022**, **TimescaleDB 2**, **MongoDB 7** (TASK-214/219), the **CosmosDB emulator**
> (TASK-223/224) and **RavenDB** (TASK-221/222). Of the five clusters below, four have had their backend
> exercised live already; only **InfluxDB 2.x** (cluster 2) and **Elasticsearch** (cluster 5) have not
> been started in this environment, and nothing suggests they cannot be.
>
> So this story is **no longer environment-blocked**. What it is blocked on is that nobody has picked it
> up — a different and more honest status. See the two notes below before planning the harness, because
> both change its shape.

## Prerequisite — TASK-042-00: integration-test harness (blocks everything)

Before any cluster, add the shared plumbing:
- A Testcontainers-based fixture pattern (xUnit `IAsyncLifetime` / collection fixtures) per backend.
- **Skippable when Docker is absent** — the offline unit suites (currently all green) must keep
  running clean on dev machines/CI without Docker; integration tests skip (not fail) when no daemon.
- A `docker-compose` (or per-fixture container spec) + CI wiring (a separate `integration` job).
- Decide the boundary: these `*.IntegrationTests` projects live alongside the existing offline
  `*.Tests` (do **not** fold live tests into the offline projects — keeps the fast suite fast).

**Acceptance:** one reference integration project (suggest MSSql, see cluster 1) runs green with Docker
up and **skips cleanly** with Docker down; CI has an opt-in integration job.

> ⚠ **CORRECTED 2026-08-20 — this prerequisite is PARTLY SUPERSEDED, and re-read it before building it.**
> While this story sat, the repo evolved its own answer to the same problem and it is now the de facto
> standard across the SQL family, MongoDB, CosmosDB and RavenDB: a suite reads `BIRKO_{PROV}_HOST` (+
> `_PORT` / `_USER` / `_PASSWORD` / `_DB`), returns early with a printed `SKIPPED:` line when it is unset,
> and turns that absence into a failure when `BIRKO_REQUIRE_LIVE` is set. That already satisfies the first
> half of the acceptance above — *green with Docker up, clean skip with Docker down* — without
> Testcontainers, and it is what the TASK-242/243/245/253/256/263 live suites use.
>
> Two things it does **not** do, and they are the real remaining work:
> - **No CI job.** No `azure-pipelines.yml` in the SQL family sets any `BIRKO_*_HOST`, so these suites run
>   only when a human starts containers and exports the variables by hand. The gated suites therefore pass
>   because someone chose to run them — which is precisely the failure mode this epic keeps rediscovering
>   (TASK-214: the finding was inside a suite that had **never** run). **This is now the highest-value part
>   of TASK-042-00**, and it is worth more than the fixture pattern.
> - **The boundary decision went the other way.** This story specifies separate `*.IntegrationTests`
>   projects, "do **not** fold live tests into the offline projects". In practice live tests were added
>   *inside* the existing `*.Tests` projects (`BulkTransactionBoundaryLiveTests`,
>   `UtcFieldInstantLiveTests`, `DeclaredIndexLiveTests`, …). That is a real divergence from the plan and
>   it has a cost — the "fast suite" now contains gated live tests — so either the plan or the practice
>   should change deliberately rather than by drift. **Decide it, don't inherit it.**

> **External consumer:** [[TASK-042]] (EPIC-016, cross-provider SQL store-factory + DI) is `review`-blocked
> solely on this harness — its live CRUD round-trip is currently env-gated (`BIRKO_{PROV}_TEST`) and should
> adopt the MSSql/Postgres fixture here once it exists. Coordinate cluster 1 with that task.

---

## Cluster 1 — SQL sync + MSSql bulk atomicity · MSSql/Postgres container
**Findings:** CR-M138 (deferred) · CR-M166 (✅ closed offline via TASK-058 — kept below for provenance)

- **CR-M166 — ✅ CLOSED OFFLINE (no Docker), 2026-07-14.** The blocker was a real connector bug, not a
  limitation to route around: [TASK-058](../../_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md)
  fixed `SqLiteConnector` DDL (AUTOINCREMENT scoped to `INTEGER PRIMARY KEY`; a non-PK
  `[IncrementField]` is now a plain column), so the dual-key `SqlSyncKnowledgeItem` creates and the
  full SQL sync-store CRUD round-trip runs on a real SQLite `.db`. `Birko.Data.Sync.Sql.Tests` 2 → 6.
  **No MSSql/Postgres container needed for M166.** (A live MSSql/Postgres would still add value for
  provider-specific behaviour, but it is no longer required to close the finding.)
  - **Spin-off → [TASK-058](../../_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md)
    (SqLiteConnector dual-key AUTOINCREMENT DDL).** This surfaced a genuine connector bug (SQLite
    allows `AUTOINCREMENT` only on `INTEGER PRIMARY KEY`; `FieldDefinition` emits it for any
    increment field, detached from PRIMARY KEY). **Offline-fixable** (not Docker-gated). If it lands
    with option (a) — plain `INTEGER` for a non-PK increment column — the M166 SQL-sync CRUD may then
    run on **SQLite offline**, potentially closing M166 here without a container. Track that outcome.
- **CR-M138** — MSSql native bulk (`BulkInsert/Update/Delete` + async) uses a **transactionless
  `SqlBulkCopy`**, so a mid-copy failure leaves partial rows (the base `RunCommandTransaction` rolls
  back). `InitException` semantics were confirmed contract-consistent last session; the residual is
  atomicity. Fix: wrap the six bulk paths in a transaction (rollback on failure). Only verifiable by
  triggering a mid-bulk failure against a live SQL Server.

**Acceptance:** SQL sync-store CRUD round-trips green against a real backend; a forced mid-bulk failure
leaves **zero** rows (rollback proven). One MSSql (or Postgres) fixture serves both.

> ⚠ **CORRECTED 2026-08-20 — CR-M138 needs RE-VERIFYING, not implementing.** The mechanism it names has
> changed since the finding was written, so picking this up as described would re-fix something already
> largely fixed. [[TASK-242]] rewired the bulk paths for the transaction boundary and, as a side effect,
> most of CR-M138's concern:
> - `BulkUpdate` / `BulkDelete` and their async twins now run through `RunBulk`, which owns a transaction
>   and calls `transaction.Rollback()` on failure when it owns it. **Four of the six paths now roll back.**
> - `BulkInsert` (sync + async) deliberately still runs with no enclosing transaction on the *standalone*
>   path, via `RunBulkOnConnection` — but it is atomic anyway: `SqlBulkCopy.BatchSize` is never set, so it
>   defaults to `0`, sending the whole copy as a **single batch**. Inside a caller's boundary it now enlists
>   through the `SqlBulkCopy(SqlConnection, SqlBulkCopyOptions, SqlTransaction)` overload, whose third
>   argument used to be `null` — which is what let the copy escape the boundary entirely.
>
> **Residual, and it is conditional rather than open:** if anyone ever sets `BatchSize > 0` — plausible for
> a large copy, and the idea exists in the family already (`MySqlSettings.BulkInsertBatchSize`) — the
> standalone `BulkInsert` regains exactly the partial-rows behaviour CR-M138 describes. So the honest task
> here is: force a mid-bulk failure against a live SQL Server, confirm zero rows for each of the six paths,
> and decide whether `BatchSize` should be settable at all given that it would reintroduce the defect.
> Verified by reading, not by running — the forced-failure test this story asks for still does not exist.

## Cluster 2 — InfluxDB migrations · InfluxDB 2.x container
**Findings:** CR-M108, CR-M109

- **CR-M108** — the whole `Birko.Data.Migrations.InfluxDB` surface is **fake-async**
  (`.GetAwaiter().GetResult()` over the async SDK; "async" methods `Task.FromResult`-wrap the sync
  ones). Rewrite to genuine `await`. *Structurally unblocked* — the `CancellationToken` migration
  interface refactor it depended on (CR-M101) already landed; but deadlock/thread behaviour is only
  verifiable against a live InfluxDB.
- **CR-M109** — the `count() |> group() |> sum()` Flux pipeline **over-counts multi-field
  measurements** (counts point-fields, not rows). Fix: restrict to one `_field` before `count()`, or
  document the semantics. Requires observing real InfluxDB output for a multi-field measurement.

**Acceptance:** migrations run over a live InfluxDB with no sync-over-async blocking; row-count on a
multi-field measurement returns the correct value. Both touch `InfluxDBDataMigrator` — do together.

## Cluster 3 — Cosmos sync store · Cosmos emulator (or Container fake)
**Findings:** CR-M159, CR-M160

- **CR-M159** — `AsyncCosmosSyncKnowledgeStore` / `CosmosSyncKnowledgeStore` don't implement the
  framework's `ISyncKnowledgeItemStore<T>` / `IAsyncSyncKnowledgeItemStore<T>`; they expose ad-hoc
  method shapes. Align to the **MongoDB sync store reference** (implements the interface, routes
  through the base `*Core` methods). Compiles offline, but shipping a public-signature change without
  running the sync provider against Cosmos is the risk — verify wiring end-to-end.
- **CR-M160** — no `.Tests` project. `ConvertToCosmosItem` branches are already unit-tested (CR-M158,
  done); the CRUD/query/set-sync-time paths need the emulator.

**Acceptance:** interface implemented + a sync round-trip (create → read → set-sync-time → delete)
green against the emulator; the sync provider drives the Cosmos store through the framework interface.

## Cluster 4 — RavenDB sync store · RavenDB test server
**Findings:** CR-M164, CR-M165

- **CR-M164** — same interface gap as Cosmos; align to the Mongo reference (delegate to base
  `ReadAsync`/`UpdateAsync`/`DeleteAsync` rather than hand-rolling sessions).
- **CR-M165** — no `.Tests` project. High value: the tests **lock in the already-fixed critical bugs**
  CR-C19 (tenant filter could never match) and CR-C20 (async delete used the wrong overload), plus the
  idempotent-upsert case (same EntityGuid+Scope twice → one doc). Those regressions are only
  observable against a live RavenDB (tenant-scoped query, dedup).

**Acceptance:** interface implemented; CRUD + `SetLastSyncTime` round-trips green; tenant scoping
filters correctly and upsert is idempotent (regression lock on CR-C19/C20).

## Cluster 5 — ElasticSearch store (largest) · Elasticsearch container
**Findings:** CR-M089

The highest-risk single item. `ElasticSearchStore` / `AsyncElasticSearchStore` have **zero tests** for:
- `ReadStreamAsync` scroll + offset + limit paging across the **`max_result_window` boundary**
  (intricate skip/end bookkeeping — classic off-by-one),
- `BulkAsync` size-limit enforcement + filter-based update/delete,
- `AggregateAsync` grouped / time-bucket parsing,
- `GetIndexName` sanitization,
- and it would catch the referenced `ReadAsync(ct)` truncation bug.

Nest's `ElasticClient` is awkward to fake meaningfully, so this realistically needs an Elasticsearch
Testcontainer with a real index seeded past `max_result_window`. Likely multi-day on its own.

**Acceptance:** scroll paging returns the correct window across the boundary with an offset; bulk
size-limit enforced; aggregation parsed correctly against a live index.

---

## Sequencing

| Order | Cluster | Fixture | Effort | Findings |
|---|---|---|---|---|
| 0 | Harness (TASK-042-00) | — | M | prerequisite |
| 1 | MSSql bulk atomicity | MSSql/Postgres | S–M | M138 (M166 already closed offline via TASK-058) |
| 2 | InfluxDB migrations | InfluxDB 2.x | M | M108, M109 |
| 3 | Cosmos sync | Cosmos emulator | M | M159, M160 |
| 4 | RavenDB sync | RavenDB test server | M | M164, M165 |
| 5 | ElasticSearch store | Elasticsearch | L | M089 |

After the harness, each cluster is independent — they can be picked up in any order or in parallel by
whoever has the relevant container. Per cluster: implement the fix (where there is one) **and** its
integration tests together, so the refactor is verified by the same round-trip.

## Out of scope / notes
- This story does **not** re-verify critical/high findings; but the RavenDB sync tests (cluster 4)
  incidentally lock in CR-C19/CR-C20, and the ES store tests (cluster 5) the referenced ReadAsync
  truncation fix — a welcome side effect.
- Keep the offline `*.Tests` suites untouched and fast; live tests go in separate `*.IntegrationTests`
  projects gated on Docker availability.
- Update `CODE-REVIEW-AUDIT-2026-06-17.md` status lines (`open`/`deferred` → `done`) as each finding
  closes, same as STORY-024/025/026.
