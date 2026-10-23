---
id: TASK-735
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: collapse by double-clicking a tab, and Ctrl+F1

## Context

FEATURE-019 D6 (approved by the user 2026-10-22): Double-clicking a tab collapses or expands the ribbon, and Ctrl+F1 does the same, as in Office.

Only the ▲ button collapses the panel today.

## Acceptance criteria

- [x] Double-click on a tab toggles expanded (and emits expand as the button does); a single click still selects
- [x] Ctrl+F1 toggles anywhere in the app (decided by the user, Q1); an attribute switches it off or rebinds it, and a
      key press the app already handled (`defaultPrevented`) is left alone
- [x] Documented, and the shortcut shown in the collapse button's tooltip
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Double-click tabs and press Ctrl+F1: the ribbon collapses and comes back, pinned state respected

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **`toggleRibbon()`** switches between pinned (panel always shown) and tabs only (unpinned and collapsed; the panel
  opens over the page when a tab is used), emitting `pin` / `expand` as before. That is Office's "collapse the ribbon".
  - The criterion said "toggles expanded", but that undoes itself on a collapsed ribbon: the double-click's own first
    click expands it again. So both triggers toggle the pinned state instead.
  - A panelless tab ignores the double-click, and tabs-only mode ignores both triggers.
- **Ctrl+F1 works anywhere in the app** (decided by the user, Q1).
  - It is a `window` keydown listener, added in `onMount` and removed in `onUnmount`.
  - It listens in the bubbling phase and skips `defaultPrevented` presses, so an app's own handler wins.
  - `collapse-shortcut` rebinds it (`Ctrl+Shift+F1`) or turns it off (`none`). The collapse button shows it in its
    title and as `aria-keyshortcuts`.
- **Gate.** Playground `keyboard-check.mjs` 93/93, with 11 new checks using a real double-click and keys.
  - The checks: double-click collapses and restores, `pin` events, a single click only selects, Ctrl+F1 from outside
    the ribbon both ways, an app handler that prevented it, `none`, rebinding, the button names the shortcut, and no
    listener left after removal.
  - 5 of them fail on the TASK-739 ribbon. `verify.mjs` clean; entry-points 409/409; `tsc` clean.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.

## Follow-up: D6 changed by the user (2026-10-23)

- **No more "pinned, only tabs" ribbon.** Collapsed and unpinned are one state, as in Office: collapsing unpins, and
  pinning opens the panel. Before, a pinned ribbon could show only its tabs with nothing open, which the user found
  and read as broken.
- **One control at a time, Office's (the user's pick).** While pinned, the tab row has only the collapse arrow, which
  does what a double-click and Ctrl+F1 do. While collapsed, the tab row has no arrow and no pin; a tab opens the panel
  over the page, and that panel carries a pin that keeps it open.
- **A click on the tab that is already open does nothing**, as in Office.
- **The web, specifically.** The collapse arrow calls `toggleRibbon()` (it used `toggleExpand()`, which left the
  ribbon pinned); `collapse()` unpins a pinned ribbon and `unpin()` collapses; an `attributeChangedCallback` keeps the
  attributes one state, so an app shell that saves `pinned` and `expanded` apart (b-app-shell does) no longer restores
  "pinned, not expanded". The pin moved from the tab row to the panel (`.ribbon-panel-pin`).
- **Gate.** `keyboard-check.mjs` +7 (arrow only while pinned, it collapses by unpinning, the pin on the panel, the pin
  keeps it open, a restored `pinned` opens the panel, closing a pinned ribbon's panel unpins it): 211/211;
  `verify.mjs` 0 failing; `ribbon-a11y-check.mjs` 22/22.

## Human test plan (D6 change)

- [x] In the Playground: collapse with the arrow, open a tab (the panel over the page has a 📌), pin it; reload a page
  saved pinned and check the panel is open; double-click and Ctrl+F1 as before
