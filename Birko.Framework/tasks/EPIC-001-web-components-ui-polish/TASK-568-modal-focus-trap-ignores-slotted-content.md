---
id: TASK-568
parent: EPIC-001
feature: null
status: todo
priority: P1
assignee: null
created: 2026-10-12
depends-on: []
blocks: []
findings: []
pr: null
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

- [ ] The trap's focusable list is the dialog's content in **flat-tree order**: shadow children, slotted
      elements (`slot.assignedElements({ flatten: true })`), and focusables inside open shadow roots of
      those elements (e.g. `b-button`'s inner `<button>`). Tab and Shift+Tab cycle through all of it
      and wrap at both ends
- [ ] `open()` focuses the first focusable **content** element, slotted included. It falls back to Close
      only when the dialog has no other focusable
- [ ] Comparing against the active element works across shadow boundaries (walk
      `shadowRoot.activeElement`), so wrapping triggers on a slotted `b-button`
- [ ] A test or Testing/playground check opens a modal with a slotted link, input and `b-button` and
      asserts the Tab order. The same check fails against the current code
- [ ] `b-confirm-dialog` and any other component with a focus trap get the same fix, or a recorded reason
      why they don't need it
- [ ] Not breaking → PATCH

## Out of scope

- Switching to the native `<dialog>` focus behaviour wholesale: a larger change, file it separately if wanted
- Consumer re-tests beyond Presenter TASK-037

## Human test plan

- [ ] In a playground modal holding a link, an input and a `b-button`: `Tab` visits each, then Close, then
      wraps; `Shift+Tab` goes backwards
- [ ] In Presenter (TASK-037): `i`, `a` and `?` dialogs, with Narrator on, `Tab` reaches every control
