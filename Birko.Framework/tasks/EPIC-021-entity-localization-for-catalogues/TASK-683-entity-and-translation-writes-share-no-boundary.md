---
id: TASK-683
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-19
depends-on: [TASK-680]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-M083]
pr: null
github-issue: null
jira-key: null
---

# The entity write and its translation write share no boundary, so a failure desynchronises them

## Context

Spawned from [[TASK-168]] (SH-M083, confirmed 2026-10-19). Every write in `Birko.Data.Localization` is two
independent store calls: the inner store's create/update/delete, then the translation rows
(`LocalizedTranslationRows.Save`, `DeleteTranslations`). The bulk paths loop per entity after one inner bulk
call. Nothing joins them:

- a throw in the second half leaves the entity written with no translation, or **deleted with its rows orphaned
  forever** (`DeleteTranslations` is the only deletion site, so nothing cleans them up);
- a bulk call that fails part-way reports nothing about the entities already processed.
- **found at TASK-168's close review:** when the action throws part-way through a filter-based update, the two
  wrappers differ. The sync wrapper writes its pending translation rows only after `_innerStore.Update` returns,
  so entities the inner store already persisted earlier in the call lose their translation change; the async
  wrapper writes each entity's rows right after its own update, so it keeps them. Flushing the sync list in a
  `finally` is not the fix: it would also write rows for an entity whose inner write is the one that failed
  (SH-M075). Decide it with the boundary.
- **also from that review:** the inner bulk store skips a row deleted concurrently (`EntityNotFoundException`),
  but the sync filter update has already queued that row's translations and writes them, leaving orphans; the
  async filter update calls `UpdateAsync` per row and throws on such a row after earlier rows were written,
  where the default-culture path skips it and finishes.

TASK-168 fixed one ordering case (SH-M075: a filter-based update wrote its rows *before* the inner write, so they
outlived a failed update). The rows are now written after the inner write on every path, so the remaining gap is
a failure **after** the inner write succeeds.

Too large for the triage: it needs a decision about how the wrappers take part in a boundary. The framework has
`ITransactionalStore` (`Birko.Data.Stores`), `IUnitOfWork` (`Birko.Data.Patterns`), and TASK-669's
`AmbientSqlTransaction` for SQL stores sharing a connection. Rule 6 applies: a write that opens its own
connection cannot be inside anybody's transaction, and a boundary is only as wide as its narrowest participant.
It depends on TASK-680, which decides the translation model and may put both tables on one SQL store.

## Acceptance criteria

- [x] Decide and write down how the wrappers join a boundary: participate in an ambient transaction when both
      stores are on one SQL connection, and what they do otherwise (refuse, compensate, or document as best effort)
- [x] On a SQL provider with both tables on one connection, a translation write that throws after the entity
      write rolls back the entity write, asserted on a real database and proven by removing the participation
- [x] A delete whose translation cleanup fails leaves no orphaned rows (rolled back, or reported and retried)
- [x] The README states which store pairings are atomic and which are not

## Out of scope

- The unique key and model shape: [[TASK-680]].
- Batching the reads: [[TASK-679]].

## Human test plan

N/A — library contract, covered by automated tests against a real database.

## Outcome (2026-10-20)

All criteria met; measured live on SQLite, PostgreSQL 16, SQL Server 2022 and MySQL 8.4.

- **AC 1, decided** (by the user, recorded above). The wrappers and `EntityTranslations` take an optional
  `ILocalizedWriteBoundary` (`Birko.Data.Localization/Boundaries`, storage-free; best effort by default).
  - `SqlLocalizedWriteBoundary.For(entityStore, translationStore, requireAtomic)` (`Birko.Data.Localization.SQL`) is
    atomic when both chains reach an `ISqlConnectedStore` (new, `Birko.Data.SQL`) with one `Settings.GetId()`.
  - It joins a caller's boundary and marks it rollback-only on failure. Otherwise it owns a transaction and commits on
    `EntityNotFoundException`, as `Atomically` does.
  - A store given `SetTransactionContext` makes the write run in that transaction. This came from the close review.
- **AC 2:** a translation write failing after the entity write rolls it back, for create, collection create, update,
  collection update, the filter update, delete, collection delete and filter delete, async and sync (`FailingStore`).
  - Mutation "pairing never atomic": 24 red. Also red: "sync path commits a failed write" (4), "a participant's failure
    does not poison the caller" (4), "explicit transaction context ignored" (4).
- **PostgreSQL race** (decided: whole-unit retry): an owned boundary re-runs the write once. Measured: without the
  retry the PostgreSQL race test fails, and SQL Server retries in place. Inside a caller's transaction the typed
  violation surfaces.
  - Found on the way: the first version of the race test passed with the retry removed. `RacingStore` was not an
    `IStoreWrapper`, so `For()` returned best effort. The helper is a wrapper now, and the test asserts `IsAtomic`.
- **AC 3:** atomic pairings roll back. Best-effort deletes remove the entity first and then the rows. A failed cleanup
  throws `TranslationCleanupException` (`RemainingEntityGuids`, the original as its inner exception). It is not
  cancelled once the entities are gone (close review), and `RemoveEntitiesAsync` clears it. Tested live on an InMemory
  translation store and in unit tests.
- **AC 4:** the README pairing table, plus the Localization.SQL README / CLAUDE.md, `docs/localization.md` and
  Birko.Data.SQL's CLAUDE.md.
- **Also:**
  - Inside an atomic boundary the filter update skips its best-effort save.
  - A collection update reporting items not stored saves the stored items' translations. On best effort the report
    wins over a failed save; on an atomic boundary the failure rolls back.
  - A filter update skips a row deleted since its read, as the default-culture path does.
  - Collections are materialized before the boundary, because a retry re-runs the write.
- **Tests:**
  - Unit 276/276 (LocalizationBoundaryTests 15, a recording boundary).
  - SQL 104/104 live (+9 per provider).
  - 14 mutations red: 9 unit, 5 live.
  - The solution builds with no new warnings; Sandbox and Symbio.Birko build.
- **Close review** (9 findings).
  - Fixed:
    - A filter update that hit `EntityNotFoundException` dropped the translations it had already written. It now
      skips that row.
    - An explicit transaction context ran beside the boundary's own transaction.
    - A failed save after the not-stored report replaced the report.
    - A cancellation could leave a cleanup half done.
    - The not-stored lookup was O(n·m).
    - The simple DI overloads could not take a boundary.
  - Kept, as decided: the retry re-running caller code (delegates, the action, an assigned `Guid`), now documented.
  - Documented only: the connector is read when `For()` runs.
  - Not done: folding the culture gating repeated across the four wrappers into one helper (a style call).
- **Spec harvest** (entity-localization regenerated).
  - Fixed: the async default-culture filter update now skips a row deleted since its read, as the sync one does. This
    was TASK-683's second divergence. The sync cleanup now reports a cancellation like the async one.
  - Not acted on: map coverage for `ISqlConnectedStore` / `DataBaseStore` (TASK-689 owns the map), `ParameterReplacer`
    left unused by TASK-681, and two speculative lows.

## Implementation plan

_Drafted 2026-10-20 (Plan agent, against `5412e88f`). Decided by the user 2026-10-20: a wrapper-owned boundary
(option A, built on B), whole-unit retry for the PostgreSQL key race, entity first on best-effort deletes, and the
`requireAtomic` opt-in._

The Context above predates TASK-690: translation writes are now batched, and both filter updates take one path
(read → write each entity → one batched translation save). On a part failure, the translations of the entities already
written are saved best effort (decided by the user).

**AC 3, as read here:** "reported and retried" means a typed error naming the GUIDs whose rows remain. The caller
retries with the idempotent `IEntityTranslations.RemoveEntitiesAsync`; the wrapper does not retry on its own.

### Design

- **Seam (`Birko.Data.Localization`, SQL-free):** `ILocalizedWriteBoundary { bool IsAtomic; void Run(Action);
  Task RunAsync(Func<CancellationToken, Task>, CancellationToken) }`.
  - The default, `BestEffortWriteBoundary`, runs the write inline.
  - An optional `boundary` constructor parameter goes on the four wrappers, their default-model subclasses,
    `EntityTranslations`, and the DI factory overload.
- **SQL boundary (`Birko.Data.Localization.SQL`):** `SqlLocalizedWriteBoundary.For(entityStore, translationStore,
  requireAtomic = false)`.
  - It walks both chains (`StoreChain.Walk`) to the store implementing a new non-generic `ISqlConnectedStore`
    (`Birko.Data.SQL`, implemented explicitly by `DataBaseStore` and `AsyncDataBaseStore`) and compares
    `Connector.Settings.GetId()`, the key `AmbientSqlTransaction` uses (rule 16).
  - On a match, each two-store write runs in one transaction:
    - async: a `SqlUnitOfWork` built in the running flow, which joins a caller's boundary;
    - sync: mirrors `DataBaseBulkStore.Atomically`.
  - A failure inside a caller's boundary marks it rollback-only.
  - With no match, `For()` returns best effort, or throws when `requireAtomic` is set.
  - `EntityNotFoundException` (TASK-555) commits the stored part and rethrows, as `Atomically` does.
- **PostgreSQL key race (decided):** when the boundary owns the transaction, a `StoreUniqueViolationException` rolls it
  back and re-runs the whole write once, in a new transaction. The second pass reads the other writer's row and updates
  it. Inside a caller's transaction there is no retry: the typed error surfaces, documented. Savepoints stay with
  TASK-685.
- **Wrapped paths:**
  - Non-default culture: `Create(T)`, `Create(IEnumerable)`, `Update(T)`, `Update(IEnumerable)` and
    `Update(filter, Action)`, plus the `PropertyUpdate` fallback that routes into it.
  - Every culture: `Delete(T)`, `Delete(IEnumerable)`, `Delete(filter)`.
  - Default-culture creates and updates write one store and stay outside.
  - The filter update skips its best-effort save when `IsAtomic`.
  - `Update(IEnumerable)`, on `EntityNotFoundException`, saves the translations of the stored items before
    rethrowing.
- **Deletes (decided):** atomic pairings roll back. Otherwise the entity is deleted first; a failed cleanup throws
  `TranslationCleanupException : StoreException` with `RemainingEntityGuids`, the original error as its inner
  exception, and a pointer to `RemoveEntitiesAsync` (rule 22). Rule 9: no `catch (StoreException)` filter changes
  meaning.
- **`IEntityTranslations`:** `SetAsync`, `RemoveCultureAsync` and `RemoveEntitiesAsync` each run as one unit through
  the boundary.
- `Destroy` (DDL) stays outside the boundary (rule 35).

### Tests

- **Live, both tables on one `Settings()`, all four providers:**
  - A `FailingStore<T>` (an `IStoreWrapper` that throws client-side on its N-th write) fails the translation write
    after the entity write in create, update, bulk update, filter update, delete, bulk delete and filter delete,
    sync and async.
  - Each case asserts that the entity rows are unchanged and no rows are orphaned.
  - A caller's `SqlUnitOfWork` is joined, and a failure marks it rollback-only.
  - **Mutation:** best-effort boundary → red on every provider.
- **PostgreSQL:** a racing writer inside an owned boundary is retried once (one row, our value). Inside a caller's
  boundary it surfaces as the typed violation. MySQL skips only the key and race tests (TASK-684).
- **Best effort:** the translation store on a second SQLite file. The entity survives, and a failed delete raises
  `TranslationCleanupException` with the GUIDs, after which `RemoveEntitiesAsync` clears them.
- **Unit:** a recording boundary proves each two-store path enters it once and default-culture create/update do not.
  Also the `IsAtomic` skip, and the cleanup exception's contents.

### Docs

- README pairing table: atomic, caller-owned, not atomic, different settings, non-SQL backends, and rule 6's
  own-connection decorators.
- Project and test CLAUDE files, `docs/localization.md`, the spec, and Recent Updates.
- Remove the "not atomic yet (TASK-683)" lines.
