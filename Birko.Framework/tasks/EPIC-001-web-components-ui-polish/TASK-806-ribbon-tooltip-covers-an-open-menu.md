---
id: TASK-806
parent: EPIC-001
feature: null
status: done
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

- [x] Reproduced first, in the Playground with a real mouse, and the mechanism named (below).
- [x] While a menu is open no tooltip shows over it, whether opened by mouse or Enter on the ▾ part.
- [x] Same for a plain menu button, a gallery grid and a folded group's flyout (one rule for all three popups); a
      command inside an open flyout keeps its tooltip.
- [x] After the menu closes, hovering a command shows its tooltip as before.
- [x] A real-input check that fails without the fix: `category-ribbon-check.mjs` (on Birko.Web main: Sales'
      description over Catalogue's open menu).
- [x] Symbio told when it is on Birko.Web main (its dev server watches it). Symbio then rebuilds its bundle and adds
      an assertion to its `tests/ui-e2e/ribbon-module-menu.spec.ts` (E-Shop's ▾ open, no tooltip over the menu); its
      TASK-1027 does not wait for this one and names it as the owner (Symbio, 2026-11-03).

## Out of scope

- The icon-entity difference (buttons put `icon` into innerHTML, the menu escapes it): Symbio decodes its icons
  itself, and escaping in the menu is right.

## Human test plan

- [x] In the Playground's "Category ribbon (Shell)" card, hover Sales until its tooltip shows, then click its ▾.
      Expected: the tooltip is gone and the menu's first choices are visible; moving over the menu, or over another
      module while the menu is open, does not bring one back. **PASS (the user, 2026-11-03).**
- [x] Symbio, Commerce tab, E-Shop's ▾: the same. Symbio's guard (`ribbon-module-menu.spec.ts`: Commerce tab click,
      which re-renders, then E-Shop's ▾; no tooltip at once and 1 s later) was red 3 of 3 on v0.4.1 (also at 2 s and
      5 s), green on v0.4.2. Hovering the main part before the ▾ click did not reproduce it; only the click path did,
      which fits the re-render mechanism. Symbio has not seen the menu close after a ▾ click on re-render.

## Mechanism (measured 2026-11-03)

Hover, ▾, pointerdown and the menu's beforetoggle all hid the tooltip, and the plain sequence never showed it
(hover main, move onto ▾, click: hidden at +0/100/400/1000 ms, both sizes, gradual or one-step moves). What showed
it was a *new* mouseover while the menu stayed open: the panel's hover handler had no idea a menu was up. Symbio's
case is that, without moving the mouse: its shell navigates on a category-tab click and re-renders the ribbon
(`setTabs`) after the ▾ click, so the element under the still pointer changes, a mouseover fires, and the tooltip,
"warm" from the hide 300 ms earlier, shows at once (Symbio's tooltip was E-Shop's description, not "More options").
Reproduced deterministically: open Catalogue's ▾, move over Sales → Sales' tooltip over the open menu.

Seen on the way, not fixed: a re-render ~50 ms after a ▾ click closed the just-opened menu (300 ms later it
survived). Symbio's menu stayed open, so it is not this task's; file it if it shows in a consumer.

## Implementation plan

`_showTip` refuses while `.ribbon-menu`, `.ribbon-flyout` or `.ribbon-gallery-grid` is `:popover-open`, unless the
command is inside that popup. Checks in category-ribbon-check: hover over another command with a menu open, the
tooltip back after it closes, a menu opened by Enter. Mutation: refusing inside an open flyout too fails
keyboard-check (flyout tooltip in the top layer, first Escape hides the tooltip).

## Built (2026-11-03), on branches with TASK-808, not merged

Birko.Web `task/TASK-808` c5a157a (v0.4.2), Playground `task/TASK-808` 10ddd9d. category-ribbon-check 19/19 with the fix, 16/19 on
Birko.Web main.

## Merged (2026-11-03)

Birko.Web main 3260449, tagged **v0.4.2** and pushed; Playground main 6d27396, pushed. Symbio told before and after.
Worktrees and branches removed.
