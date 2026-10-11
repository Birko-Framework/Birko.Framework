---
id: TASK-547
parent: EPIC-018
feature: null
status: done
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

- [x] **One retry loop:** on error the native source is closed and `SseClient`'s schedule alone owns the retry
- [x] **A delay schedule:** e.g. `reconnectDelays?: number[]` (default `[5000, 10000, 30000, 60000]`, last value
      repeats); the delay grows per consecutive failure and resets on `_open`. `reconnectMs` keeps working as a single
      fixed delay; giving both is refused (or one documented winner), never silently merged
- [x] **A failure signal:** consecutive failures are counted (reset on `_open`); after a threshold (e.g.
      `failingAfter?: number`, default ~20) an internal `_failing` event is dispatched with `{ failures, nextDelayMs }`.
      Recovery stays signalled by `_reconnect`
- [x] `disconnect()` cancels any pending retry; `connect()` after `disconnect()` starts the schedule fresh
- [x] Tests (fake timers + a fake `EventSource`): no second connection while one retry is pending; delays follow the
      schedule and reset on open; `_failing` fires once at the threshold; disconnect cancels; the old fixed
      `reconnectMs` behaviour is unchanged for existing callers
- [x] `Birko.Web.Core` README/CLAUDE.md document the options and events; the final option and event names are sent
      back to the Presenter session (its TASK-009)

## Out of scope

- Presenter's own wiring (passing the options on its three pages, showing the hint on `_failing`) — Presenter TASK-009

## Human test plan

- [ ] In Presenter, open the phone remote on Wi-Fi, toggle airplane mode for ~2 minutes, then off. Expected: the
      retry delays visibly lengthen (devtools network tab), the "reconnection failing" hint appears after the
      threshold, and on recovery exactly one stream reconnects and `_reconnect` clears the hint

## Implementation plan

Done directly.

## Resolution (2026-10-10)

`Birko.Web.Core/src/http/event-source.ts`:

- **One retry loop.** `onerror` closes the native source before scheduling, so the browser's own retry (and a
  server `retry:` field) never runs alongside the client's. `connect()` (public) resets the schedule; retries go
  through a private `_open()`, so a retry does not reset it.
- **Schedule.** `reconnectDelays?: number[]`, default `[5000, 10000, 30000, 60000]`, last repeats, reset on open.
  `reconnectMs` becomes the one-entry schedule `[reconnectMs]` — unchanged for the existing callers (Symbio, DraCode
  KoboldLair pass `reconnectMs: 5000`); `0` still disables reconnecting. **Both options → `TypeError`**; an empty or
  negative schedule → `TypeError`.
- **Failure signal.** `_failing` with `SseFailing { failures, nextDelayMs }` (type exported), fired **once** when
  the consecutive count reaches `failingAfter`, again only after an open and a new outage; `0` never fires. Recovery
  stays `_reconnect`. New `failures` getter; `state` reads `'connecting'` while a retry is pending (it would read
  `'closed'` now that the native source is closed during the wait).
- **`failingAfter` default is 5, not the provisional ~20.** On the default schedule 20 failures is ~18 minutes, so
  this task's own human test (airplane mode ~2 minutes) could never show the hint; 5 failures is ≈2¾ minutes
  (5 + 10 + 30 + 60 + 60 s). A consumer wanting a longer wait sets it.

Docs: Core README (§ SseClient — reconnecting, options, events) and CLAUDE.md (§ SseClient rules).

**Proof.** Birko.Web `dad5d8a`, Playground `d7c0539`, Presenter `e5467b1`. Playground `src/sse-retry-smoke.ts` (fake `EventSource` swapped in for the run; delays read by wrapping
`setTimeout` around the synchronous error dispatch only): 30/30, full run 0 failing checks. **Mutation** — old
`event-source.ts` restored: 16 fail, including "a pending retry never closes a connection the native source
recovered" (the overlap defect itself). Core and Playground `tsc` clean.

**Sent to Presenter:** the final names and the `failingAfter` default are written into Presenter TASK-009, and its
`blocked:` line removed. The human test plan above runs in Presenter, after its TASK-009 wiring.

## Verified (2026-10-11)

Presenter's human test passed, run by the user against Presenter TASK-009 (`_failing` → a "connection lost" hint,
`_open` → clears it, on the presenter, remote and follow pages; `SseClient` built with `url` only, so the default
schedule). Server stopped: the hint appeared after ~2 min on all three pages. A real phone remote in airplane mode
for ~3 min: the hint appeared; airplane mode off: it cleared on its own and Next/Prev worked again. Merged on
Presenter main as `601477e` (task commit `aebcb9a`). verify → done.

Found on the way, not a defect: Chrome DevTools Network → *Offline* does not break an already-open `EventSource`,
so it cannot produce `_failing`. Recorded in the Core README (Birko.Web `910653c`).
