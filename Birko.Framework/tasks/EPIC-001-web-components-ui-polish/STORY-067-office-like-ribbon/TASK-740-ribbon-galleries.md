---
id: TASK-740
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-732]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: galleries

## Context

FEATURE-019 D11 (approved by the user 2026-10-22): A group can show a row of visual choices (like Office's style gallery) that scrolls and expands into a larger grid.

Not available; apps list the choices as plain commands.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] An in-ribbon gallery of items with a preview, row scroll buttons and an expand button opening the full grid
- [x] Scales down with the group (fewer visible items, then folded)
- [x] Keyboard and ARIA as a listbox/grid
- [x] A Playground smoke suite covers it
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Scroll, expand and pick from a gallery by mouse and keyboard at a wide and a narrow width

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user.** All three previewed looks kept and switchable: `gallery-style` framed (default) / open /
  dropdown, overridable per gallery. Picking selects and reports (over "runs it, no current choice").
- **API.**
  - `RibbonItem.gallery` = `{ items: [{ id, label, preview?, keytip? }], selected?, visible? (4), columns? (4), style? }`.
  - `setGallerySelection(itemId, choiceId | null, tabId?)` is silent; `getGallerySelection(itemId, tabId?)` reads.
  - A pick emits `item-click` with `{ tabId, groupId, itemId: <choice>, parentId: <gallery> }`.
- **Behaviour.**
  - The inline row shows `visible` tiles, stepped a row at a time by ▲ ▼ (framed) or ‹ › (open); the ends are
    disabled. Small groups show 2.
  - The grid opens over the gallery: arrows by one and by a row, Home / End, Enter picks, Escape closes with focus
    back on ▾.
  - After a pick, focus returns to the picked tile, or to ▾.
  - Tiles are radios in a named radiogroup and key-tip targets; the grid is a key-tip level.
  - The mobile list offers radios, kept in step.
  - In a folded group, the grid opens over the flyout and a pick closes both.
- **ARIA.** The row and the grid are `role="radiogroup"` with `aria-label` = the gallery's label; tiles are
  `role="radio"` + `aria-checked`. Previews and labels are escaped.
- **Fixed while testing.**
  - Grid tiles showed a tooltip repeating their visible name, which took the first Escape. Tiles now have none.
  - The mobile radios went stale after a pick, because the panel re-render does not cover the mobile list.
- **Flake removed.** Playground `ribbon-scaling-smoke`'s flyout checks failed about one run in three. A modal from
  another suite (gallery-smoke's zoom) closes every popover. The check now waits for no open modal and retries once.
  `verify.mjs` was clean four runs in a row after that.
- **Gate.** Playground `keyboard-check.mjs` 168/168 twice, with 21 new gallery checks using real clicks and keys.
  - They cover: the framed row; radios; picking; ▲ ▼ with their ends; text preview; grid open on the current tile
    and its arrows; Enter picks with focus back; Escape; `setGallerySelection` / `getGallerySelection`; open style;
    per-gallery dropdown; key tips for ▾ and the grid; the mobile radios and a pick there; the folded flyout and grid.
  - The checks fail on the TASK-734 ribbon. entry-points 409/409; `tsc` clean. Checked in Chrome: the framed gallery
    and its open grid.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
