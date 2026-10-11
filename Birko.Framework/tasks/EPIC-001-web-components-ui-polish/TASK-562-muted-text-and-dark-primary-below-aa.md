---
id: TASK-562
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `--b-text-muted` and the dark primary button fall below WCAG AA in light and dark

## Context

Found by Presenter's accessibility audit (Presenter `TASK-025`, axe + Lighthouse, 2026-10-11). Fixed
upstream at Presenter `TASK-031` by the user's decision, so every Birko consumer inherits the fix.

- **`--b-text-muted`** is below 4.5:1 for text in both base themes:
  - light `#94a3b8`: 2.56:1 on `--b-bg`, 2.45:1 on `--b-bg-secondary`
  - dark `#64748b`: 3.75:1 on bg, 3.07:1 on bg-secondary

  119 component rules use it, including `.close-btn`, the shared close button of `b-modal`, `b-drawer`
  and `b-gallery`. `Birko.Web.Components/CLAUDE.md` § *hint vs description* already measured the
  shortfall and routed help text to secondary; the token itself was never fixed.
- **Dark primary button:** white `--b-text-inverse` on `--b-color-primary` `#3b82f6` is 3.68:1, and
  2.54:1 on hover (`#60a5fa`). `--b-color-primary` cannot simply darken, because 45 rules also use it as a
  *text* colour on dark surfaces. Darkening it to `#2563eb` would drop those to 2.83:1.

## Acceptance criteria

- [x] `--b-text-muted` ≥ 4.5:1 on `--b-bg` and `--b-bg-secondary`, in light (`:root`) and dark. Light
      `#64748b` (4.76 / 4.55), dark `#8391a7` (5.59 / 4.58). Both stay distinct from `--b-text-secondary`
- [x] A new token, `--b-text-on-primary`: text on a primary fill. `:root` sets
      `var(--b-text-inverse)`, so light, finstat, neon and inverse keep their behaviour. Dark sets `#0f172a`
      (4.85:1 on primary, 7.02:1 on hover)
- [x] `b-button` `.primary` and `b-pagination` `.page-btn-active` use
      `var(--b-text-on-primary, var(--b-text-inverse))`
- [x] The values are changed in `tokens.json` and generated (never hand-edited into the CSS). `verify` is
      clean for CSS and AXAML
- [x] Regression test `tests/Birko.DesignTokens.Tests/ContrastTests.cs`: it reads `tokens.json` and
      asserts every pair above at ≥ 4.5:1. The whole suite is green
- [x] `Birko.Web.Components/CLAUDE.md` records the new token and the muted floor

## Out of scope

- Muted in neon, finstat and inverse. Those themes were not measured by the Presenter audit. Measure them
  before changing a value
- Muted on `--b-bg-tertiary` (the hover band). Dark secondary itself is only 4.04:1 there, so that needs a
  separate decision
- Version tag and CHANGELOG entry: done at push time per `Web/CLAUDE.md`

## Human test plan

- [x] In a Birko app (Presenter will do), dark and light: modal close × and muted captions are readable but
      still look secondary; the primary button's dark-on-blue text reads cleanly, including on hover

## Implementation plan

1. `tokens.json`: two muted values, plus `--b-text-on-primary` in the `:root` and dark sheets, each with
   a ratio comment. Then run `generate` and `verify`.
2. `ContrastTests.cs` (WCAG relative luminance; `var(--x)` is resolved inside the same sheet, falling back
   to `:root`).
3. Web: the two component rules and the CLAUDE.md note.
4. One commit in each repo: Framework (tokens.json, AXAML, the test, this file) and Web (CSS, components,
   docs).

## Progress log

- 2026-10-11 — implemented: `tokens.json` muted (light `#64748b`, dark `#8391a7`) and `--b-text-on-primary` (`:root` `var(--b-text-inverse)`, dark `#0f172a`); `generate` + `verify` clean (5 CSS, 6 AXAML; finstat/neon AXAML gain `BTextOnPrimary` from their own inverse). `ContrastTests.cs`: 56/56 green, and its 8 pair cases fail against the previous `tokens.json`
- 2026-10-11 — Web side committed as Birko.Web `234cd4d` (CSS, `b-button`, `b-pagination`, components CLAUDE.md). Presenter's axe + Lighthouse matrix (Presenter TASK-031): zero `color-contrast` violations in dark and light, Lighthouse 100 on every surface but the help overlay (heading-order, Presenter TASK-032)
- 2026-10-11 — human test plan run by the user in Presenter (dark and light, modals, remote, follow, primary button incl. hover): passed. Spawned TASK-563 for the six remaining white-on-primary sites. Tag + CHANGELOG at the user's push, per `Web/CLAUDE.md`
