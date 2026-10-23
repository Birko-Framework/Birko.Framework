---
id: TASK-763
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751, TASK-752, TASK-753, TASK-754, TASK-755, TASK-756, TASK-758, TASK-759, TASK-760, TASK-761, TASK-762]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: every size a theme resource

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-747. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

The web ribbon's sizes are all `--b-*` tokens (TASK-747). On the desktop, every size the ribbon draws comes from a theme resource in `Themes/Tokens.axaml`, with a check that fails on a literal.

## Acceptance criteria

- [x] Every size the ribbon draws is a theme resource; no visible change, measured
- [x] A test fails on a literal size in the ribbon code

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

N/A — fully covered by automated tests

## Outcome (2026-10-23)

- **Every size is a theme resource.** About ninety literal sizes in the ribbon's code (paddings, margins, font sizes,
  gaps, widths, borders, corner radii) now come from theme resources. Where the desktop size is the web's, it reads
  the same design token the web does. The rest are the ribbon's own sizes in a new `Controls/Ribbon.axaml` dictionary,
  which both theme bundles already merge, so no app has to change anything. Most of those differ from the web for a
  measured reason: the desktop draws its focus ring as a 2px border, which takes space, where the web's outline takes
  none. An app can override any of them.
- **Where they live, a detail changed from the task text.** The task said the generated `Themes/` dictionaries. Those
  are produced from the web's token file, and the web has no such sizes, so adding them there would put
  desktop-only values into the web's stylesheet. They sit beside the other hand-authored control dictionaries instead.
- **No visible change, measured.** Before and after, every element's position, size, margin, padding, border, radius
  and font size was dumped in 21 states (every group size and contextual look, pinned and unpinned, the reveal, the
  narrow bar and menu, a folded group's popup, the gallery grid, a tooltip, key tips): identical, 6 572 lines. The 35
  ribbon screenshots are byte-identical. Two baseline runs were first checked to be identical to each other.
- **Gate.** New `RibbonSizeTests`, 3 tests:
  - the ribbon's code has no literal size (89 lines of the code before this task fail it);
  - the check itself recognises literals and passes token reads;
  - every resource key the ribbon names resolves, and every key in the new dictionary is used.
  - Birko.Xaml.Avalonia.Tests 320/320; Birko.Xaml.Core.Tests 60/60; the Gallery builds.

