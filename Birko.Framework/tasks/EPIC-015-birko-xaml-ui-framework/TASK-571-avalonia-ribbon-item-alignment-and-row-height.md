---
id: TASK-571
parent: EPIC-015
feature: FEATURE-015
status: done
priority: P2
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon items are staggered, off-centre and taller than b-ribbon's

## Context

Reported from FinStat's desktop API tester ("the ribbon looks weird", then "too tall"). Measured headlessly
and against `b-ribbon`, the Avalonia `Ribbon`'s Medium items had four defects, all in `BuildItem` / `BuildGroup`:

- **Staggered labels.** The item button stretches to its column and the shared Button theme centres content,
  so labels of different lengths started at different x.
- **Off-centre labels.** Icon and label were top-aligned; the 11px label sat above the middle of its 16px icon.
  `b-ribbon`'s item is a centred flex row. Narrow emoji glyphs (an arrow) also pulled their label left of
  their neighbours'.
- **Rows 34px against the web's 27px.** The focus ring is a permanent 2px border (`AddFocusRing`), added on
  top of the web's 4/8 padding — the web ring is an outline and takes no space. And emoji icons got a 22px
  line box from their fallback font where CSS `line-height: normal` gives ~19px.
- **Group caption** used the default line box (15px) where `.ribbon-group-label` is `line-height: 1`.

`b-ribbon` documents its own target: a degraded three-row medium panel is ~107px. The Avalonia one was ~140px.

## Acceptance criteria

- [x] Medium items left-align their content
- [x] Icon and label are vertically centred; the icon slot is at least `BRibbonIconSmall` wide, glyph centred
- [x] Border + padding equals the web's 8/4 content inset (padding 6/2 under the 2px ring)
- [x] Small-icon line height is capped at 1.2 x `BRibbonIconSmall`; group caption line height is its font size
- [x] A Medium emoji row measures 27px (was 34px)
- [x] Regression test fails without the alignment/centring changes and passes with them

## Human test plan

Desktop tester at full width: every tab's items line up in columns, each label is centred on its icon, and
the ribbon body is visibly shorter than before; at narrow widths the scaling still degrades right to left.

## Review log

- 2026-10-12 — `Medium_items_left_align_their_icon_and_label` covers left alignment, vertical centring and the
  icon slot; it fails with the centring lines removed and passes with them. Row height measured with a
  headless probe (34 → 27px emoji, 31 → 27px text). Full `Birko.Xaml.Avalonia.Tests` green.
