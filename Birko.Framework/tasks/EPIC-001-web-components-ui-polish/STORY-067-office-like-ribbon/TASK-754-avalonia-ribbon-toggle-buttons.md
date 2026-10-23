---
id: TASK-754
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-23; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: toggle buttons and one-of sets

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-739. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Toggle` / `Pressed` / `ToggleGroup`; a click flips a toggle (or picks a set member and releases the others); the pressed look is the tinted and outlined one the user picked; `SetPressed` / `IsPressed`; state kept across rescales and in the narrow menu; automation toggle state.

## Acceptance criteria

- [x] Toggle and one-of sets with the picked pressed look, no layout shift
- [x] `SetPressed()` silent, `IsPressed()`; the click reports the new state
- [x] Kept across rescales and in the narrow menu; automation toggle state; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Toggle Bold and Italic, switch Columns / Rows by mouse and keyboard; check the pressed look in every theme

## Outcome (2026-10-23)

- **Model.** `RibbonItem.Toggle`, `Pressed`, `ToggleGroup`.
- **Control.**
  - A toggle is a `RibbonToggleCommand`: a `Button` whose automation peer reports the Toggle pattern, or a radio
    button (SelectionItem) for a one-of member.
  - It is not a `ToggleButton`. That would flip itself, and its Fluent checked look would fight the picked one.
- **State.**
  - The ribbon owns the state, by tab, group and item. It is seeded from `Pressed` and reset by new `Tabs`.
  - Every copy of a toggle is kept in step in place, with no rebuild: each size, the folded popup's, and the narrow
    menu's.
  - Pressing a one-of member releases the others; pressing the one that is on keeps it on.
- **API.** `ItemToggled` (tab, item, new state) fires before `Run`; `SetPressed` is silent; `IsPressed` reads.
- **Look (D10, option A).** The primary tint, the primary text colour and a 1px primary frame inside the button. The
  frame is always there, transparent when off, and the padding is a pixel less, so pressing never resizes. It is
  re-applied on a theme switch.
- **Fixed while testing.** Framing the content re-parented a control that the button still held ("already has a
  parent"). The content is now detached first.
- **Gallery demo.** Bold and Italic are toggles, Columns / Rows a one-of set, as in the web Playground.
- **Gate.** New `RibbonToggleTests`, 7 tests, which do not compile against the code before:
  - a toggle flips, reports and runs, with the look and no size change;
  - one-of sets; `SetPressed` silent with every copy in step, through a rescale and a tab reset;
  - automation (toggle, radio button, a plain command has no state);
  - the look after a theme switch; the narrow menu's state;
  - screenshots in light and dark.
  - Birko.Xaml.Avalonia.Tests 236/236; Birko.Xaml.Core.Tests 60/60; the Gallery builds. Checked on headless
    screenshots in light and dark.


## Follow-up from your Gallery check (2026-10-23)

- **Two outlines on a pressed toggle, fixed.** The tint was on the button and the 1px frame sat inside its padding, so
  a larger tinted rounded shape showed around the frame. The tint and the frame are now one shape filling the button
  inside the focus ring, as the web's inset outline; the button's size does not change.
- **A second border on click, fixed.** The focus ring showed on any focus, a mouse click included. It now shows for
  keyboard focus only, as the web's `:focus-visible`; this applies to every ribbon button.
- Gate: `RibbonToggleTests` +2 (the frame fills the button inside the ring; a real click shows no ring, Tab does),
  Birko.Xaml.Avalonia.Tests 322/322.
- **A toggle's label not lined up with its icon, fixed.** The toggle, menu and folded-group buttons are subclasses of
  `Button` and so got no Birko button theme: they fell back to the base theme, whose font made their labels taller (15px
  against 13px) and set them apart from the plain commands. All three are styled as `Button` now, so every ribbon command
  draws its label the same way. Measured in a headless probe: icon and label positions in a toggle now equal a plain
  command's.
- **Icons sitting lower than their labels, fixed.** An icon is a character, and characters from different fonts sit at
  different heights in the same line box: Bold's and Italic's math letters drew about 2px below the middle. Every
  command's and toolbar button's icon is now centred by its measured ink, a draw-time shift that leaves every size
  unchanged. Checked in the live Gallery: Bold, Italic, Colour, Columns, Rows, Paste and Cut line up with their labels.
  Gate: `RibbonToggleTests` +1 (the ink of 𝐁, 𝑰 and ✂ is centred in its box), Birko.Xaml.Avalonia.Tests 324/324.
- **Still 1–2px off at your second check, fixed again.** Centring the icon's ink in its box was the wrong target: a
  label's letters sit above its box's middle (the box keeps room for descenders), and a letter icon is read as text.
  Now a letter icon (𝐁, 𝑰) stands on its label's baseline, and a picture icon (✂, 📋, ▥) has its ink centred on the
  label's capital letters; an icon with no label beside it is still centred in its box. The shift is rounded to device
  pixels so the glyph stays sharp, and positions are read from the layout, not through the icon's own shift (the first
  version measured against its own transform). Checked at 3x zoom in the live Gallery: Bold and Italic share their
  labels' baselines. `RibbonToggleTests.An_icon_glyph_lines_up_with_its_label` replaces the box-centring test;
  Birko.Xaml.Avalonia.Tests 324/324.
