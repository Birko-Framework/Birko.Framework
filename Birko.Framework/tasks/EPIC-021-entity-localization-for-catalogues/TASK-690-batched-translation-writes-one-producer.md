---
id: TASK-690
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `LocalizedTranslationRows` (sync + async) saves one entity-language in **1 read + at most 3 bulk calls**
      (delete for null values, update, create). A bulk call is skipped when its list is empty.
- [ ] A bulk create refused by the unique key (`StoreUniqueViolationException`) is re-read once: rows that now exist are
      updated and the rest are created. A second refusal or a failed re-read goes through the existing `Unretryable`
      path (PostgreSQL's 25P02 message kept).
- [ ] `DeleteMany(guids, entityType)` and `DeleteCulture(guid, entityType, culture)` are filter deletes chunked by 500.
      An empty id list makes no store call (rules 2, 3, 5).
- [ ] One shared `LocalizeAll(Async)` (`ReadMany` + `Apply`), plus `ReadAllCultures(Async)` (one read, culture → field →
      value) and a reusable `OnlyFields`, ready for TASK-682.
- [ ] All four wrappers use these: `SaveTranslations` → batched save, `Delete(IEnumerable)` / `Delete(filter)` →
      `DeleteMany`, Localize / LocalizeAll → the shared helper. No per-field or per-entity loops are left.
- [ ] Tests:
  - [ ] Write counts are pinned on the InMemory test stores, which get create/update/delete counters.
  - [ ] A racing writer leaves one row, in unit tests and in the live SQL `TranslationTableTests`.
  - [ ] Mutations each turn a test red: per-field save restored, retry removed, empty-ids guard removed, per-entity
        delete or localize loop restored.
  - [ ] Unit and SQL live suites stay green.

## Out of scope

- The `IEntityTranslations` API, DI and README (TASK-682).
- Making the entity write and the translation write share one boundary (TASK-683).
- MySQL's unique key (TASK-684).

## Human test plan

N/A — asserted by tests (write counts and live SQL suites; no user-visible surface).

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
