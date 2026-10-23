---
id: TASK-758
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752, TASK-755]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: key tips (plain Alt)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-736. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Tapping and releasing plain Alt shows letters on the tabs (decided 2026-10-23: plain Alt, as in desktop Office; rebindable). The levels follow the web: tabs, then a tab's commands, then a folded group, menu or gallery; Escape steps back; letters are automatic with `Keytip` overrides; dark squares as picked on the web.

## Acceptance criteria

- [x] Plain Alt (tap and release) shows tab letters; a letter opens the tab and shows command letters
- [x] Levels for folded groups, menus and galleries; Escape steps back; a click or focus loss ends it; Alt inside an input does nothing
- [x] Automatic letters with `Keytip` overrides; dark squares; rebindable; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Reach three different commands with key tips only, one in a menu and one in a folded group

## Outcome (2026-10-23)

- **Trigger.** Tapping and releasing plain Alt (either Alt key) shows a letter on every tab, as in desktop Office (the
  user's choice). It arms only with nothing pressed in between, so Alt+F does nothing, and never inside a text box.
  `KeyTipKey` rebinds it; `Key.None` turns it off.
- **Levels, as on the web.**
  - Tabs: a tab's letter selects it, bringing the panel up (reveal when unpinned), and shows its commands' letters.
  - Commands: a letter runs a command through its own click path. On a folded group or a menu button it opens the
    popup or menu and shows the letters inside.
  - Inside: a letter picks a menu choice (`MenuItemInvoked`) or runs a command.
  - Escape steps back one level. A click, the window losing focus, or a letter with no tip ends the mode; that
    letter is swallowed.
- **Letters.** A declared `Keytip` on `RibbonTab`, `RibbonGroup`, `RibbonItem` or `RibbonMenuItem` wins. Otherwise
  the label's first free letter (accents folded), then any free letter or digit, up to 36 a level.
- **Badges.** Adorners in the tooltip's dark tokens (the dark squares picked on the web): centred under a tab, on a
  command's bottom-right corner.
- **Measured and fixed on the way.**
  - Keys pressed in a popup do not pass the window's tunnel even when its visual root is the window, so a letter in a
    menu did nothing. The mode also listens on the popup's presenter, de-duplicated by event.
  - Badges were clipped to their element, cutting off the half outside the corner (seen in the screenshot). They are
    now unclipped.
  - The mode takes the keys whether or not something marked them handled.
- **Gallery parity gap, by design.** The Quick Access Toolbar's digits (b-ribbon numbers it) and the galleries' and
  inputs' levels come with TASK-759, TASK-761 and TASK-762.
- **Gate.** New `RibbonKeyTipTests`, 9 tests with real headless key presses, which do not compile against the code
  before:
  - Alt shows tab letters, drawn as adorners; a tab letter shows its commands and a command letter runs it;
  - declared letters and first-free letters; a menu's letters pick a choice; a folded group's popup and its letters;
  - Escape step by step; Alt in a text box, Alt+F and the key turned off do nothing;
  - a letter with no tip and a click end it; a screenshot.
  - Birko.Xaml.Avalonia.Tests 265/265; Birko.Xaml.Core.Tests 60/60; the Gallery builds.

