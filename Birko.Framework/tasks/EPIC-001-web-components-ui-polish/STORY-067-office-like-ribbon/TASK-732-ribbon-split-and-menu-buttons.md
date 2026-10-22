---
id: TASK-732
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
priority: P2
assignee: ai
created: 2026-10-22
depends-on: [TASK-731]
blocks: [TASK-736, TASK-740, TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: split buttons and dropdown menu buttons

## Context

FEATURE-019 D3 (approved by the user 2026-10-22): A command can open a menu of options (like Office's "Paste" with its small arrow): either the whole button opens the menu, or the main part runs the command and the arrow opens the menu.

RibbonItem is id/label/icon/href/action/badge/disabled/active/variant only, so a command cannot carry options.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] RibbonItem can declare a menu (list of items) as a menu button or a split button
- [x] The menu opens below the command in the top layer (not clipped by the panel), keyboard-operable, with Escape and light dismiss; choosing an item emits item-click with the item id and its parent
- [x] Works at every group size and inside a folded group's flyout
- [x] ARIA: aria-haspopup / aria-expanded, menu roles
- [x] A Playground smoke suite covers it
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Open a split button by mouse and by keyboard, pick an option, check the main part still runs its own command
- [x] Open one inside a folded group on a narrow window

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Look, picked by the user from a three-option preview:** option A, Office-like, where the ▾ part's divider shows on
  hover. Menu items carry an icon, a label, a right-aligned shortcut, and separators.
- **API.** `RibbonItem.menu?: RibbonMenuEntry[]` and `split?: boolean`. An entry is `RibbonMenuItem { id, label, icon?,
  shortcut?, disabled? }` or `{ separator: true }`.
  - A choice emits `item-click` with `{ tabId, groupId, itemId: <choice>, parentId: <command> }`.
  - The ▾ part is named "More options: {label}" (`bwc.ribbon.moreOptions`). Labels and shortcuts are escaped.
- **Layout.** Medium and small: command and ▾ side by side. Large and in a flyout: the parts stack, the icon runs the
  command and "Label ▾" opens the menu. The menu opens under the whole split frame, kept inside the viewport.
  - In a folded group, the menu is the flyout's DOM descendant, so opening it keeps the flyout open, and a choice
    closes both.
  - On mobile, choices are listed under their command, and a menu-only command is their heading.
- **Keyboard.** Enter or Space on ▾ or on a menu-only command, and Alt+↓ on the command, open the menu at its first
  choice. ↑/↓ wrap and skip disabled choices; Home/End; Enter runs a choice; Escape or Tab closes, back to the button.
  - Both split parts are panel stops (TASK-731's roving tabindex).
- **Bug found and fixed: TASK-731's flyout stole focus.** It returned focus to its button on any close while focus was
  outside the ribbon. A click elsewhere, or another popover or modal opening, pulled focus back into the ribbon.
  - `_returnFocusOnClose` reads `beforetoggle` and restores only when focus was inside and nobody has taken it since.
    Menus use it too.
  - Found because verify.mjs's gallery-smoke and modal-focus-smoke lost their focus checks. The new "closed by a click
    elsewhere leaves focus there" check fails on the TASK-731 ribbon.
- **Gate.**
  - Playground `keyboard-check.mjs` 66/66 with real clicks and keys, stable over three runs. Its new menu checks plus
    the flyout focus check are red on the TASK-731 ribbon.
  - `ribbon-menu-smoke` 21/21: markup, ARIA, escaping, large, mobile. It opens no popover: verify.mjs runs every suite
    at once, an auto popover closes the others, and opening menus there broke `ribbon-scaling-smoke`'s flyout checks
    intermittently.
  - `verify.mjs` clean three times; entry-points 409/409; `tsc` clean.
  - Checked in Chrome on the gallery ribbon: Paste ▾ hover and open menu.
- **Gallery.** The Playground ribbon now has a split Paste and a CSV menu, so its demo differs from the Avalonia
  gallery's until TASK-742.
- **Not changed.** Existing ribbon labels (tabs, commands, groups) are still interpolated as markup; only the new menu
  text is escaped. b-tabs moved to text labels in TASK-525. Whether the ribbon follows is a user decision, not filed yet.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
