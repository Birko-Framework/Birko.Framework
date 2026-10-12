---
id: TASK-568
parent: EPIC-001
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: []
pr: a6fff5e
github-issue: null
jira-key: null
---

# b-modal's focus trap ignores slotted content, so Tab is stuck on Close

## Context

Found by Presenter's Narrator pass (Presenter TASK-029 → TASK-037, 2026-10-12). In every Presenter
dialog, `Tab` does nothing: focus opens on the header's **Close** and stays there. Reproduced headlessly
in all three of Presenter's `b-modal`s, at four Tab presses each.

The cause is in `src/layout/b-modal.ts`:

- **The focus trap** (`onUpdated`, the `keydown` listener) collects focusables with
  `dlg.querySelectorAll(…)`. That searches the shadow tree only. The dialog's real content is
  **slotted** light DOM, so the list holds just `.close-btn`. With `first === last`, every `Tab` is
  `preventDefault`ed and refocuses Close.
- **`open()`'s auto-focus** runs the same shadow-only query with `:not(.close-btn)`, finds nothing, and
  leaves focus wherever `showModal()` put it (Close).
- A slotted `b-button` adds a second boundary: its real `<button>` is inside its own shadow root, so even
  a light-DOM query on `button` would miss it.

This affects every consumer that puts a control in a `b-modal`: a form, a confirm, a link. A keyboard or
screen-reader user cannot reach anything but Close. It is P1 because it fails WCAG 2.1.1 Keyboard. Check
whether `b-confirm-dialog` and any other component with its own trap share the code.

## Acceptance criteria

- [x] ~~The trap's focusable list is the dialog's content in flat-tree order … and wrap at both ends~~
      **Changed 2026-10-12 (user):** the custom trap is **removed**. `showModal()` already makes the page
      behind inert and tabs through slotted content in flat-tree order, as `b-confirm-dialog` and `b-drawer`
      rely on. Tab and Shift+Tab reach every control in the dialog. After the last one, focus may go to the
      browser's own UI and then back into the dialog, never to the page behind
- [x] `open()` focuses the first focusable **content** element, slotted included. It falls back to Close
      only when the dialog has no other focusable
- [x] ~~Comparing against the active element works across shadow boundaries~~ Moot once the trap is gone
- [x] A test or Testing/playground check opens a modal with a slotted link, input and `b-button` and
      asserts the Tab order. The same check fails against the current code
      (in-page it asserts open() focus and that Tab is never cancelled; real Tab order traced headlessly in Presenter)
- [x] `b-confirm-dialog` and any other component with a focus trap get the same fix, or a recorded reason
      why they don't need it. Found 2026-10-12: `b-confirm-dialog` and `b-drawer` have no custom trap.
      `b-tour` traps Tab over its **own** shadow buttons only (no slot, and it is not a `<dialog>`), so its
      trap is correct and stays
- [x] Not breaking → PATCH

## Out of scope

- Consumer re-tests beyond Presenter TASK-037

## Implementation plan

1. `src/layout/b-modal.ts` `onUpdated()`: delete the `keydown` Tab trap. Keep the backdrop click, Close
   and `close` event wiring.
2. `open()`: after `showModal()`, focus the first focusable **content** element, walking the flat tree:
   the body and footer slots' `assignedElements({ flatten: true })`, descending into open shadow roots (a
   `b-button`'s inner `<button>`). Skip disabled, `tabindex="-1"` and hidden elements. If there is none,
   leave the native focus (Close).
3. Regression check: `Birko.Web.Playground/src/modal-focus-smoke.ts`, wired in `app.ts` and in
   `verify.mjs`'s suite list. A modal holding a slotted link, `b-input` and `b-button`:
   - on open, focus lands on the link, not Close
   - a Tab `keydown` on Close or on the last content control is **not** `defaultPrevented` (the old trap
     cancelled it and refocused Close)
   - Esc / `close()` still returns focus to the opener

   Red-verify by restoring the old `onUpdated`/`open` and confirming the suite fails. Other suites run in
   the same page, so the modal is open for as short a time as possible, and focus checks retry
   (`smoke-focus.ts`).
4. Real-key check: a headless Puppeteer trace in Presenter's three dialogs (TASK-037), with real `Tab` presses.
5. Docs: `ACCESSIBILITY.md` Dialog row says "native modal focus" for `b-modal`; a Components `CLAUDE.md`
   note on why there is no custom trap (it ignored slotted content).

## Human test plan

- [x] In a playground modal holding a link, an input and a `b-button`: focus opens on the link, `Tab`
      visits the input and the button, and then Close (possibly via the browser's own UI), never the page
      behind. `Shift+Tab` goes backwards
- [x] In Presenter (TASK-037): `i`, `a` and `?` dialogs, with Narrator on, `Tab` reaches every control

## Progress log

- 2026-10-12 — picked. Decision (user): remove the custom trap and rely on native `showModal()`, as `b-confirm-dialog` and `b-drawer` do, rather than fix the trap
- 2026-10-12 — implemented in `b-modal.ts`: trap removed; `open()` walks the body/footer slots' `assignedElements({ flatten: true })` into open shadow roots, skipping disabled, hidden and `tabindex="-1"`. `tsc --noEmit` clean. Docs: Components `CLAUDE.md` Recent Updates note, `ACCESSIBILITY.md` Dialog row
- 2026-10-12 — guard: `Birko.Web.Playground/src/modal-focus-smoke.ts` (10 checks), serialised after the suites a modal disturbs (`gallery-smoke` gained a `__gallerySmokeDone` flag for it). Full `verify.mjs`: every suite passes, 0 failing checks. **Red-verified:** with the old `b-modal.ts` restored, 5/10 fail (Tab and Shift+Tab on Close cancelled; focus on Close, not the first control)
- 2026-10-12 — real keys, headless, in Presenter (TASK-037): `i` dialog Tab: link → Copy → browser UI → Close → link; Shift+Tab reverses; `a`: link → browser UI → Close; Esc returns focus to the opening button
- 2026-10-12 — code review: `firstFocusable` skipped a `display: contents` wrapper (no boxes) and so its children; it now descends into one. Smoke check added, 11/11; full `verify.mjs` 0 failing
- 2026-10-12 — human test (user, Presenter TASK-037): Tab reaches every control in the `i`, `a` and `?` dialogs, through the browser UI after the last; Esc returns focus to the button that opened the dialog (after the `i` shortcut, to where focus was, as the APG pattern says). Signed off; done. Committed Web `a6fff5e`, Playground `3a95b30`. **Not pushed**: CHANGELOG, version bumps and tag are the owner's, at push
