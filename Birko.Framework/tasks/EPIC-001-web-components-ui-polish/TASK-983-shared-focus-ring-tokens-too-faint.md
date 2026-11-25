---
id: TASK-983
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-982]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The shared focus-ring tokens are below 3:1 in every theme

## Context

Found at TASK-982 (Reps' report on b-sync-status's dead button). `--b-focus-ring` and `--b-focus-ring-danger` are a
3px `box-shadow` wash at 15–30% alpha (`Birko.DesignTokens/tokens.json` → `css/tokens.css` and the themes). Measured
against the page background, composited: `--b-focus-ring` 1.23:1 (base), 1.41:1 (dark), 1.26:1 (finstat), 1.99:1
(neon); `--b-focus-ring-danger` 1.26:1 (base), 1.31:1 (dark). A focus indicator needs 3:1 (WCAG 1.4.11 / 2.4.11).
About 35 components rely on them: the shared `:focus-visible` sheet, inputs and selects on `:focus`, combo
containers, `.toggle-control`, `b-checkbox`, table sort buttons, kanban cards and lane tags, sortable-list handles,
tag remove buttons, toolbar and icon buttons. `b-button` has no rule of its own and shows the browser's default 2px
outline, which is why it stands out as the clear one.

Inputs also change their border to `--b-border-focus` on focus, so their indicator is not the ring alone; measure that
border's contrast too before deciding they fail.

## Acceptance criteria

- [x] Every shipped theme's focus indicator for the token users reaches 3:1 against the adjacent colours, measured.
- [x] **The look is chosen by the user from a preview** (2–3 options, e.g. a stronger-alpha ring, a solid 2px ring
      with a gap, a solid outline) before the tokens change: this restyles every focusable control in every consumer.
- [x] The change is made in `tokens.json` and generated; token-parity CI passes.
- [x] Playground check measuring the indicator on a representative set (an input, a checkbox, a table sort button, a
      kanban card) in every theme, red before the change.
- [x] CHANGELOG entry naming the visual change.

## Evidence

**The look: "G", picked by the user 2026-11-25** from a preview page (round 1: solid ring, gap ring, outline, set aside
as too hard-edged; round 2: halo with edge, deeper halo, blurred glow; G is the blurred glow tightened at the user's
request). Each ring is `0 0 0 1px <line>, 0 0 4px 1px <glow at 50%>`: the 1px line carries the contrast, the tight glow
keeps the soft look. Line colours: base #2563eb / #dc2626, dark #3b82f6 / #ef4444, neon #8cffb0 / #ff4466, finstat
#1c86b8 / #d9482a (its #54b9e8 measured 2.21:1, so the line is a deeper shade; the glow keeps the lighter blue),
inverse #60a5fa / #f87171 (new: the inherited base ring measured 2.80:1 on its charcoal).
- Framework `task/TASK-983` 303a7da9 (`tokens.json`, held on a branch so Framework main keeps matching Birko.Web main
  until the merge); generated into Birko.Web `task/TASK-983` 9310879 (joins v0.5.3), `verify` OK (5 CSS + 6 AXAML; the
  AXAML carries no focus rings). DesignTokens tests 59/59 against the two branches.
- Playground e552414 `focus-ring-check` 24/24: in base, dark, neon, finstat and an inverse area on base and on finstat,
  the ring's and the danger ring's solid line measure 3.64–15.53:1 against the page and the secondary surface; a real
  Tab onto `b-checkbox` and into `b-input` draws the token. **Red** 12/24 on Birko.Web main (1.23–2.04:1).
- verify 0 failing (twice); keyboard-check 244/244; theme-roundtrip 11/11.
- Not covered: Windows high-contrast mode drops box-shadows (as before); an `outline: 2px solid transparent` fallback
  was offered and not chosen.

## Out of scope

- The dead sync button (done locally at TASK-982).

## Human test plan

- [x] Tab through a Symbio form and list page in light and dark: every focused control is plainly marked.

**Signed off and merged 2026-11-25** (the user: "it looks good", then "merge", after tabbing through the Playground
built from task/TASK-983). Framework main cb1e154d (`tokens.json`) and Birko.Web main 75a972f together, as part of
v0.5.3 (the unpushed tag moved). On main: DesignTokens `verify` OK and tests 59/59; `focus-ring-check` 24/24,
`sync-dead-focus-check` 15/15, `split-transition-check` 26/26, `overlay-footer-check` 8/8, keyboard-check 244/244,
verify 0 failing. Stays at verify until the Symbio step above is run (it needs Symbio rebuilt from Birko.Web main).
  - _Run 2026-11-25 by the agent in Chrome with real keys_, Symbio rebuilt from Birko.Web v0.5.4 (30eef30), tenant
    acme, with Symbio's own light/dark tokens updated to v0.5.4 by its TASK-1131 (served by the dev server, branch
    not yet merged there). HR → Positions and its Add Position form, computed styles after the 150 ms fade: in light
    the search box, the sort header and the form fields (title, code, department, pay grade) draw
    `rgb(37,99,235) 0 0 0 1px, rgba(37,99,235,.5) 0 0 4px 1px` and the focused row a 2px rgb(37,99,235) outline; in
    dark the same in rgb(59,130,246). Plainly visible in the screenshots in both. PASS.

**Closed 2026-11-25.** Symbio's own tokens follow via its TASK-1131.
