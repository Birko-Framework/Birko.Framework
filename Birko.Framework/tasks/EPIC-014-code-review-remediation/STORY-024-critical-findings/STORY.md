---
id: STORY-024
parent: EPIC-014
status: done
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: critical
finding-count: 24
finding-ids: CR-C01 … CR-C24
---

# Critical findings

## User story

As a maintainer, I want every **critical** code-review finding fixed (or explicitly waived) so the
framework has no known correctness-breaking defects shipping.

## Scope

The 24 critical findings `CR-C01 … CR-C24` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). All were
adversarially re-verified (a second agent re-opened the cited file), so these are high-confidence.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Cxx` entry, copying its ID/Title → title, Path → file:line, Detail → context, Fix → approach,
Acceptance → derive + add a regression test. Flip each finding's `Status` in the audit as it lands.

## ✅ All 24 critical findings resolved (2026-07-06)

Every `CR-Cxx` is fixed (or verified already-fixed for CR-C23) with a regression/compile-guard test
and its audit `Status` flipped to `done`. Ten new `.Tests` sibling projects were created along the way
(EventSourcing, Migrations.RavenDB, Sync.RavenDB, AI.Providers, Communication.Hardware,
SQL.ViewModel, SQL.Caching, Migrations.MongoDB, BackgroundJobs.Redis) plus tests added to existing
JSON/Sync/XML/BCrypt/Migrations.SQL/CosmosDB suites. Bonus fixes: XML `SetSettings` stack-overflow
and Mongo index-builder reachability (CR-H062). **Behavioral-test infra gaps** (need a live
server/emulator, tracked for a future infra-enabled pass): CR-C02 (Redis Lua path), CR-C04 (Cosmos
emulator), CR-C09 (Mongo replica-set transactions), CR-C12/C15 (RavenDB/Mongo query behavior),
CR-C16 (SQL end-to-end). **Residuals noted in the audit:** CR-C09 store-record write not yet in the
session; CR-C13 RavenDB CopyData is fail-fast pending a real cross-collection implementation; CR-C19
tenant travels on the item (write-path populate relies on TenantSyncProvider).

## Progress (worked project-by-project, testable-in-session batches first)

- [x] **CR-C07** · Birko.Data.JSON · `JsonStore.SaveData` writes `_items.Values` (array) — round-trip test
- [x] **CR-C08** · Birko.Data.JSON · `JsonSeparateStore.SaveData` guard inverted + path registered — write test
- [x] **CR-C01** · Birko.AI.Providers · Ollama streaming posts to `/api/chat` (+ `num_predict`) — new `.Tests` (fake handler)
- [x] **CR-C02** · Birko.BackgroundJobs.Redis · time-dominant score + bounded priority tiebreaker; threshold on same scale — new `.Tests` (Lua path = Redis infra gap)
- [x] **CR-C03** · Birko.Communication.Hardware · `ResolvePortAddress` maps LPT#→base I/O address — new `.Tests`
- [x] **CR-C04** · Birko.Data.CosmosDB · `CosmosGuidIdSerializer` injects `id` == Guid (Cosmos-local, no shared-model change) — serializer unit tests (point ops = emulator gap)
- [x] **CR-C05** · Birko.Data.EventSourcing · single Create Guid linkage — new `.Tests` project + test
- [x] **CR-C06** · Birko.Data.EventSourcing · bulk Create Guid linkage (sync + async) — tests
- [x] **CR-C09** · Birko.Data.Migrations.MongoDB · session threaded through context/migrator/schema-builder → ops join the txn (+ bonus CR-H062) — compile-guard `.Tests` (behavioral = replica-set gap; store-record residual)
- [x] **CR-C10** · Birko.Data.Migrations.RavenDB · `using System.Linq;` added — new `.Tests` compile guard
- [x] **CR-C11** · Birko.Data.Migrations.RavenDB · `using System.Linq;` added — compile guard
- [x] **CR-C12** · Birko.Data.Migrations.RavenDB · CountDocuments executes the built query (behavioral test = infra gap)
- [x] **CR-C13** · Birko.Data.Migrations.RavenDB · CopyData fail-fast `NotSupportedException` — test (full copy = follow-up)
- [x] **CR-C14** · Birko.Data.Migrations.SQL · terminal `Build()` (default iface method) → SQL emits CREATE TABLE/INDEX — SQLite-backed tests
- [x] **CR-C15** · Birko.Data.MongoDB.Views · query-only stages on the view; base prepended only on-the-fly (behavioral test = live Mongo infra gap)
- [x] **CR-C16** · Birko.Data.SQL.Caching · filter Update/Delete now invalidate cache — new `.Tests` (key-invariant + mechanism; e2e = infra gap)
- [x] **CR-C17** · Birko.Data.SQL.ViewModel · repo generic over TConnector (mirrors sync) — new `.Tests` + BardStudio consumer updated
- [x] **CR-C18** · Birko.Data.Sync · knowledge upserted (create-new/update-existing split), sync + async — 2 regression tests
- [x] **CR-C19** · Birko.Data.Sync.RavenDB · model implements canonical `ITenant`; `ConvertToRavenItem` copies tenant → end-to-end tenant scoping (reworked to reuse tenant infra) — new `.Tests`
- [x] **CR-C20** · Birko.Data.Sync.RavenDB · async delete uses tracked entity — compile-guarded
- [x] **CR-C21** · Birko.Data.XML · real batched List<T> persistence (sync+async, store+bulk) + fixed a pre-existing SetSettings stack-overflow — 3 tests
- [x] **CR-C22** · Birko.Data.XML · separate stores override bulk Core to write per-file (sync+async) — test
- [x] **CR-C23** · Birko.Security.AspNetCore · already resolved by the 2026-06-25 delimiter fix (both readers split `[',',';']`); verified + tested
- [x] **CR-C24** · Birko.Security.BCrypt · corrected EksBlowfish + encipher to canonical bcrypt; validated vs published `$2a$` reference vectors (also closes CR-H138); README notes the NuGet option. Confirmed unused by consumers.

**Test-infra gaps noted while working** (true regression tests need infra / a missing `.Tests` sibling):
CR-C10–C13 (RavenDB migrations — no `.Tests`, needs a Raven server), CR-C04 (Cosmos emulator),
CR-C09 (Mongo replica set), CR-C02 (Redis), CR-C15/C16/C17/C19/C20 (no `.Tests` sibling).
