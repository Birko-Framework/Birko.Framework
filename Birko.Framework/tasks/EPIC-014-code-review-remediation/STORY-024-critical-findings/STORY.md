---
id: STORY-024
parent: EPIC-014
status: in-progress
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

## Progress (worked project-by-project, testable-in-session batches first)

- [x] **CR-C07** · Birko.Data.JSON · `JsonStore.SaveData` writes `_items.Values` (array) — round-trip test
- [x] **CR-C08** · Birko.Data.JSON · `JsonSeparateStore.SaveData` guard inverted + path registered — write test
- [x] **CR-C01** · Birko.AI.Providers · Ollama streaming posts to `/api/chat` (+ `num_predict`) — new `.Tests` (fake handler)
- [ ] CR-C02 · Birko.BackgroundJobs.Redis · scheduled-job score scale
- [x] **CR-C03** · Birko.Communication.Hardware · `ResolvePortAddress` maps LPT#→base I/O address — new `.Tests`
- [ ] CR-C04 · Birko.Data.CosmosDB · missing `id` property
- [x] **CR-C05** · Birko.Data.EventSourcing · single Create Guid linkage — new `.Tests` project + test
- [x] **CR-C06** · Birko.Data.EventSourcing · bulk Create Guid linkage (sync + async) — tests
- [ ] CR-C09 · Birko.Data.Migrations.MongoDB · transaction wraps no ops
- [x] **CR-C10** · Birko.Data.Migrations.RavenDB · `using System.Linq;` added — new `.Tests` compile guard
- [x] **CR-C11** · Birko.Data.Migrations.RavenDB · `using System.Linq;` added — compile guard
- [x] **CR-C12** · Birko.Data.Migrations.RavenDB · CountDocuments executes the built query (behavioral test = infra gap)
- [x] **CR-C13** · Birko.Data.Migrations.RavenDB · CopyData fail-fast `NotSupportedException` — test (full copy = follow-up)
- [ ] CR-C14 · Birko.Data.Migrations.SQL · schema builder never emits CREATE
- [ ] CR-C15 · Birko.Data.MongoDB.Views · persistent view re-runs base pipeline
- [ ] CR-C16 · Birko.Data.SQL.Caching · filter Update/Delete bypass invalidation
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
