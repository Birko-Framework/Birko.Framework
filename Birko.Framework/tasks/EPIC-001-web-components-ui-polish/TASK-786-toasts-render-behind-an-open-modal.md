---
id: TASK-786
parent: EPIC-001
status: todo
priority: P1
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A toast raised while a modal is open renders behind the modal

## Context

Found by the user on 2026-10-29 while walking Symbio TASK-731 (the block editor's validation refusal). The refusal's
message reached the toast, in the right language, but the toast sat **behind the open modal and its backdrop**, so the
author could not read it.

**Mechanism (read from the source, not yet measured in a Playground smoke):**
- `b-modal` (`layout/b-modal.ts:110`) opens a native `<dialog>` with `showModal()`. That puts the dialog and its
  `::backdrop` in the browser's **top layer**.
- `b-toast` (`feedback/b-toast.ts:80`) appends a `position: fixed` container to `document.body` with
  `z-index: var(--b-z-toast, 500)`. Nothing in the normal stacking context renders above the top layer, whatever its
  `z-index`.
- `showModal()` also makes everything outside the dialog **inert**, so a toast with an action button or a link could not
  be clicked even if it were visible.

**Scope:** every toast raised while any `b-modal` is open, in every consumer. In Symbio that is every save, error or
success message a form inside a modal reports through a toast.

**Why the consumer guard missed it:** Symbio's browser test read the toast's text and asserted `toBeVisible()`.
Playwright's visibility does not consider occlusion, so a toast covered by the dialog still passes.

## Acceptance criteria

- [ ] The toast container is shown in the top layer **above** any open dialog, for example `popover="manual"` plus
      `showPopover()`. It is re-shown when a toast is added, so it lands after a dialog opened since the container was
      last shown, because the newest top-layer entry is the topmost one.
- [ ] A toast's action button and link can be clicked while a modal is open.
- [ ] The modal keeps working: focus stays in the dialog, Escape closes the dialog and not the toasts, and clicking the
      backdrop behaves as it does today.
- [ ] No regression with no modal open: the toast's position, stacking order, offsets (`_bottomOffset`) and
      auto-dismiss are unchanged.
- [ ] A Playground smoke that opens a `b-modal`, raises a toast, and asserts the toast is **on top**: `elementFromPoint`
      at the toast's centre returns the toast, or a node inside it. A text or `toBeVisible()` check cannot see this
      defect. Mutation-proven: the smoke fails with the old container.
- [ ] CHANGELOG entry and package version bump.

## Out of scope

- Symbio's own guard for its block editor; Symbio files that on its side, and re-walks its TASK-731 after this ships.
- Other overlay components (`b-drawer`, dropdowns, tooltips) inside a modal. Check them while here, and file anything
  found as its own task.

## Human test plan

1. Playground: open a modal, trigger an error toast from a button inside it. The toast is readable above the backdrop.
2. Its action button (if any) works while the modal stays open.
3. In Symbio: Presentation → Pages → a page → add an Event list block, switch off "Only events that have not started
   yet", choose no type or venue, save. The refusal toast is readable on top of the block modal.
