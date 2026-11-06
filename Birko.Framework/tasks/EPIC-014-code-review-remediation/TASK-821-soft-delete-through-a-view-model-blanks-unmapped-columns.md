---
id: TASK-821
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-453, TASK-454, TASK-159, TASK-649]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Soft-deleting through a view-model repository blanks every column the view model does not map

## Context

Found while writing [[TASK-454]]'s test (2026-11-06). `SoftDeleteStoreWrapper.Delete(data)` (and its async twin) stamps
`DeletedAt` on the entity it is **given** and writes that entity whole (`_innerStore.Update(data)`). The view-model
repositories' `Delete` passes `LoadModelInstance(viewModel)`, a FRESH model carrying only what `MapToModel` assigns. So
the soft delete overwrites the stored row with that partial model.

**Measured** (scratch probe over `InMemoryStore`, not committed): a `Post { Title = "t", Body = "kept" }` deleted through
an `AbstractViewModelRepository` whose view model maps `Guid` and `Title` reads back `DeletedAt` set, `Title = "t"`,
**`Body = null`**. A later `Restore` brings the row back without its body. Any caller passing a partial entity to
`Delete` does the same; SH-H034 fixed exactly this shape on the update path and not here.

## Acceptance criteria

- [x] Reproduced as observed state in a committed test (the probe above), sync and async.
- [x] A soft delete stamps the STORED row, not the caller's instance (read, stamp, update, as the
      already-deleted check reads it anyway), or an equivalent the measurement shows keeps every column.
- [x] The other decorators whose `Delete` writes the given entity are checked the same way and listed.
- [x] Recent Updates entry (TASK-715 rolled to CHANGELOG to keep the budget).

## Human test plan

N/A — fully covered by automated tests.

## Outcome — 2026-11-06

New `SoftDeleteFilter.ToWrite(data, stored, now)`: stamps the caller's instance (as before) and returns a stamped
memberwise copy of the **stored** row to write; with no stored row it returns the caller's instance, so the store still
reports a missing key (`EntityNotFoundException`). All four soft-delete paths use it: `SoftDeleteStoreWrapper.Delete`,
`AsyncSoftDeleteStoreWrapper.DeleteAsync`, and the two bulk `Delete(IEnumerable)`, which now read the stored rows whole
(they already read the deleted ones to skip them). The copy matters: the portable stores hand back the instance they
hold, so stamping it would change store state before the write.

**Other decorators checked:** of every `Delete(T)` / `DeleteAsync(T)` in `Birko.Data.*`, only the soft-delete wrappers
write the given entity; audit, timestamp, sluggable, default, versioned, tenant and localized wrappers forward the
delete. The filtered soft delete was already a native property update (`SoftDeleteFilter.Deletion`), which writes only
the stamped columns.

Tests: `Birko.Data.Patterns.Tests/Task821SoftDeleteKeepsColumnsTests` (single and bulk, sync and async, a stub holding
only the Guid; the stored row asserted as observed state) plus the missing-key case; with the old `Delete` code put back,
exactly the 4 column tests go red and the missing-key one stays green. On the path it was found:
`HiddenRowViewModelUpdateTests.Soft_deleting_through_a_view_model_keeps_the_columns_it_does_not_map` (+ async).
