---
id: TASK-734
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: contextual tabs

## Context

FEATURE-019 D5 (approved by the user 2026-10-22): Some tabs appear only while something is selected (like Office's "Table Design" when a table is selected), shown together under a coloured heading, and disappear again when the selection goes.

RibbonTab.category only draws a divider between categories; tabs cannot be shown or hidden as a set or coloured.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] A set of tabs can be declared contextual with a name and a colour, and shown or hidden by the app at runtime
- [x] Shown sets sit after the normal tabs with their heading and colour; hiding the set while one of its tabs is active falls back to the previous normal tab and emits tab-change
- [x] Colours come from tokens and pass contrast in every theme
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Select and deselect something in a demo page: the contextual tabs appear and disappear, the active tab falls back
- [x] Check the heading colour in light, dark, neon, finstat and inverse

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user.**
  - Of three previewed looks, the user asked to keep all three, switchable by configuration: `contextual-style` on
    the ribbon (`band` default, `pill`, `edge`), overridable per set.
  - Hiding the open set goes back to the previous normal tab (picked over "always the first tab").
- **API.**
  - `RibbonTab.contextualSet`.
  - `setContextualSets([{ id, label, color?, style? }])`, `showContextual(id, { select? })`, `hideContextual(id)` and
    `isContextualShown(id)`.
  - Colours are `primary` | `success` | `warning` | `danger` | `info`; unknown values fall back to primary.
- **Render.**
  - Normal tabs come first, then each shown set in declared order, inside a presentation wrapper in the tablist. The
    name is `aria-hidden` text, and each tab carries it as `aria-description`.
  - Colour rules are generated per theme colour, using the -text / -alpha-bg / -alpha-border pairs that pass AA
    (TASK-599); primary uses -light and itself.
  - The band splits the fixed tab-row height, so the ribbon does not grow.
  - The mobile list and the tab arrow keys only include shown tabs.
- **Hiding.**
  - When the open tab belongs to the set, the ribbon goes back to `_lastNormalTab` (else the first visible tab) and
    emits `tab-change`.
  - Hiding a set that is not open changes nothing.
- **Gate.**
  - Playground `ribbon-contextual-smoke` 21/21. It covers: hidden until shown, placement, band, name and colour,
    aria-description, the name's colour token, row height, `isContextualShown`, the three styles, an unknown style,
    a per-set override, an unknown colour, the name as text, hiding back to the previous tab with `tab-change`, hiding
    a set that is not open, `select`, the arrow keys through the set and wrapping, and the mobile list.
  - It fails on the TASK-733 ribbon. `keyboard-check` 147/147; `verify.mjs` clean; entry-points 409/409; `tsc` clean.
  - Checked in Chrome: band, pill and edge on the gallery's Table set.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
