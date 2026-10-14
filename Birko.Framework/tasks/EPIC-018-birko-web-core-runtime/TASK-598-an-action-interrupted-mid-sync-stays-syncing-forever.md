---
id: TASK-598
parent: EPIC-018
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
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

- [x] An action found `syncing` when a sync starts (i.e. left over from an interrupted one) is treated as due again,
      keeping its retry count. Decide and record whether that resend is safe for each method: a POST that did land
      would be created twice unless it is `idPinned` — the classification rules for a replay apply
- [x] Two tabs: a `syncing` action that another live tab is sending right now must not be sent twice. Either a lock
      (Web Locks API / a lease timestamp) or a documented single-syncer rule
- [x] It counts in `pendingCount` while it is due, and it holds its entity like a `failed` one
- [x] Playground smoke: an action stored as `syncing` is picked up by a fresh SyncManager; a live lease is respected

## Out of scope

- Background Sync registration (TASK-581's out of scope too)

## Human test plan

N/A — covered by automated tests (a stored `syncing` action and a fresh SyncManager reproduce the reload).

## Implementation plan

_Populated by `/tasks plan TASK-598` — leave empty until then._

## Progress

**2026-10-14 — fixed in Birko.Web, release v0.1.10 pending.** Decisions (taken with the owner, 2026-10-14):

- **Resend only where a landed replay is recognisable.** A recovered `PUT`, `DELETE` or `idPinned` `POST` becomes
  `failed`, due at once, retry count kept: if it had landed, the replay reads as applied (`DELETE` → 404, pinned `POST`
  → 409, `PUT` is idempotent). A plain `POST` or a `PATCH` becomes **`dead`** with *"Interrupted while sending: it may
  have been applied. Retry or discard it."*, and `onDead` fires: resending could apply it twice, so the user decides.
  `PATCH` is not treated as idempotent: an increment or an array append is not.
- **One drainer: Web Locks, with a lease fallback.** `sync()` runs under `navigator.locks.request('birko-sync:<queue
  name>', { ifAvailable: true })`; another tab's `sync()` returns a zero result without sending. The holder knows any
  `syncing` action is orphaned (a closed tab frees its lock), so recovery is immediate. Without Web Locks
  (`locks: null`, a non-secure context), `syncingSince` is stored with `syncing`, and an action counts as abandoned
  past `syncLeaseMs` (default 5 min); one with no timestamp (written before this change) counts as abandoned. A
  `syncing` action inside its lease holds its entity.
- **The lock also fixes a duplicate send that predates this task:** two managers on one queue both sent the second
  pending action (`PUT a, PUT b, PUT b` in the red run).
- `pendingCount` now counts `syncing` too. Needed for recovery, not just display: the periodic sync runs only while
  the count is above zero, so a reload whose only write was stranded would never have synced.

Gate: `sync-interrupt-smoke` (21 checks: reload count, PUT / DELETE / idPinned POST resent and applied, retries kept
and backoff from them, plain POST and PATCH dead with onDead and the entity held, two managers send each action once,
lease respected and reclaimed in order, untimestamped treated as abandoned). **Red 4/21** before the fix, 21/21 after;
full `verify.mjs` green. `backport-smoke`'s M260 fake queue gained `getAll()` and `locks: null` (it tests the `online`
listener; a real lock's acquisition outlasts its `setTimeout(0)`).

