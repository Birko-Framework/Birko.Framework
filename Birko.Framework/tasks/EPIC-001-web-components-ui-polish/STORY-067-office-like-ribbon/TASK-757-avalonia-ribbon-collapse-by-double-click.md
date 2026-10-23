---
id: TASK-757
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: collapse by double-clicking a tab

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-735. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Ctrl+F1 already collapses the desktop ribbon. Add Office's double-click on a tab, with the same toggle semantics as the web (pinned ↔ tabs only), and make the collapse shortcut rebindable or off.

## Acceptance criteria

- [x] Double-clicking a tab collapses the ribbon and brings it back
- [x] Ctrl+F1 and the double-click share one toggle; the shortcut is rebindable or off
- [x] Headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Double-click a tab and press Ctrl+F1 in the Gallery: the ribbon collapses and comes back

## Outcome (2026-10-23)

- **One toggle.** `ToggleRibbon()` is shared by a double-click on any tab and by the new `CollapseShortcut`: Ctrl+F1
  by default, rebindable, null turns it off, and an app that handled the key first wins. Expanded and pinned, it
  collapses; collapsed or unpinned, it pins the ribbon open, as b-ribbon's does.
- **The single click keeps its behaviour**, which already selects, and on the active tab toggles. The double-click is
  decided from the state before its first click, and the click that ends it is swallowed.
- **Three things measured on the way**, each of which made a double-click impossible:
  - `ClickCount` is always 1 on the second press. The first click rebuilds the tab strip, so the second press lands on
    a new button, and Avalonia counts a double-click only on the same element. The ribbon times the two presses
    itself, from the platform's double-tap time and size.
  - The headless platform stamps every event 0, so the timing uses the system clock.
  - Unpinned, the first click opens the reveal popup, and its light dismiss consumed the second press, so the reveal
    only closed. The popup now passes the dismissing press through (`OverlayDismissEventPassThrough`). This also lets a
    click on another tab switch to it in one press, as in Office.
  - In the tests the ribbon is top-aligned, as an app docks it. Filling the window, a collapsed ribbon centred its
    strip, and the tab moved about 100px between the clicks.
- **Gate.** New `RibbonCollapseTests`, 5 tests with real headless mouse clicks, which do not compile against the code
  before:
  - double-click on the active tab collapses, and again brings it back;
  - on another tab, it selects and collapses; unpinned, it pins open;
  - a single click is unchanged;
  - the shortcut is shared, rebindable and can be turned off.
  - Birko.Xaml.Avalonia.Tests 256/256.

## Follow-up: D6 changed by the user (2026-10-23)

- **No more "pinned, only tabs" ribbon.** Collapsed and unpinned are one state, as in Office: collapsing unpins, and
  pinning opens the panel. Before, a pinned ribbon could show only its tabs with nothing open, which the user found
  and read as broken.
- **One control at a time, Office's (the user's pick).** While pinned, the tab row has only the collapse arrow, which
  does what a double-click and Ctrl+F1 do. While collapsed, the tab row has no arrow and no pin; a tab opens the panel
  over the page, and that panel carries a pin that keeps it open.
- **A click on the tab that is already open does nothing**, as in Office.
- **The desktop, specifically.** `ToggleRibbon()` now unpins (it collapsed and kept the ribbon pinned, so the
  desktop's double-click and Ctrl+F1 also led to the third state); `IsPinned` and `IsCollapsed` keep each other in
  step, whoever sets them; the 📌 moved from the tab row to the temporary reveal's bottom right.
- **Gate.** `RibbonCollapseTests` +3 (one control at a time with the pin on the floating panel; pinning always opens
  and collapsing always unpins; a click on the open tab does nothing), two tests updated to the new model:
  Birko.Xaml.Avalonia.Tests 327/327. The Gallery's own pin button follows the ribbon now.
