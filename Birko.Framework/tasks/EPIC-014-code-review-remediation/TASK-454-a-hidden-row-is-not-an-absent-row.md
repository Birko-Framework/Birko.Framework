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
