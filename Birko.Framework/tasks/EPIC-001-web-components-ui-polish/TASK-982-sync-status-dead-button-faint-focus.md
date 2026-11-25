---
id: TASK-982
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-977]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-sync-status`: the dead button's focus indicator is too faint to see

## Context

Reported by Reps (workouttracker, 2026-11-25), found while running TASK-977's keyboard step in desktop Chromium on a
real dead write. The "N not saved" button (`button.dead`, TASK-977) has `outline: none` and
`box-shadow: var(--b-focus-ring-danger)`, which the base tokens set to `0 0 0 3px rgba(220, 38, 38, 0.15)` (the dark
theme 0.25). On the pink dead chip in light mode, focused and unfocused look almost the same: well below the 3:1
contrast a focus indicator needs (WCAG 1.4.11 / 2.4.11). `b-button` right beside it shows a clear ring.

The same 15% ring tokens (`--b-focus-ring`, `--b-focus-ring-danger` in `css/tokens.css`) are used by other controls
(`:focus-visible` in the shared sheet, `.toggle-control`, `b-checkbox`, table sort buttons, kanban cards). Measure
whether they are equally faint on their own backgrounds before choosing between a local fix and a token change.
`tokens.css` and the themes are generated from `Birko.DesignTokens/tokens.json`: a token change is made there.

## Acceptance criteria

- [ ] `button.dead:focus-visible` shows a ring at least as visible as `b-button`'s, with 3:1 contrast against the dead
      chip and the page background, in light and dark (and the other shipped themes).
- [ ] The other users of the faint ring tokens are measured and either fixed in the same change or filed, with the
      numbers.
- [ ] Playground check measuring the focused vs unfocused contrast of the dead button in both themes, red before the
      change.
- [ ] CHANGELOG entry.

## Out of scope

- The dead button's look when not focused.

## Human test plan

- [ ] In Reps (or the Playground `b-sync-status` card with a dead count), Tab to the "N not saved" button in light and
      dark: the focus ring is plainly visible on the pink chip.
