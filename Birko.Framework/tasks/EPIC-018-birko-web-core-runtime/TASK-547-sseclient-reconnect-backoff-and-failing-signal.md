---
id: TASK-547
parent: EPIC-018
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-09
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# SseClient: one retry loop, exponential backoff, and a "reconnection failing" signal

## Context

Requested 2026-10-09 by the Presenter session (its TASK-009 is parked as blocked on this; the user approved putting
it in Birko rather than Presenter). Presenter's presenter, remote and follow views each hold an SSE stream, and the
phone remote on flaky Wi-Fi cycles through failures. Any Birko app with SSE on mobile has the same need.

`Birko.Web.Core/src/http/event-source.ts` (`SseClient`, separate repo `C:\Source\Birko\Web`) reconnects on a fixed
`reconnectMs` (default 5000), forever, with no backoff and no way to tell the app that reconnects keep failing.

**Defect found with it (verified 2026-10-09, `event-source.ts:63–67`):** `onerror` calls `_scheduleReconnect()`, but on
a transient error the native `EventSource` is already in `readyState === CONNECTING` and retrying on its own (browser
default, or the server's `retry:`). Two retry loops overlap: if the native retry succeeds first, the timer then calls
`connect()` → `disconnect()`, closing the healthy connection and opening a new one.

## Acceptance criteria

- [ ] **One retry loop:** on error the native source is closed and `SseClient`'s schedule alone owns the retry
- [ ] **A delay schedule:** e.g. `reconnectDelays?: number[]` (default `[5000, 10000, 30000, 60000]`, last value
      repeats); the delay grows per consecutive failure and resets on `_open`. `reconnectMs` keeps working as a single
      fixed delay; giving both is refused (or one documented winner), never silently merged
- [ ] **A failure signal:** consecutive failures are counted (reset on `_open`); after a threshold (e.g.
      `failingAfter?: number`, default ~20) an internal `_failing` event is dispatched with `{ failures, nextDelayMs }`.
      Recovery stays signalled by `_reconnect`
- [ ] `disconnect()` cancels any pending retry; `connect()` after `disconnect()` starts the schedule fresh
- [ ] Tests (fake timers + a fake `EventSource`): no second connection while one retry is pending; delays follow the
      schedule and reset on open; `_failing` fires once at the threshold; disconnect cancels; the old fixed
      `reconnectMs` behaviour is unchanged for existing callers
- [ ] `Birko.Web.Core` README/CLAUDE.md document the options and events; the final option and event names are sent
      back to the Presenter session (its TASK-009)

## Out of scope

- Presenter's own wiring (passing the options on its three pages, showing the hint on `_failing`) — Presenter TASK-009

## Human test plan

- [ ] In Presenter, open the phone remote on Wi-Fi, toggle airplane mode for ~2 minutes, then off. Expected: the
      retry delays visibly lengthen (devtools network tab), the "reconnection failing" hint appears after the
      threshold, and on recovery exactly one stream reconnects and `_reconnect` clears the hint

## Implementation plan

_Populated by `/tasks plan TASK-547` — leave empty until then._
