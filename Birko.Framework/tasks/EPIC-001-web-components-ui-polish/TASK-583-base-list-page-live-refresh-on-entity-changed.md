---
id: TASK-583
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: todo
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `base-list-page` cannot refresh when another user changes its entities

## Context

Filed from Symbio TASK-898, decided 2026-10-13. When a list is open in one tab and a row of its entity type is
created, edited or deleted in another tab, or by another user, the list does not change until it is reloaded.
Symbio will publish an `entity-changed` event over its tenant-scoped SSE stream. Every CRUD list is a
`base-list-page`, and it has no way to react to that event. It does already have `reload()`
(`Birko.Web.Shell/src/pages/base-crud-page.ts:987`), which returns the table's in-flight `load()`.

**The decided contract (Symbio TASK-898)** — the framework implements this and does not invent its own:

```
event: entity-changed
data: { moduleId: string, entityType: string, entityGuid: string | null, kind: 'created' | 'updated' | 'deleted' }
```

- **Ids only, never a body.** The list reloads through the normal permission-checked API and never patches a row
  from the event.
- `entityGuid: null` means a bulk or filter-based write changed a set of rows the producer did not enumerate.
  The list reloads either way, so null exists to avoid sending one event per row.
- Tenant scoping and permission filtering are done by the **producer** (Symbio's server), not by this opt-in.

## Acceptance criteria

- [ ] A typed `EntityChangedEvent` matching the shape above, exported from Birko.Web.Shell (or Core, if `SseClient`
      users want it without the Shell)
- [ ] An **opt-in** on `base-list-page`, e.g. `protected liveRefresh?: { moduleId: string; entityType: string;
      debounceMs?: number }` (default off). It matches on `moduleId` + `entityType` and calls `reload()`,
      debounced, so a burst of events causes one reload
- [ ] The event source is **injected**, a `subscribe(handler) => unsubscribe` the app provides once (for
      example from its `SseClient`), so the Shell neither opens a connection nor knows the event's transport
- [ ] The subscription ends when the page unmounts. No reload is pending after navigation
- [ ] No reload while the page's create/edit modal is open. Defer it until the modal closes, so an open form
      is not disturbed
- [ ] An event for another module or entity type, or a malformed payload, is ignored
- [ ] Tests: debounce (N events → 1 reload), matching, unmount cleanup, the deferred reload while the modal is open
- [ ] README documents the opt-in and the contract. Record the Birko.Web tag in Symbio TASK-898

## Out of scope

- The server-side producer, tenant scoping and permission filtering: Symbio TASK-898
- Detail and edit pages (a concurrent edit is a conflict question, not a refresh)
- Patching rows in place from the event

## Human test plan

N/A here: the behaviour is asserted by the Shell tests. The two-tab, two-user check is in Symbio TASK-898's
test plan, where the producer exists.
