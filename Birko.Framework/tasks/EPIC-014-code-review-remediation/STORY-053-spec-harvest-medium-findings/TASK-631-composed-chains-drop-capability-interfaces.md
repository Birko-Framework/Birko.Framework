---
id: TASK-631
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M336]
pr: null
github-issue: null
jira-key: null
---

# A composed decorator chain drops every capability outside `IAsyncBulkStore<T>`

## Context

Spawned by [[TASK-159]] (2026-10-15), which confirmed SH-M336 by reading and left it unfixed because the fix is a design
decision across every decorator, not a local edit.

`StoreWrapperBuilder.Build` takes and returns `IAsyncBulkStore<T>`, and each decorator implements only that surface.
A raw store that also implements `IAsyncAggregatableStore<T>`, a transactional interface or `ISettingsStore` loses them
once wrapped: `wrapped as IAsyncAggregatableStore<T>` is null. The caller's way out is `GetInnerStore()`, and an
operation sent there bypasses the soft-delete exclusion and the tenant scope of every decorator above it, silently.
Symbio builds its stores through the builder (`RepositoryBase.cs:36`).

## What to decide

1. **Forward** each capability through every decorator, applying the decorator's own scope (an aggregate through the
   soft-delete wrapper must exclude deleted rows; through the tenant wrapper, other tenants). That is one translation
   per capability per decorator, and a decorator that cannot scope a capability must refuse it, not forward it raw
   (rule 41).
2. Or **refuse**: make `GetInnerStore()` access to a scoped capability visible (a documented escape hatch), and say on
   the builder which capabilities the chain does not carry.

## Acceptance criteria

- [ ] An aggregate through a composed chain either honours every decorator's scope or is refused, measured with a
      soft-deleted row and a second tenant's row present
- [ ] The same answer for transactions and settings
- [ ] Red on the current code

## Human test plan

N/A: a library contract, asserted by tests.
