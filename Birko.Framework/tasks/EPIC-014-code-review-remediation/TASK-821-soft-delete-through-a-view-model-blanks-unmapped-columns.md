---
id: TASK-821
parent: EPIC-014
feature: null
status: todo
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

- [ ] Reproduced as observed state in a committed test (the probe above), sync and async.
- [ ] A soft delete stamps the STORED row, not the caller's instance (read, stamp, update, as the
      already-deleted check reads it anyway), or an equivalent the measurement shows keeps every column.
- [ ] The other decorators whose `Delete` writes the given entity are checked the same way and listed.
- [ ] Recent Updates entry.

## Human test plan

N/A — fully covered by automated tests.
