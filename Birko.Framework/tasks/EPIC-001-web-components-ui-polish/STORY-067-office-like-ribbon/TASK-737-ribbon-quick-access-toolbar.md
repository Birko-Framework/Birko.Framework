---
id: TASK-737
parent: STORY-067
feature: FEATURE-019
status: verify  # verified with real clicks and keys 2026-10-22; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: Quick Access Toolbar

## Context

FEATURE-019 D8 (approved by the user 2026-10-22): A small row of favourite commands always visible above or below the tabs, which the user can add to and remove from, and which is remembered.

No such row exists.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] The app declares the toolbar's default commands; the user can add a command (context menu "Add to Quick Access Toolbar") and remove one
- [x] The choice persists under a key (localStorage, as split-panel sizes do), hostile stored values ignored
- [x] Default position in the tab row, before the tabs (decided by the user from a live preview, Q3); an attribute
      moves it to its own row above the tabs or below the ribbon; keyboard-reachable; announced
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Add and remove commands, reload: the toolbar keeps them; check it at a narrow width

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **API.** `setQuickAccess(items)`, `getQuickAccess()` (refs), attributes `qat-key` and `qat-position` (`tabs` default |
  `above` | `below`), event `quick-access-change` → `{ refs }`. A button emits `item-click` with `source: 'qat'`; a
  ribbon command added there also carries its `tabId` / `groupId`.
- **Customising.**
  - Right-click, Shift+F10 or the menu key (all fire `contextmenu`) on a ribbon command offers "Add to Quick Access
    Toolbar". On a toolbar button it offers "Remove". Menu-only commands and links cannot be added.
  - ▾ lists the commands as `menuitemcheckbox`es plus "Reset to default". A re-shown default returns to its declared
    place, as in Office.
  - Both menus are one shared popover, filled when it opens; focus returns to the opener.
- **Storage.** Under `birko-ribbon-qat:<key>`: a list of refs, each matched against a strict pattern, de-duplicated,
  capped at 50 and resolved against real commands. Anything else is ignored. Reset removes the stored list.
- **Behaviour.**
  - One Tab stop with ←/→/Home/End inside.
  - Tooltips come from the shared b-tooltip.
  - A toggle pressed in the toolbar is pressed in the ribbon too.
  - Hidden below 48rem, where the mobile menu lists everything.
- **Bug found and fixed.**
  - `_showTabContent` (hover preview, scaling) re-runs `_bindMenus`, which bound the ribbon-menu click handler onto
    every `.ribbon-menu-item`. That included an open shared menu, so after a hover across a tab, a choice was swallowed
    and a stray item-click emitted.
  - The shared menu is now excluded. It also explains a flaky failure: a check passed in isolation, where the pointer
    never crossed a tab.
- **Gate.**
  - Playground `keyboard-check.mjs` 116/116 twice, with 23 new checks using real clicks and keys. They cover:
    defaults; the tab-row position; a click's source; one Tab stop and arrows; adding by right-click and Enter (saved
    and reported); running an added command; menu-only commands refused; a toggle kept in step; removing by Shift+F10;
    ▾ checkboxes and hiding; a hover preview while ▾ is open; restoring after a remount; hostile, non-list and unknown
    stored values; reset; and above/below.
  - Without the menu exclusion, 2 fail (2 stray item-clicks).
  - `verify.mjs` clean; entry-points 409/409; `tsc` clean.
- **Not checked by eye.** The Chrome tab froze while showing this; headless runs at the same viewport and theme did
  not reproduce it.

## Follow-up (2026-10-22): the toolbar is opt-in

The user asked whether the toolbar can be left out. It could only partly: it showed only after `setQuickAccess()`, but a
right-click on any command still offered "Add to Quick Access Toolbar", which created one in an app that never asked for
it.

- Now `setQuickAccess()` switches it on: there is no toolbar and no "Add" entry until then. `[]` gives an empty toolbar
  the user can fill, and `null` turns it off.
- New checks in keyboard-check: no toolbar or menu without the call, `[]` fillable, `null` off. 119/119; the first check
  fails without the right-click guard.
