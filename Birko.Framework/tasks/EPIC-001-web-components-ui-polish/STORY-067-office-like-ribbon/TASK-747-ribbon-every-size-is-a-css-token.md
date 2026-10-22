---
id: TASK-747
parent: STORY-067
feature: FEATURE-019
status: done
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: every size is a CSS token

## Context

Asked by the user after the FEATURE-019 run of ribbon work: "are all sizing and other properties as css tokens?"
Measured: no. The ribbon's stylesheet had 82 literal lengths outside any `var()`:

- about 40 matched a token that already existed (`0.125rem` gaps, `0.25rem`, `0.625rem` text, 1px and 2px borders);
- about 25 were ribbon-specific sizes with no token (gallery tile, toolbar button, key tips, badges, launcher, menu
  and flyout limits, the contextual accent, an input box's padding);
- the rest were structural (`100%`, `repeat(3, 1fr)`, the −1px tab overlap, the 0.5px divider centring).

## Acceptance criteria

- [x] Every length is a token, except what CSS cannot or should not tokenise (media queries, `em` sizes that scale with
      their own text, the 92vw phone cap)
- [x] No visual change: screenshots and computed styles identical before and after
- [x] A check fails when a literal length comes back
- [x] Components README and CLAUDE.md list the ribbon tokens

## Human test plan

N/A — fully covered by automated tests (pixel-identical screenshots of 24 states, computed styles of every element).

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Shared tokens** where the value already existed: `--b-space-2xs`, `--b-space-xs`, `--b-text-2xs`,
  `--b-border-width` (also the tab overlap, the divider's width and centring, inset rings), `--b-border-width-thick`.
- **21 new `--b-ribbon-*` tokens** in `css/tokens.css`, each defaulting to the old value: `item-min-width`,
  `flyout-max-width`, `menu-min-width`, `popup-max-height`, `tab-badge-size`, `tab-divider-height`, `context-accent`,
  `scroll-glyph`, `item-badge-size`, `item-badge-text`, `badge-padding`, `keytip-size`, `qat-button-size`,
  `qat-more-width`, `launcher-size`, `launcher-glyph`, `gallery-tile-width`, `gallery-tile-height`,
  `gallery-button-width`, `field-padding`, `mobile-header-height`. No theme overrides the shared tokens used, so no
  theme changes.
- **Measured unchanged.** A throwaway puppeteer script captured the demo ribbon in 24 states, in light and dark:
  four widths, the contextual looks, a menu, the gallery grid, the toolbar menu, key tips and a folded flyout. Each
  capture also dumped the box and 23 computed properties of all 435 shadow elements. Before vs after: the computed
  dumps are identical in every state. The screenshots are pixel-identical apart from one key-tip capture in some runs,
  which also differs between two runs of the same code: the badges close as the screenshot blurs the window.
- **Gate.** New Playground `ribbon-tokens-smoke` reads the live stylesheet and fails on any literal length outside its
  allowlist. It fails on the code before (82 literals) and passes now. `verify.mjs` clean; keyboard-check 195/195;
  `tsc` clean.
