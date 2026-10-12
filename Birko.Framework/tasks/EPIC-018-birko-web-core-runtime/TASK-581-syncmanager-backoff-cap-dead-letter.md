---
id: TASK-581
parent: EPIC-018
feature: FEATURE-018
priority: P2
status: todo
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

- [ ] Exponential backoff between attempts of one action, configurable (`retryBaseMs`, `retryMaxMs`); the
      next-attempt time is persisted with the action, so a reload resumes the schedule
- [ ] A configurable retry cap (`maxRetries`) moves an action to a terminal `dead` state; an unrecoverable
      response (a 4xx that is neither 409 nor 408/429) goes there at once rather than after the cap
- [ ] A dead or backing-off action does not block later queued writes to **other** entities; writes to the
      **same** entity keep their order behind it
- [ ] Public API to list dead actions, retry one (resets its schedule) and discard one, plus an event when an
      action dies, so a consumer can build the failed-action UI
- [ ] Tests for the backoff timing, cap → dead, unrecoverable → dead, and ordering around a dead action
- [ ] README (Core) documents the states and the options; a changed default is called out in CHANGELOG

## Out of scope

- The failed-action UI itself (Symbio TASK-151)
- Browser Background Sync registration
- 409 conflict handling (already a separate state with its own listener)

## Human test plan

N/A — the schedule, the transitions and the ordering are asserted by the tests above with a fake clock; the
consumer's UI is verified in Symbio TASK-151.
