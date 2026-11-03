---
id: TASK-806
parent: EPIC-001
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-11-03
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon's tooltip stays over an open split or menu button's menu

Found by Symbio (TASK-1027, adopting v0.4.1), seen in a Playwright screenshot.

## Context

Repro in Symbio: Commerce tab, hover E-Shop's split button until its description tooltip shows below it, then click
its ▾. The menu opens under the still-visible tooltip, which covers its first choices (Dashboard / Orders / Cart).
Expected, as in Office: opening a command's menu hides its tooltip, and it does not come back while the menu is open.
Symbio expects the same for a plain menu button (`menu` without `split`).

The ribbon already hides the tooltip on the panel's `pointerdown` and on the menu's `beforetoggle`
(`Birko.Web.Components/src/nav/b-ribbon.ts` `_bindTooltipArea` and the menu trigger's wiring), so something re-shows it
after the menu opens: a candidate is the panel's `focusin` (shows the tip for a `:focus-visible` command) or the
hover path with the tooltip still "warm" (`_tipWarmUntil`). Not measured yet.

TASK-803 (v0.4.1) made every multi-page module in the Shell's category ribbon a split button with a description, so
this now shows on every module with two or more pages.

## Acceptance criteria

- [ ] Reproduced first, in the Playground with a real mouse (hover until the tip shows, then click ▾), and the
      mechanism named.
- [ ] Opening a split button's ▾ menu hides the command's tooltip; it stays hidden while the menu is open, whether
      opened by mouse, Enter / Alt+↓ on the ▾ part, or a key tip.
- [ ] Same for a plain menu button and a folded group's flyout.
- [ ] After the menu closes, hovering a command shows its tooltip as before.
- [ ] A real-input check that fails without the fix (mutation), in `category-ribbon-check.mjs` or `keyboard-check.mjs`.
- [ ] Symbio told when it is on Birko.Web main (its dev server watches it). Symbio then rebuilds its bundle and adds
      an assertion to its `tests/ui-e2e/ribbon-module-menu.spec.ts` (E-Shop's ▾ open, no tooltip over the menu); its
      TASK-1027 does not wait for this one and names it as the owner (Symbio, 2026-11-03).

## Out of scope

- The icon-entity difference (buttons put `icon` into innerHTML, the menu escapes it): Symbio decodes its icons
  itself, and escaping in the menu is right.

## Human test plan

- [ ] In the Playground's "Category ribbon (Shell)" card, hover Sales until its tooltip shows, then click its ▾.
      Expected: the tooltip is gone and the menu's first choices are visible; moving over the menu does not bring it back.
- [ ] Symbio, Commerce tab, E-Shop's ▾: the same.

## Implementation plan

_Populated by `/tasks plan TASK-806` — leave empty until then._
