---
id: TASK-980
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
related: [TASK-580, TASK-568]
findings: []
pr: null
github-issue: null
jira-key: null
---

# An empty modal or drawer footer still draws a band: padding and a top border under the content

## Context

Reported by Reps (its TASK-196 "not saved" sheet, a device pass on Android Chrome 152, 2026-11-24): a `<b-modal>`
with nothing in `slot="footer"` shows an empty band under its body, the footer's padding plus its top border, in both
themes.

`b-modal.ts` always renders `<div class="overlay-footer"><slot name="footer"></slot></div>`, and the shared
`.overlay-footer` sheet gives it padding and a border. `b-drawer.ts` renders the same footer and tries to hide it with
`.overlay-footer:empty { display: none; }`, which never matches: the div always holds its `<slot>` element, so it is
never `:empty`. (`b-confirm-dialog` always has buttons in its footer and is not affected.) `b-modal`'s `onUpdated`
already walks `.overlay-body slot, .overlay-footer slot` for slotchange, so it can tell when the footer slot has
assigned elements.

## Acceptance criteria

- [ ] `<b-modal>` and `<b-drawer>` with nothing slotted into `footer` show no footer: it takes no space and draws no
      border.
- [ ] A footer slotted in later (slotchange) appears, and one removed later disappears.
- [ ] A footer with content looks exactly as today.
- [ ] The dead `:empty` rule in `b-drawer` is replaced by the working mechanism, shared rather than duplicated.
- [ ] Playground check proving the above for both components, red before the change.

## Out of scope

- The footer's look when it has content.

## Human test plan

- [ ] Open a modal and a drawer with no footer (light and dark): the body's last line sits on the overlay's bottom
      edge padding, with no extra band or border line under it. Open one with a footer: unchanged.
