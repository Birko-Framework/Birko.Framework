---
id: TASK-977
parent: EPIC-018
feature: FEATURE-018
priority: P1
status: verify
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A dead write is invisible on the sync chip, and an interrupted upsert dies though re-sending it is harmless

## Context

Found by Reps (its TASK-196, P1; its FEATURE-001 D19, approved 2026-11-24). Since TASK-581 a queued write can become
`dead`, and `SyncManager` already exposes `getDead()`, `retry()`, `discard()` and `onDead`. Two gaps stop a consumer
from using that without forking framework code:

1. **`<b-sync-status>` cannot show a dead write.** It reads `pendingCount`, which excludes `dead`, so the chip clears
   the moment a write dies. The screen then says "everything saved" about a write the server never got.
   `ActionQueue` has no observable dead count either: `getDead()` is a one-off async read.
2. **An interrupted POST dies even when re-sending it is harmless.** `_recover` puts an abandoned `syncing` action
   back in line only for `PUT`, `DELETE`, and a `POST` declaring `idPinned`. A POST to an **upsert** endpoint (the
   body carries the client-minted id, and the server overwrites on a second arrival) is just as safe, but
   `idPinned` cannot declare it: that flag asserts what the endpoint's **409** means, and an upsert never returns
   409. Reps' session and set writes are such POSTs, so a phone killing the app mid-send leaves a set `dead`.

Also confirm, rather than change: a consumer passing `maxRetries: Infinity` gets a write that never dies on a network
error or a 5xx and still backs off to `retryMaxMs`. That is how Reps implements "never give up on a network failure".

## Acceptance criteria

- [x] `ActionQueue` exposes a `deadCount` kept in step with `pendingCount` (same refresh, same change notification),
      and `SyncSource` gains it as an optional member, so an existing source without it still binds.
- [x] `<b-sync-status>` shows a dead state when `deadCount > 0`, online or offline, ahead of the pending states:
      a localizable `deadLabel` (default `'{count} not saved'`), the danger colour in both themes, rendered as a
      button that dispatches a bubbling, composed `b-sync-dead` event. With no dead write it behaves exactly as today.
      _Changed while building: the button stands **beside** the offline/syncing chip rather than ahead of it (a
      code-review finding: an offline device with writes waiting must still say so), and it has its own `dead` class,
      so `.chip` stays one element (Reps' existing specs read the chip by class and broke on a second one)._
- [x] `ActionMetadata` / `ActionMeta` gain `replaySafe`: on a `POST` or `PATCH` it tells `_recover` that re-sending
      cannot apply the write twice, so an interrupted one is put back in line (`failed`, due now, retry count kept)
      instead of made `dead`. The doc comment separates it from `idPinned` (409 semantics) and says when not to set it.
- [x] `maxRetries: Infinity` is asserted: twelve-plus failures leave the action `failed`, never `dead`, and its delay
      stays at `retryMaxMs`.
- [x] Tests in Playground (`sync-interrupt-smoke` for `replaySafe`, `sync-retry-smoke` for `Infinity`,
      `backport-smoke`'s chip case for the dead state and event), each proven able to fail with its change reverted.
- [x] README (Core and Components) document `deadCount`, `replaySafe` and the chip's dead state; CHANGELOG records them.

## Out of scope

- The list of dead writes with Retry / Discard: Reps builds it (TASK-196) on `getDead` / `retry` / `discard`. Lift it
  here if a second consumer needs one.
- Changing the default `maxRetries`: a consumer chooses its own.

## Human test plan

- [x] In the Playground gallery's `b-sync-status` card (or a page binding a fake source with a dead count), check the
      dead state in light and dark themes: readable red, visibly a button, keyboard-focusable, and the event fires on
      Enter as well as on a tap.
  - _Run 2026-11-24 on Android (Reps on a Mi A2 Lite, Chrome 152, a real dead write):_ the button reads
    "1 neuložené", red and readable in dark and in light, sits beside the other top-bar controls, and a real tap
    dispatched `b-sync-dead` (Reps opened its list).
  - _Keyboard, run 2026-11-25 in desktop Chromium (Reps on its local Testing host, a real server-refused set):_ real
    Tab presses reach `button.dead` in document order, after the brand link; Enter opens Reps' list, Space opens it
    too, and Escape returns focus to the button.
    **Found:** the focus indicator is faint. It is `outline: none` plus a 3px box-shadow at 15% red, so on the pink
    chip in light mode the focused and unfocused states are hard to tell apart; `b-button` shows a clear ring
    beside it. Not a failure of this step, but it is below the 3:1 a focus indicator needs, so it needs a
    follow-up task.

## Implementation plan

1. `action-queue.ts`: a `_deadCount` signal and `deadCount` getter, set in `_refreshCount` from the same `getAll()`.
   `_notifyChange` stays the single notification (the chip re-reads both counts on it).
2. `b-sync-status.ts`: `SyncSource.deadCount?: number`; in `paint()` a `dead` state first; the chip element becomes a
   `<button>` only in that state (or always a button, `disabled`-free, with `tabindex` only when dead: pick what keeps
   the idle chip out of the tab order); `deadLabel`; `b-sync-dead` on click. Danger tokens for both themes.
3. `action-queue.ts` + `api-client.ts`: `replaySafe?: boolean` on both metadata types, carried through enqueue the
   way `idPinned` is. `sync-manager.ts` `_recover`: `safe` includes `(POST || PATCH) && replaySafe`.
4. Playground: `sync-interrupt-smoke` case (an abandoned `syncing` POST with `replaySafe` comes back `failed`, one
   without still dies); `sync-retry-smoke` case for `Infinity`; `backport-smoke` chip case extended with a dead count.
   Prove each red by reverting its change.
5. README + CHANGELOG (joins the unreleased v0.5.3 entry, alongside TASK-973 and TASK-976).

## Progress log

- 2026-11-24 — Built from Reps (TASK-196). Merged into Birko.Web main **`3ebe9fd`** (commit `78beb83`), part of
  v0.5.3; the unpushed `v0.5.3` tag moved to the merge. (An earlier `e977ad0` was made while this session was still
  editing the files and was undone before any merge; it is in no branch.) The framework session folded the separate
  v0.5.4 into the unpushed v0.5.3 (one tag per push) and added the Components README section for `b-sync-status`.
  - Playground: `sync-interrupt-smoke` 25/25, `sync-retry-smoke` 55/55, `backport-smoke` 497/497, `verify.mjs` 0
    failing. **Proven red** with the four framework files reverted: `sync-interrupt-smoke` 21/25 (`deadCount`,
    `replaySafe`), `sync-retry-smoke` 54/55 (`deadCount`), `backport-smoke` failing at the dead chip. The
    `maxRetries: Infinity` checks pass either way, by design: they assert existing behaviour; case 2 is their control.
  - Reps consumes it (`dead-writes.spec.ts`, full suite 252/252).
  - Human test step not run yet: it can be run on Reps' phone pass for TASK-196, which produces a real dead write.
  - Re-run by the framework session on the merged code (build "✓ Built"): `verify.mjs` 0 failing (the three smokes as
    above), keyboard-check 244/244, a11y-name OK (43), rerender-leak PASS, Core and Components `tsc` clean.
