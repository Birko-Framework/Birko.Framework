---
id: TASK-454
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
related: [TASK-316]
findings: []
pr: null
affects: [Birko.Data.ViewModel, Birko.Data.Patterns, Birko.Data.Tenant]
---

# A decorator-hidden row reads as absent, and updating it resurrects it with its columns blanked

## Context

Found by the security pass on [[TASK-316]]. Pre-existing behaviour that TASK-316 did not change, but its
new merge branch now makes the wrong premise explicit in a comment, which will read to the next
maintainer as a considered decision unless it is owned.

`SoftDeleteStoreWrapper.Read(Guid)` returns `null` for a row whose `DeletedAt` is set, and
`TenantStoreWrapper.Read(Guid)` returns `null` for another tenant's row. **The row exists; it is
hidden.** Every caller that treats `null` as "no such row" is therefore wrong in a specific way:

- `AbstractViewModelRepository.Update` falls back to writing the freshly mapped model, and
  `SoftDeleteStoreWrapper.Update` simply forwards — so updating a soft-deleted entity **resurrects it**
  (`DeletedAt` reset from the fresh instance) with every unmapped column blanked.
- The tenant case is caught: `TenantStoreWrapper.Update` re-reads **unscoped** and throws
  `TenantMismatchException` (SH-H047). Soft delete has no such guard.

## Rescoped 2026-11-04 — part of this was done by later work

Found re-checking EPIC-014's open tasks against the code. **Done:** the resurrection. [[TASK-159]] (`ebb0c1e7`) makes `SoftDeleteStoreWrapper.Update` (sync and async) throw
`EntityNotFoundException` for a soft-deleted row, and [[TASK-649]] refuses `Save` of one and adds `Restore`.
**Remaining, and this is the task now:** the comment in `AbstractViewModelRepository.LoadModelInstanceForUpdate`
(and its async twin) still says "no backend REPORTS a missing row", which [[TASK-555]] made untrue. Correct it, and
check that a view-model update of a hidden row surfaces `EntityNotFoundException` rather than returning quietly.
Coordinate with [[TASK-453]], which changes the same method to skip an unchanged save.

## Acceptance criteria

- [ ] Reproduced as observed state — a soft-deleted row, updated through a ViewModel repository, read back
      with `DeletedAt` cleared and an unmapped column blanked. Never as "no exception was thrown"
- [ ] A decision on the contract: refuse the update, keep it hidden (no-op), or resurrect deliberately.
      Whichever is chosen applies to **every** caller that reads a decorated store by key, not just this
      one — enumerate them first
- [ ] `SoftDeleteStoreWrapper` gets the guard, or it is recorded why the tenant wrapper needs one and this
      does not
- [ ] TASK-316's comment in `LoadModelInstanceForUpdate` is updated to point at whatever is decided

## Human test plan

`N/A — fully covered by automated tests.`
