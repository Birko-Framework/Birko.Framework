---
id: TASK-791
parent: EPIC-001
status: done
priority: P1
assignee: ai
created: 2026-10-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Changing an open b-modal's title closes it

## Context

Measured by the Symbio session on 2026-10-30 (Symbio TASK-1010, blocked on this by the user's ruling "framework
only"; no local workaround).

`b-modal` (`Birko.Web.Components/src/layout/b-modal.ts`) observes `title` (`:10`), so a title change runs `update()`.
Its `render()` emits `<dialog id="dlg" …>` without `open` (`:76`), and BaseComponent's morph removes every attribute
the fresh render lacks (`Birko.Web.Core/src/base/base-component.ts:384`), so it strips `open` from the live dialog.
**Measured in Symbio:** re-rendering the block editor while its modal was open (title "+ Add Block" → "+ Pridať blok")
left `dialog.open === false`; without the title change it stayed open. Any consumer whose modal title depends on
state can hit it, and the locale fix (TASK-790) makes it more likely.

Removing `open` from a dialog shown with `showModal()` also leaves `modal-stack.ts` thinking it is open, so toasts
would still be homed inside it (TASK-786).

## Fix direction

Keep the dialog open across its own re-render (and keep focus and scroll): e.g. preserve `open` in the morph for a
dialog shown modally, or have b-modal render the state it is in. Check b-drawer, b-confirm-dialog and the other
`modal-stack` users for the same shape (any observed attribute that re-renders an open dialog).

## Steps

1. Playground smoke: open a b-modal, focus a field inside, change `title` (and each other observed attribute); assert
   still open, still modal (top layer, backdrop), focus and scroll kept, `topModal()` correct. Red today.
2. Fix; the same checks for b-drawer / b-confirm-dialog if they share it; mutation-prove.
3. verify.mjs and gates; CHANGELOG, version bump.
4. Tell the Symbio session (TASK-1010).

## Human test plan

Playground: open the Modal card's modal, type into a field, trigger a title change (a button or a locale switch);
the modal stays open with the text and focus kept, and Escape still closes it. Symbio: open the blocks editor's
Add Block modal and switch the locale; it stays open (Symbio TASK-1010).

## Resolution (2026-10-30, started at the user's word via the Symbio session)

- **Fix, in the morph (Birko.Web.Core `_morphNode`):** an attribute the fresh render lacks is removed, except `open`
  on a `<dialog>`, which `show()` / `showModal()` own. No framework render controls a dialog's `open` (grepped:
  Components, Shell). One fix covers b-modal, b-drawer (modal and not), b-confirm-dialog and consumers' own dialogs.
- **Red, measured:** after a `title` / `size` / `message` change the dialog had `open === false` yet still matched
  `:modal`: half-closed, the page behind still inert. Focus left the typed field.
- **Proof:** `locale-switch-check.mjs`, TASK-791 half: b-modal (title, size), b-drawer modal and non-modal (title),
  b-confirm-dialog (title, message): stays open, modal where it should be, the same `<dialog>`, focus kept in the
  field, the new title shown, `close()` still closes. 15/26 before the fix (with TASK-790), 30/30 after.
- Committed in Birko.Web dc59ddd (released as v0.1.23, pushed and tagged 2026-10-30) and Playground 653375c (with TASK-790).

**Human test, Playground (2026-10-30, the user, v0.1.23 in Chrome):** B (open modal, title changed from the console: stays open, new title, Escape / x close) **ok**.

**Symbio walk (2026-10-30, Symbio TASK-1010 on v0.1.23):** the block editor's modal stays open through a locale switch that changes its title, and an unsaved edit is kept. **PASS.** Closed by the user 2026-10-30.
