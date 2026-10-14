---
id: TASK-598
parent: EPIC-018
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-036]
pr: null
github-issue: null
jira-key: null
---

# A queued write interrupted mid-sync stays `syncing` forever, invisible to every list and count

## Context

Found while building [[TASK-581]] (2026-10-14). `SyncManager.sync()` (`Birko.Web.Core/src/offline/sync-manager.ts`)
writes `status = 'syncing'` to IndexedDB **before** it sends an action, and only rewrites the status when the request
settles. If the tab is closed, reloaded or crashes while the request is in flight, the stored action keeps `syncing`.

Nothing reads `syncing` back: `getPending()` returns `pending` and `failed`, `getDead()` returns `dead`, and
`pendingCount` is built from `getPending()`. So the write is never sent again, never shown as pending, never listed
as failed, and TASK-581's per-entity hold does not see it either: a later write to the same entity goes ahead of it.

Measured by reading the code, not reproduced in a browser.

## Acceptance criteria

- [ ] An action found `syncing` when a sync starts (i.e. left over from an interrupted one) is treated as due again,
      keeping its retry count. Decide and record whether that resend is safe for each method: a POST that did land
      would be created twice unless it is `idPinned` — the classification rules for a replay apply
- [ ] Two tabs: a `syncing` action that another live tab is sending right now must not be sent twice. Either a lock
      (Web Locks API / a lease timestamp) or a documented single-syncer rule
- [ ] It counts in `pendingCount` while it is due, and it holds its entity like a `failed` one
- [ ] Playground smoke: an action stored as `syncing` is picked up by a fresh SyncManager; a live lease is respected

## Out of scope

- Background Sync registration (TASK-581's out of scope too)

## Human test plan

N/A — covered by automated tests (a stored `syncing` action and a fresh SyncManager reproduce the reload).

## Implementation plan

_Populated by `/tasks plan TASK-598` — leave empty until then._
