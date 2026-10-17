---
id: TASK-649
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

# SoftDelete Save of a soft-deleted entity creates it again under the same Guid

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `store-decorator-composition`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`Save` of a soft-deleted entity: `Update` throws `EntityNotFoundException` (TASK-159), and `StoreSave` then runs
`Create` under the same Guid against a store that still holds the row
(`Birko.Data.Patterns/Decorators/SoftDeleteStoreWrapper.cs:52-54,73`, async twin): a duplicate-key error or a
resurrection, depending on the backend. Also minor: a soft-delete bulk update whose inner update throws for missing items
never reports the soft-deleted ones (`SoftDeleteBulkStoreWrapper.cs:41-46`).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Decide what Save of a soft-deleted entity means (refuse, or restore explicitly) and test it on two backends
- [x] Red on the current code; the spec `docs/specs/store-decorator-composition.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced on three backends** with a temporary probe (since deleted): create, soft-delete, change, `Save`. InMemory,
SQLite and PostgreSQL all threw `StoreUniqueViolationException`, "an entity with the same key is already stored …
use Update or Save". `Update` reported the soft-deleted row as not found (TASK-159), and `StoreSave` fell through to a
create of the same Guid. No backend resurrected the row, but the refusal gave the wrong reason. A store that does
not enforce the key would have brought the row back from a stale copy.

**Decision (user, 2026-10-17): refuse, and restore explicitly.**
- `Save` / `SaveAsync` on the soft-delete wrappers first read the stored row. If it is soft-deleted they throw the
  new `EntitySoftDeletedException` (`Birko.Data.Patterns/Decorators`) naming the Guid. It is a `StoreException`, so
  `catch (StoreException)` still selects it (rule 9), and not an `EntityNotFoundException`, which `StoreSave` would
  read as "create it".
- The new `Restore(T)` / `RestoreAsync(T)` clear `DeletedAt` and write the entity as it stands. They throw
  `EntityNotFoundException` when no row holds the Guid, and simply write a live entity. The bulk wrappers inherit
  them.
- Thanks to TASK-638, a restored row comes back with its reserved slug and without its retired default.

**The minor finding is fixed too.** A soft-delete bulk update whose inner update reported never-stored items lost the
soft-deleted ones. Both are now named in one `EntityNotFoundException`, sync and async.

**Red check:** with the `Save` guard off, 2 tests failed (sync and async); with the bulk naming off, 1. Tests:
`Task649SoftDeletedSaveTests` (Patterns, 8, InMemory), and `Task649SoftDeletedSaveLiveTests` (Migrations.SQL, live on
SQLite and PostgreSQL).

**Docs:** the spec `store-decorator-composition` (the scenario that recorded the fall-through now reads as fixed; a
new bulk scenario), `docs/patterns.md`, `Birko.Data.Patterns/CLAUDE.md`, and Recent Updates.

**Regression:** the 88 suites that import `Birko.Data.Patterns` or `Birko.Data.Composition` (4,975 tests) are
green, live on local Docker (no Redis, so its gated tests returned early).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-649` — leave empty until then._
