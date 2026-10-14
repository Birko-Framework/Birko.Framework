---
id: TASK-581
parent: EPIC-018
feature: FEATURE-018
priority: P2
status: done
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `SyncManager` retries a failing write every cycle forever: no backoff, no cap, no dead letter

## Context

Asked for by Symbio (its TASK-151, P2), which records it as a framework gap to fix upstream. TASK-199 fixed how a
replayed write's response is classified and left this out of scope.

`SyncManager` (`Birko.Web.Core/src/offline/sync-manager.ts`) drains the outbox FIFO on the `online` event and on a
30-second interval. A replay classified `failed` only sets `status = 'failed'`, increments `retries` and records
`lastError` (lines ~107-120), and the next cycle tries it again. So a write the server will never accept (a
validation refusal the client cannot fix) is re-sent every 30 seconds for as long as the page is open, and the
user sees a pending badge that never resolves.

Symbio builds the failed-action UI (inspect, retry, discard) on top of whatever this exposes.

## Acceptance criteria

- [x] Exponential backoff between attempts of one action, configurable (`retryBaseMs`, `retryMaxMs`); the
      next-attempt time is persisted with the action, so a reload resumes the schedule
- [x] A configurable retry cap (`maxRetries`) moves an action to a terminal `dead` state; an unrecoverable
      response (a 4xx that is neither 409 nor 408/429) goes there at once rather than after the cap
- [x] A dead or backing-off action does not block later queued writes to **other** entities; writes to the
      **same** entity keep their order behind it
- [x] Public API to list dead actions, retry one (resets its schedule) and discard one, plus an event when an
      action dies, so a consumer can build the failed-action UI
- [x] Tests for the backoff timing, cap → dead, unrecoverable → dead, and ordering around a dead action
- [x] README (Core) documents the states and the options; a changed default is called out in CHANGELOG

## Out of scope

- The failed-action UI itself (Symbio TASK-151)
- Browser Background Sync registration
- 409 conflict handling (already a separate state with its own listener)

## Human test plan

N/A — the schedule, the transitions and the ordering are asserted by the tests above with a fake clock; the
consumer's UI is verified in Symbio TASK-151.

## Progress log

- 2026-10-14 — Done and released in **Birko.Web v0.1.6** (Web `3cca43e`, tag commit `5a6fd44`; Playground `051cc0b`).
  - Backoff `retryBaseMs` 5 s doubling to `retryMaxMs` 15 min, `nextAttemptAt` persisted; `maxRetries` 10 → `dead`;
    `dead` at once on a 4xx other than 401, 408, 409, 429. **401 is retryable**, a deliberate addition to the
    criterion's list: an expired session is fixed by signing in, not by giving up on the write. A `DELETE` 404 with
    `deleteMissingIsApplied: false` stays retryable, as that option documents.
  - Per-entity order: key `entityType:entityId`, or the path without an `entityId`. A `conflict` action does **not**
    hold its entity; 409 handling is out of scope and kept as it was.
  - `getDead` / `retry` / `discard` / `onDead`; `SyncResult.dead`; `pendingCount` excludes dead actions, so Symbio
    TASK-151's failed-write UI is where they show.
  - Tests: Playground `sync-retry-smoke` 43/43, each mechanism proven red (19 fail with backoff, dead-on-4xx and the
    hold removed). It waits for `carousel-smoke`, whose drag/autoplay timing failed under the suite's IndexedDB load in
    2 of 3 runs; after that, 11 of 12 full runs were green, and the one failure was not captured.
  - Spawned [[TASK-598]]: an action interrupted mid-sync stays `syncing` forever.
