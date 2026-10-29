---
id: TASK-786
parent: EPIC-001
status: verify
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

- [x] The toast container is shown in the top layer **above** any open dialog, for example `popover="manual"` plus
      `showPopover()`. It is re-shown when a toast is added, so it lands after a dialog opened since the container was
      last shown, because the newest top-layer entry is the topmost one.
- [x] A toast's action button and link can be clicked while a modal is open.
- [x] The modal keeps working: focus stays in the dialog, Escape closes the dialog and not the toasts, and clicking the
      backdrop behaves as it does today.
- [x] No regression with no modal open: the toast's position, stacking order, offsets (`_bottomOffset`) and
      auto-dismiss are unchanged.
- [x] A Playground smoke that opens a `b-modal`, raises a toast, and asserts the toast is **on top**: `elementFromPoint`
      at the toast's centre returns the toast, or a node inside it. A text or `toBeVisible()` check cannot see this
      defect. Mutation-proven: the smoke fails with the old container.
- [x] CHANGELOG entry and package version bump.

## Out of scope

- Symbio's own guard for its block editor; Symbio files that on its side, and re-walks its TASK-731 after this ships.
- Other overlay components (`b-drawer`, dropdowns, tooltips) inside a modal. Check them while here, and file anything
  found as its own task.

## Resolution (2026-10-29)

Birko.Web `bd5d691` (v0.1.22, not pushed or tagged yet), Playground `44974d2`.

- **The suggested fix was falsified before it was built.** A `popover="manual"` on `<body>`, shown after a modal
  dialog, is drawn above it but is **still inert**: in Chromium and Firefox `elementFromPoint` returns the dialog, a
  trusted mouse click on its button does nothing, and `focus()` is refused. The acceptance criterion's own hit test
  would have failed it.
- **Built instead:** the toast container moves into the newest open modal dialog and is shown there as a popover (the
  dialog's subtree is the one part of the page that is not inert), and goes back to `<body>`, toasts and all, when
  that dialog closes. With no modal open it is unchanged: on `<body>`, not a popover. The browser does not expose the
  order of open modals, so the new `Birko.Web.Components/src/modal-stack.ts` keeps it, and all six components that
  call `showModal()` go through it (b-modal, b-drawer, b-confirm-dialog, the dialogs `busy` helper, b-gallery zoom,
  b-ribbon mobile menu). Recorded as a MANDATORY rule in Birko.Web.Components' CLAUDE.md.
- **Measured on the way:** Escape in Chromium closes the dialog right after `cancel` but fires `close` ~150 ms later
  (Firefox, and a `close()` call, fire it at once). The stack also re-checks one task after `cancel`, so the toast is
  back on `<body>` without that gap.
- **Behaviour change for tests:** while a modal is open, `document.getElementById('b-toast-system')` returns null (the
  container is in the dialog's shadow root). Playwright locators pierce it. The Playground's undo-delete-smoke used
  that query and failed when it overlapped the new suite; the two are now serialised. In CHANGELOG and API.md.
- **Not covered:** a `<dialog>` a consumer opens with its own `showModal()`. Symbio has one, the "write outcome
  unknown" overlay, which is meant to block the page, so toasts behind it are correct.
- **Other overlays inside a modal (out of scope check):** b-select / b-multi-select dropdowns and b-tooltip are
  popovers inside the modal's subtree, so they are not inert; b-drawer is now tracked. Nothing to file.

**Gate:** Playground `toast-modal-smoke` 16/16 in Chromium and Firefox, 10/16 on the old code (all 6 failures hit
`dialog#dlg`). A scratch run with trusted input in both engines: a mouse click on the toast's action runs it and the
modal stays open; Escape closes the modal and the toast is on `<body>` 60 ms later; a backdrop click still closes it.
verify.mjs 0 failing (three runs in a row), entry-points 409/409, keyboard-check 222/222, device-fix 140/140. tsc:
the one error is in `sync-interrupt-smoke.ts`, a file this change does not touch.

## Human test plan

1. Playground: Gallery → Layout → Modal → Open modal, then **Show a toast** in its footer (added for this test). The
   toast is readable above the backdrop.
2. Its action button (if any) works while the modal stays open.
3. In Symbio: Presentation → Pages → a page → add an Event list block, switch off "Only events that have not started
   yet", choose no type or venue, save. The refusal toast is readable on top of the block modal.
