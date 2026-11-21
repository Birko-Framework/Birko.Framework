---
id: TASK-951
parent: EPIC-001
feature: null
status: verify
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

- [x] Every coloured element of `b-json-diff` (added, removed and changed markers, values and counts, in the tree and
      side-by-side views) is at least 4.5:1 against its actual background in every shipped theme (light, dark, neon,
      finstat, inverse).
- [x] Added, removed and changed stay distinguishable from one another, and not by colour alone (the `+` / `−` / `~`
      markers and the screen-reader text stay).
- [x] A Playground check measures these ratios per theme and fails below 4.5:1; it is shown red before the fix.

## Decision

The user's, 2026-11-21, picked from three: the shared data-viewer card moves to `--b-bg-secondary` (over a diff-only
surface, or retuning the semantic text tokens everywhere). Then, for the `inverse` box the card fix did not reach, the
user chose to lighten inverse's success and warning text, as inverse already does for danger.

## Change (Birko.Web v0.4.20, Birko.Framework tokens)

- `.data-viewer-card` (b-json-diff, b-json-viewer, b-object-tree, b-xml-viewer, b-code-block): `--b-bg-tertiary` →
  `--b-bg-secondary`.
- `b-json-diff` `ins` / `del`: the solid `--b-color-{success,danger}-light` tint instead of the `-alpha-bg` wash; that
  is the badge pair the token gate already holds at 4.5:1.
- `inverse` theme, in `tokens.json` and generated: `--b-color-success-text` `#2cac5b` → `#4ade80`,
  `--b-color-warning-text` `#dc8117` → `#fbbf24`.
- Token gate: new `SemanticOnSurface` pairs (success/warning/danger `-text` on `--b-bg` and `--b-bg-secondary`), 48
  more measurements, 280 in all.

## Evidence

- Playground `device-fix-check` § 3c: 35 FAIL on Birko.Web main (the numbers in Context, plus the markers inside
  `inverse` at ~3.0:1); with the card and tint change, 16 FAIL left, all inside `inverse`; with the inverse tokens,
  105/105, 245/245 overall.
- `Birko.DesignTokens contrast`: the new pairs fail 8 times with inverse's old values (3.50 and 3.52:1 on `#434040`),
  pass with the new; `Birko.DesignTokens.Tests` 59/59 against the worktree; `verify`: CSS and AXAML match tokens.json.
- `verify.mjs` 400/400 twice, 0 failing checks; theme-roundtrip 11/11; rerender-leak PASS; a11y-name OK;
  entry-points 433/433; Components `tsc` clean.

**Push together:** Birko.Web's `token-parity` CI compares its CSS with Birko.Framework `main`'s `tokens.json`, so the
Framework commit and Birko.Web's must be pushed in the same go.

## Out of scope

- The contrast of other components; the theme tokens' own pairs (TASK-130's scan).

## Human test plan

1. In the Playground's JSON diff, switch to dark, expand everything, and read the added, removed and changed values:
   each is clearly legible, and the three still look different from one another.
2. Repeat in light, and glance at neon, finstat and inverse.
