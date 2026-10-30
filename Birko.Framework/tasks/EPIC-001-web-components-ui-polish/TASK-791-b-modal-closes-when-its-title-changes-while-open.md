---
id: TASK-791
parent: EPIC-001
status: todo
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
