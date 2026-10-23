---
id: TASK-762
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: input boxes and dropdowns

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-741. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Field`: text, number or dropdown (free values per dropdown), in the three caption looks the user kept; a committed value is reported once (Enter, a pick, leaving the box); arrows edit inside, Tab leaves, Escape reverts; folded and key-tip support.

## Acceptance criteria

- [x] Text / number / dropdown boxes, free values per dropdown, the three caption looks
- [x] Commit on Enter / pick / leaving, never twice; Escape reverts; arrows edit inside; Tab leaves
- [x] Folded groups, key tips (focus with text selected), narrow menu; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Type a font name and a size not in the list, step Height with the arrows, Escape, and narrow the window

## Outcome (2026-10-23)

- **Boxes.** Text, number and dropdown inputs, sized in characters, in the ribbon's small box size. Each dropdown says
  whether it takes a value not in its list (a font size of 13); a typed label is read as its option's value.
- **The three caption looks.** None (Office: the tooltip and the accessible name say what the box is), beside (dropped
  in a small group, as the web's) and above (the box and its caption take two rows), for the whole ribbon or per box.
- **Committing.** What is typed is a draft. Enter, a pick from the open list, or leaving the box commits it, and the
  ribbon reports it once: leaving after Enter reports nothing more. Enter and a pick also close a folded group's popup
  or an unpinned ribbon, as a command does; leaving does not. Escape puts the committed value back and returns to the
  tab. The app can set a value without an event, and every copy of the box shows it.
- **Keyboard.** The arrows reach a box like any command; inside it the arrows, Home and End edit it, and up / down step
  a number within its bounds. Tab leaves. A key tip puts focus in the box with its text selected, ready to type over.
- **Narrow window.** The menu has the boxes with their captions beside them; a commit there keeps the menu open.
- **Measured and fixed on the way.** The free dropdown stood taller than its neighbours, with a dark border: the theme
  styles text boxes and lists but not that control. It now takes the same tokens and height. Boxes were also centred in
  their columns; they now sit left, as the web's.
- **Gate.** New `RibbonFieldTests`, 15 tests with real headless keys and text:
  - the three boxes and their values; Enter and leaving commit once; Escape reverts and goes to the tab;
  - a number steps within its bounds and the arrows stay in the box; a pick commits once;
  - a free dropdown takes a typed value; the app sets a value silently in every copy;
  - the three caption looks; a box as a panel stop; Enter in a folded group closes it;
  - a key tip selects the text; the narrow menu; screenshots.
  - Birko.Xaml.Avalonia.Tests 317/317; Birko.Xaml.Core.Tests 60/60; the Gallery builds, with Font, Size and Height
    inputs and a caption picker.


## Follow-up from your Gallery check (2026-10-23)

- **No arrow on the Font and Size boxes, fixed.** A dropdown that takes typed values had no ▾, so its list only
  appeared while typing, filtered to what was typed. It now has the web's ▾ (the closed list's chevron): a click opens
  every choice and closes it again, as does Alt+Down; typed text stops short of the arrow. The ▾ is not a separate
  keyboard stop.
- Gate: `RibbonFieldTests` +1, Birko.Xaml.Avalonia.Tests 323/323.
