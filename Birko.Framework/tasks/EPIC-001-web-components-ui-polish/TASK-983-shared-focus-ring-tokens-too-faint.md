---
id: TASK-983
parent: EPIC-001
feature: null
status: todo
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

- [ ] Every shipped theme's focus indicator for the token users reaches 3:1 against the adjacent colours, measured.
- [ ] **The look is chosen by the user from a preview** (2–3 options, e.g. a stronger-alpha ring, a solid 2px ring
      with a gap, a solid outline) before the tokens change: this restyles every focusable control in every consumer.
- [ ] The change is made in `tokens.json` and generated; token-parity CI passes.
- [ ] Playground check measuring the indicator on a representative set (an input, a checkbox, a table sort button, a
      kanban card) in every theme, red before the change.
- [ ] CHANGELOG entry naming the visual change.

## Out of scope

- The dead sync button (done locally at TASK-982).

## Human test plan

- [ ] Tab through a Symbio form and list page in light and dark: every focused control is plainly marked.
