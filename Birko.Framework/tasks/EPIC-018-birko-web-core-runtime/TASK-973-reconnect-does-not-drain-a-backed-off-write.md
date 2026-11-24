---
id: TASK-973
parent: EPIC-018
feature: FEATURE-018
priority: P1
status: done
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Coming back online does not drain a write that is backing off: it waits out the backoff and the next interval tick

## Context

Found by Reps (its TASK-194, P1): its `offline-resume` E2E spec has failed since TASK-581 shipped backoff, and the
failure cascades into twelve more specs.

`SyncManager` listens for `online` and calls `sync()`. Since TASK-581, `_drain` skips any `failed` action whose
`nextAttemptAt` is still in the future. So a write that failed while the network was dead, then gets a reconnect, is
**not** sent on the reconnect: it waits for its backoff to expire **and** for the next 30 s interval tick. Measured in
Reps (Chromium, 2026-11-24): a set queued on a dead network failed once more on an offline reload
(`retries 1`, next attempt +5 s); the `online` sync 2-3 s later skipped it, and it landed about 30 s after the reload.
After a long dead-network spell the delay reaches `retryMaxMs` (15 min) plus the interval.

Backoff exists so a write the **server** keeps refusing is not re-sent every cycle. A reconnect is new evidence about
the **network**, which is what made the write fail in the first place, so it should make the write due again. The
retry count is kept, so a write that keeps failing after the reconnect still backs off, and still dies at the cap.

## Acceptance criteria

- [x] On the `online` event, every `failed` action's `nextAttemptAt` is cleared before the sync runs, so it is sent on
      that sync; `retries` and `lastError` are unchanged.
- [x] A `dead` action is not revived by `online`: only `retry()` does that.
- [x] A write that fails again after the reconnect backs off from its kept retry count (it does not restart at
      `retryBaseMs`).
- [x] A plain `sync()` call (the interval tick, a consumer's manual call) still honours `nextAttemptAt`.
- [x] Tests in Playground `sync-retry-smoke`, proven able to fail with the change reverted.
- [x] README (Core) states the reconnect rule beside the backoff options; CHANGELOG records the behaviour change.

## Out of scope

- A failed-write UI in any consumer (Reps files its own).
- Detecting a dead-but-connected network recovering without an `online` event: the interval tick still covers that.

## Human test plan

N/A: the transition is asserted by the smoke tests with a fake clock, and Reps' `offline-resume` E2E spec exercises it
end to end in a real browser.

## Implementation plan

1. `sync-manager.ts`: the `online` handler first calls a private `_makeFailedDue()` that reads `getAll()`, clears
   `nextAttemptAt` on each `status === 'failed'` action and writes it back through `queue.update`, then calls `sync()`.
   A `failed` action held behind a `syncing` one keeps its place, because `_drain`'s ordering is unchanged.
2. `sync-retry-smoke.ts`: a new case. A PUT fails (`retries 1`, next +1000), `dispatchEvent(new Event('online'))`
   before its time sends it again; it fails again and backs off +2000 (retries kept). A dead action is not re-sent on
   `online`. A plain `sync()` before its time still sends nothing (covered by case 1, asserted again here).
3. Prove red: revert step 1, run the case, see it fail; restore.
4. README + CHANGELOG lines.

## Progress log

- 2026-11-24 — Done in **Birko.Web v0.5.3** (tag prepared locally; push and tag push are the owner's call).
  - `SyncManager`'s `online` handler runs `_makeFailedDue()` (re-reads each action before writing, so a write a running
    drain already moved to `syncing` is not put back in line), then `sync()`; a failure to clear still syncs.
  - Playground `sync-retry-smoke` case 6: 50/50. **Proven red:** with the handler reverted to a bare `sync()`, 47/50,
    the three reconnect checks failing. `sync-interrupt-smoke` 21/21 throughout.
  - Reps: `offline-resume.spec.ts` passes 3/3 (2-3 s; it timed out at 15 s before), and its full E2E suite 249/249.
  - `verify.mjs` across three runs failed one or two unrelated, timing-bound checks each time, a different one per
    run (`backport-smoke` TASK-133 focus, `carousel-smoke`, `undo-delete-smoke`); TASK-797 records the suite as flaky
    on this machine.
  - Code review (inline, small diff): one finding fixed, the unguarded promise chain that would have skipped the
    reconnect sync entirely on an IndexedDB error.
