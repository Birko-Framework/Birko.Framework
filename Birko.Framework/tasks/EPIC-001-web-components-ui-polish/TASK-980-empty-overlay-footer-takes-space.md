---
id: TASK-980
parent: EPIC-001
feature: null
status: verify
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

- [x] `<b-modal>` and `<b-drawer>` with nothing slotted into `footer` show no footer: it takes no space and draws no
      border.
- [x] A footer slotted in later (slotchange) appears, and one removed later disappears.
- [x] A footer with content looks exactly as today.
- [x] The dead `:empty` rule in `b-drawer` is replaced by the working mechanism, shared rather than duplicated.
- [x] Playground check proving the above for both components, red before the change.

## Evidence

Birko.Web `task/TASK-980` aa62a21 (joins the unpushed v0.5.3), Playground 58901c6. All from builds that printed
"✓ Built". `hideEmptyFooter` (dom-utils) sets `hidden` on `.overlay-footer` while its slot has nothing assigned and
re-checks on `slotchange`; the shared sheet's `.overlay-footer[hidden] { display: none }` wins over its `display: flex`.
Both components call it from `onUpdated`; b-drawer's `:empty` rule is gone.
- `overlay-footer-check` 8/8 on both components; **red** 4/8 against Birko.Web main (an empty footer: a 29 px band
  with its border; removing a late footer left it showing).
- verify 0 failing; keyboard-check 244/244; escape-in-modal 27/27; rerender-leak PASS; a11y-name OK (43);
  entry-points 433/433; toast-announce 7/7; Components `tsc` clean.

## Out of scope

- The footer's look when it has content.

## Human test plan

- [ ] Open a modal and a drawer with no footer (light and dark): the body's last line sits on the overlay's bottom
      edge padding, with no extra band or border line under it. Open one with a footer: unchanged.
