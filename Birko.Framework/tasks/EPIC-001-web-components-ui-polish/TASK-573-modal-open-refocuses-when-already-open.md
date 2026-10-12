---
id: TASK-573
parent: EPIC-001
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: []
pr: beafe5a
github-issue: null
jira-key: null
---

# b-modal's open() on an already-open modal steals focus and forgets the opener

## Context

A regression from TASK-568 (2026-10-12, not yet pushed), found by Presenter TASK-043's accessibility-tree trace.
Presenter calls `modal.open()` on **every re-render** while its pairing dialog is open (`_syncInfoModal`). Before
TASK-568, `open()`'s auto-focus found nothing, so that was harmless. Now `open()` focuses the first slotted control
each time it is called:

- Press Space on the dialog's **Copy audience link** button. The label changes to "Copied!", the page
  re-renders, `open()` runs again, and focus jumps back to the first control (the QR link). A keyboard user loses
  their place, and Narrator reads the link instead of "Copied!".
- `open()` also overwrites `_previousFocus` with the control inside the dialog, so the next `Esc` / `close()`
  returns focus to an element in a now-closed dialog (the body), not to the opener. This part predates TASK-568.

Calling `open()` on an open modal is a reasonable thing for a consumer to do (sync from state), so the component
should treat it as a no-op rather than every consumer guarding it.

## Acceptance criteria

- [x] `open()` on a modal whose `<dialog>` is already open does nothing: no `showModal()`, no `_previousFocus`
      overwrite, no auto-focus
- [x] A first `open()` behaves as TASK-568 left it (first slotted control; Close if none)
- [x] Playground `modal-focus-smoke`: with focus moved to a later control, a second `open()` leaves focus there,
      and `close()` still returns to the original opener. Both checks fail against the current code
      (only the focus check fails: Chrome's native `<dialog>` restore already returns focus to the opener, so the
      overwritten `_previousFocus` was masked there)
- [x] Presenter (TASK-043): Space on Copy audience link keeps focus on it, headless trace
- [x] Not breaking → PATCH

## Out of scope

- Presenter's `_syncInfoModal` calling `open()` on every update. Valid use once the component is idempotent

## Implementation plan

1. `src/layout/b-modal.ts` `open()`: return first if `#dlg` is already `open`. Comment the why (consumers sync
   from state on every render; a second call re-focused and overwrote the opener).
2. `Birko.Web.Playground/src/modal-focus-smoke.ts` section 1: after open, focus the last slotted control, call
   `open()` again, assert focus stays; then `close()` and assert focus is the opener. Red-verify against the
   current `open()`.
3. Full `verify.mjs`; rebuild Presenter and re-run its `dialog-ax.cjs` trace.
4. Components `CLAUDE.md`: one line added to the TASK-568 note.

## Human test plan

- [x] Presenter: press `i`, `Tab` to Copy audience link, press Space: "Copied!" and focus stays on the button;
      Narrator reads the new label or stays put. `Esc` returns to where focus was before `i`

## Progress log

- 2026-10-12 — filed and picked (user: "yes"), found by Presenter TASK-043's trace
- 2026-10-12 — smoke checks added to `modal-focus-smoke` section 1 first. **Red-verified:** with the current `open()`, "a second open() leaves focus where it is" fails (focus went to the link); the close() check passes, masked by native restore
- 2026-10-12 — `open()` returns early when `#dlg` is already open. `modal-focus-smoke` 12/12, full `verify.mjs` 0 failing. Presenter rebuilt: Space on Copy audience link leaves focus on the button, now named "Copied!"; the six Esc-return variants unchanged; TASK-043's key trace unchanged
- 2026-10-12 — human test (user, Presenter TASK-043): passed. Committed Web `beafe5a`, Playground `add8ecf`. Done. **Not pushed**: CHANGELOG, version bumps and tag are the owner's, at push
