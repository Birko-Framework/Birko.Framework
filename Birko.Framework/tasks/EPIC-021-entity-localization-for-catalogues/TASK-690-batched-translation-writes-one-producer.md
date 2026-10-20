---
id: TASK-690
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-20
depends-on: [TASK-679, TASK-680, TASK-681]
blocks: [TASK-682]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Translation writes batched, one producer shared by the wrappers

## Context

Spawned from [[TASK-682]] at plan time, 2026-10-20 (split decided by the user). `IEntityTranslations.SetAsync`,
`RemoveCultureAsync` and `RemoveEntitiesAsync` need batched writes, and TASK-682's AC 3 forbids a second copy of
the code. Today the four store wrappers write translations through `LocalizedTranslationRows.Save` / `SaveAsync`, at
one read and one write **per field**. Deletes by a list or a filter loop per entity, and Localize and LocalizeAll have
separate loops around `ReadMany` + `Apply`. This task changes that already-shipped wrapper behaviour, so it gets its own
mutation proofs before the API is built on top of it.

## Acceptance criteria

- [x] `LocalizedTranslationRows` (sync + async) saves one entity-language in **1 read + at most 3 bulk calls**
      (delete for null values, update, create). A bulk call is skipped when its list is empty.
- [x] A bulk create refused by the unique key (`StoreUniqueViolationException`) is re-read once: rows that now exist are
      updated and the rest are created. A second refusal or a failed re-read goes through the existing `Unretryable`
      path (PostgreSQL's 25P02 message kept).
- [x] `DeleteMany(guids, entityType)` and `DeleteCulture(guid, entityType, culture)` are filter deletes chunked by 500.
      An empty id list makes no store call (rules 2, 3, 5).
- [x] One shared `LocalizeAll(Async)` (`ReadMany` + `Apply`), plus `ReadAllCultures(Async)` (one read, culture → field →
      value) and a reusable `OnlyFields`, ready for TASK-682.
- [x] All four wrappers use these: `SaveTranslations` → batched save, `Delete(IEnumerable)` / `Delete(filter)` →
      `DeleteMany`, Localize / LocalizeAll → the shared helper. No per-field or per-entity loops are left.
- [x] Tests:
  - [x] Write counts are pinned on the InMemory test stores, which get create/update/delete counters.
  - [x] A racing writer leaves one row, in unit tests and in the live SQL `TranslationTableTests`.
  - [x] Mutations each turn a test red: per-field save restored, retry removed, empty-ids guard removed, per-entity
        delete or localize loop restored.
  - [x] Unit and SQL live suites stay green.

## Out of scope

- The `IEntityTranslations` API, DI and README (TASK-682).
- Making the entity write and the translation write share one boundary (TASK-683).
- MySQL's unique key (TASK-684).

## Human test plan

N/A — asserted by tests (write counts and live SQL suites; no user-visible surface).

## Outcome (2026-10-20)

All criteria met; measured, not inferred:

- **Write counts** (InMemory test stores, now counting calls): a save of one new, one changed and one removed field is
  1 read + 1 create + 1 update + 1 delete; a collection create of 3 entities × 2 fields is 1 read + 1 create (it was
  6 + 6); 501 entities take 2 reads; a filter or collection delete is 1 filter delete per 500 entities.
- **Order:** creates are sent first, then updates, then deletes. A create that cannot be retried leaves nothing applied.
  Updated rows get their new value only when the update is sent, because an in-memory store hands out its live rows.
  Both came from the close review.
- **Retry:** a refused batch re-reads its keys once. Another writer's rows are updated and the rest re-created. No held
  key, or a second refusal, rethrows the original (`Unretryable`). Live: a racing writer on one key of a two-key batch
  leaves one row per key on SQLite, PostgreSQL 16 and SQL Server 2022. MySQL skips (no key, TASK-684).
- **Filter-based update** (sync and async now take one path): the matched entities are read, then their translations in
  one read. The action runs on a detached copy, each entity is written, and changed translations are saved in one batch.
  The sync wrapper used to read translations per entity inside the inner store's callback.
  - **Partial failure, decided by the user:** the entities already written keep their translation changes, saved best
    effort, and the original exception always surfaces. Before, async kept them and sync lost them.
- **Empty-ids guard:** structural. `Chunk` over an empty list yields nothing, so there is no line to mutate. "Deleting no
  entities makes no call" pins it.
- **Mutations** (17, each red): per-row updates/deletes, per-row creates (async), no retry (sync, async; live: 7 red),
  second refusal swallowed, deletes before the create, values set while planning, per-entity delete (sync, async; live
  4), per-entity save (sync, async), no save after a part failure (sync, async), save failure masking the original,
  per-entity translation read (sync, async), per-entity localize.
- **Tests:** unit 238/238 (LocalizationBatchedWriteTests 33); SQL 56/56 live (SQLite, PostgreSQL 16, SQL Server 2022,
  MySQL 8.4), +4 per provider. The framework solution builds with 0 errors and no new warnings. The importers Sandbox
  and Symbio.Birko build.
- **Removed as unreachable** (rule 53): `LocalizedTranslationRows.Read` / `ReadAsync`, the per-entity translation reads.
- **Close review** (code-review, 8 findings):
  - Fixed: the async save in `finally` could mask the error; sync and async differed on partial failure; the sync
    pre-read doubled the entity read; the race-only fallback branch; a second refusal not reported as the original;
    deletes and updates sent before the create; values captured on the default culture.
  - Not fixed: the planning read fetches every field of the entities, not only the written ones. The wrappers write all
    fields, and a field filter would add an `IN` list per read.

## Implementation plan

From TASK-682's plan, steps 1–3 (Plan agent, 2026-10-20):

1. In `LocalizedTranslationRows` (sync + async), make one `Where(guid, type, culture)` read, then partition the fields
   into delete, update and create. Send `DeleteAsync(list)`, `UpdateAsync(list)` and `CreateAsync(list)`, skipping
   empty ones. On a unique refusal, re-read every key once and update or create from that; anything else goes to
   `Unretryable`. The SQL bulk create is atomic (`AtomicallyAsync`). The re-read also covers stores that insert part
   of a batch.
2. Add `DeleteMany` and `DeleteCulture` as chunked filter deletes, with the empty-ids guard. Extract `LocalizeAll`,
   `ReadAllCultures` and `OnlyFields`.
3. Switch the four wrappers (`LocalizedStoreWrapper`, `AsyncLocalizedStoreWrapper`, `LocalizedBulkStoreWrapper`,
   `AsyncLocalizedBulkStoreWrapper`) to the shared code.
4. Tests:
   - Add counters to `TestResources/InMemoryAsyncBulkStore.cs` and its sync twin.
   - Adjust existing tests that count writes or rely on the race hook firing after the first read.
   - Add a counting delegating store in `TranslationTableTests`, modelled on `RacingStore`.
5. Docs: the project README and CLAUDE.md (write cost), the spec diff at close, and Recent Updates.

Risk: a refused bulk create inside a caller's PostgreSQL transaction aborts it, as the single create does today.
This is reported, not fixed.
