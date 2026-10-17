---
id: TASK-646
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Async repository UpdateAsync and DeleteAsync with no store still return silently

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `repository-contract`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A gap in [[TASK-162]]'s own fix (SH-M219): `AbstractAsyncRepository.UpdateAsync` / `DeleteAsync` still
`if (Store == null) return;` (`Birko.Data.Repositories/AbstractAsyncRepository.cs:70,81`), contradicting its doc ("every
operation now refuses") and the sync twin, which throws. Also in this area: `AddRepository<TRepository>(store, lifetime =
Scoped)` keeps the default Scoped but accepts only Singleton since TASK-162, so calling it without a lifetime always
throws (`ServiceCollectionExtensions.cs:52,58`); and sync bulk ViewModel `Delete(IEnumerable)` calls no `RemoveHash`,
unlike the async twin (`AbstractBulkViewModelRepository.cs:146-155`).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Both async methods throw with no store
- [x] AddRepository's default lifetime is one it accepts
- [x] The sync bulk delete prunes hashes
- [x] Red on the current code; the spec `docs/specs/repository-contract.md` updated if behaviour changes

## Resolution (2026-10-17)

**All three reproduced, red first:**
- `UpdateAsync` and `DeleteAsync` on an `AbstractAsyncRepository` (and on the bulk subclass, which inherits them)
  with no store returned without throwing.
- `AddRepository<Repo>(store)` with no lifetime threw `ArgumentException` on its own default.
- After a sync bulk ViewModel `Delete(IEnumerable)`, 2 of 2 deleted models were still tracked.

**Fixes:**
- Both async methods now `throw NoStore()`, as every sibling and the sync twin do.
- The pre-built-store overload defaults to `ServiceLifetime.Singleton`, the only lifetime it accepts. A call without
  a lifetime always threw before, so no working caller changes behaviour.
- The sync bulk delete materialises the models once, deletes them, and calls `RemoveHash` on each, as the async
  twin does.

Tests: `Task646RepositoryGapsTests` (Repositories, 2) and
`ViewModelRepositoryTriageTests.Deleting_a_batch_of_view_models_stops_tracking_them`.

**Docs:** the spec `repository-contract` (the no-store requirement, the DI requirement and its scenario, the hash
scenario) and Recent Updates (the oldest entry rolled).

**Regression:** the 92 suites that import `Birko.Data.Repositories` or `Birko.Data.ViewModel` (5,385 tests) are
green. They ran live on local Docker against PostgreSQL, MySQL, SQL Server, TimescaleDB, MongoDB, RavenDB,
ElasticSearch, Cosmos DB and InfluxDB; there was no Redis, so its gated tests returned early.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-646` — leave empty until then._
