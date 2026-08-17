---
id: TASK-198
parent: EPIC-018
feature: FEATURE-018
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
created: 2026-08-10
completed: 2026-08-10
depends-on: []
blocks: []
findings: []
pr: 79fe552 (Birko.Web.Core), ae70de8 (Birko.Web.Playground)
github-issue: null
jira-key: null
---

# `fetch` has no timeout, so a dead connection hung the app forever — and a stalled body reported success

## Context

`../../Web/Birko.Web.Core/src/http/api-client.ts` — `_fetch` / `_send`.

`fetch` has **no timeout of its own**. A connection that dies *without sending a reset* — a phone whose
screen sleeps, a dropped tunnel, a captive network black-holing the packet — leaves the promise pending
for the life of the page.

Found on the **2026-08-10 Reps device pass**, where it cost a fully-performed workout: the write never
reached the server, was **never queued** (queueing keys off a *rejected* fetch), and every caller
awaiting it wedged — an `await` on a never-settling promise does not continue, **not even into a
`finally`**. Blast radius is every `Birko.Web` consumer, since `ApiClient` is the only HTTP path.

**Provenance note:** the timeout itself was written in another session and found sitting **uncommitted**
in `Web/Birko.Web.Core` during the [[TASK-196]]/[[TASK-197]] sweep — the same pattern that sweep
existed to close, and the second instance in three days. It is now committed with coverage.

## Approach

`_fetch` runs each request under an `AbortController` with a **20s default**, overridable per client via
`timeoutMs` and disabled with `0`.

The abort surfaces as the **same `{ ok: false, status: 0 }` envelope a network error already produces**.
That is the whole design rather than an implementation detail: the timeout is folded into a failure mode
the stack already handles end to end, so `_sendWrite` diverts the write to the outbox, `SyncManager`
retries it, and a read falls back to its mirror. No new failure mode reaches callers; only the log
distinguishes "refused" from "never answered". Generous rather than tight, because firing early is cheap
precisely *because* it is handled.

**Two gaps were found while testing it, and both are the more interesting half of this task:**

- **A stalled BODY was reported as SUCCESS.** The abort lands on the body read as easily as on the
  response — headers arrive, the body stalls — and it throws from `response.json()`, not from `fetch`.
  The body-read `catch` swallowed that as a malformed body and returned the response's own
  `ok: true, status: 200, data: null`. A write would therefore be reported as **saved** and never
  queued; a read would render empty instead of falling back to its mirror. That is the defect this
  change exists to fix, moved one layer down from *hangs forever* to *silently wrong* — and the
  original timer already made it **settle**, which is exactly what made the surviving wrong answer easy
  to miss.
- **`DEFAULT_REQUEST_TIMEOUT_MS` was unreachable.** Exported from `api-client.ts` but never added to
  `src/http/index.ts`, whose barrel is an explicit named list — so the documented knob could not be
  imported by any consumer. Caught by the **build**, not by reading the diff.

## Acceptance criteria

- [x] A request that never answers settles, with the network-error envelope
- [x] A timed-out **write** reaches the outbox rather than being lost
- [x] A stalled response **body** settles *and* is reported as a failure, not `ok:200` with null data
- [x] A prompt response is untouched by the timer
- [x] `timeoutMs: 0` restores the previous unbounded behaviour, as documented
- [x] The 20s default is pinned, so it cannot be retuned silently under consumers relying on the outbox
- [x] `DEFAULT_REQUEST_TIMEOUT_MS` is importable from `birko-web-core`
- [x] Revert split recorded by name (below)

## Outcome

Landed 2026-08-10. Playground `backport-smoke` 264 (was 255); `verify.mjs` 0 failing;
`device-fix-check` 68/68.

**Revert split** — the two halves are separable, and only one was in the original change:

| Reverted | Failing |
|---|---|
| the whole timeout | **6 of 9** |
| the body-read arm only | **2 of 9** — and *"a stalled BODY settles"* still **passes**, because the timer alone is what stops the hang |
| — | the 3 back-compat checks stay green in both |

Three things worth carrying past this client:

- **A fix that removes a hang can leave the wrong answer behind it.** Once the request settles, the
  symptom everyone was chasing is gone; nothing about a green screen says the write was silently
  discarded. When converting "never returns" into "returns", check what it now returns on *every*
  path, not just the one named in the report.
- **The suite reported 255/255 GREEN while the build had actually FAILED.** `verify.mjs` ran the
  previous bundle, so a stale-bundle pass is indistinguishable from a real one *except for the check
  count*. Read the build output; treat an unchanged total after adding checks as a failure.
- **A test for a never-settling promise must be bounded, not awaited.** Every assertion here races a
  timer, because a bare `await` turns a failing check into a hung suite that reports nothing at all.
  The stubs also reject with a real `DOMException` named `AbortError` and honour `init.signal`, so the
  client sees what a genuine aborted fetch produces rather than a shape invented to match the
  assertion.

## Out of scope

- Per-request (as opposed to per-client) timeout overrides — the public surface (`get`/`post`/`put`/
  `delete`) accepts no `RequestInit`, which is also why overwriting `signal` in `_fetch` is safe today.
  Revisit if a caller ever needs cancellation.
- Retry/backoff policy — that is `SyncManager`'s job and it already owns it.

## Human test plan

The original report came from a real device (a sleeping phone on a flaky link). The automated checks
stub `fetch`, so they prove the *handling*, not the platform behaviour that triggers it. If it needs
re-confirming on hardware: perform a write, sleep the screen mid-request, and check the action lands in
the outbox and syncs on wake.
