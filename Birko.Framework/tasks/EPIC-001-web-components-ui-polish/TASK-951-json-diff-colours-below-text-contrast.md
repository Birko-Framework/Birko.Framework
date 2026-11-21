---
id: TASK-951
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: [TASK-725]
findings: [FIELD-052]
pr: null
github-issue: null
jira-key: null
---

# `b-json-diff`: the added / removed / changed colours are below text contrast, most in dark mode

## Context

Found running [[TASK-725]]'s human test plan step 1 in the Playground (Chrome, Birko.Web main at v0.4.20,
2026-11-21). The diff's toggles all work; its colours do not read well enough. The coloured text is small (11.375px),
so it needs 4.5:1 (WCAG AA). Measured against each element's composited background:

| element | light | dark |
|---|---|---|
| added value (`ins`, green on its green tint) | **4.23** | **2.80** |
| added marker `+` | 4.59 | **3.53** |
| removed value (`del`) | 4.50 | **3.41** |
| removed marker `−` | 5.08 | **3.74** |
| changed marker `~` and its "(2 changes)" count | 4.65 | **3.55** |

Dark: every coloured element fails, and the added value fails even the 3:1 large-text minimum. Light: the added value
fails. The neon, finstat and inverse themes were not measured.

The theme contrast scan ([[TASK-130]]) did not report these; whether it should cover component-local colours like
these is a question for TASK-130, not for this task.

## Acceptance criteria

- [ ] Every coloured element of `b-json-diff` (added, removed and changed markers, values and counts, in the tree and
      side-by-side views) is at least 4.5:1 against its actual background in every shipped theme (light, dark, neon,
      finstat, inverse).
- [ ] Added, removed and changed stay distinguishable from one another, and not by colour alone (the `+` / `−` / `~`
      markers and the screen-reader text stay).
- [ ] A Playground check measures these ratios per theme and fails below 4.5:1; it is shown red before the fix.

## Out of scope

- The contrast of other components; the theme tokens' own pairs (TASK-130's scan).

## Human test plan

1. In the Playground's JSON diff, switch to dark, expand everything, and read the added, removed and changed values:
   each is clearly legible, and the three still look different from one another.
2. Repeat in light, and glance at neon, finstat and inverse.
