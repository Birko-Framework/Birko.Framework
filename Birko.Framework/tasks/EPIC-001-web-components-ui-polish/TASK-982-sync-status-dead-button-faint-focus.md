---
id: TASK-982
parent: EPIC-001
feature: null
status: done
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

- [x] `button.dead:focus-visible` shows a ring at least as visible as `b-button`'s, with 3:1 contrast against the dead
      chip and the page background, in light and dark (and the other shipped themes).
- [x] The other users of the faint ring tokens are measured and either fixed in the same change or filed, with the
      numbers.
- [x] Playground check measuring the focused vs unfocused contrast of the dead button in both themes, red before the
      change.
- [x] CHANGELOG entry.

## Evidence

Birko.Web `task/TASK-982` 084b3a2 (joins the unpushed v0.5.3), Playground 99c4c17. All from builds that printed
"✓ Built". `button.dead:focus-visible` is now `outline: 2px solid currentColor; outline-offset: 2px` (b-button has no
focus rule of its own, so its clear ring is the browser's default 2px outline; this matches it in weight and keeps
the danger colour).
- `sync-dead-focus-check` 15/15: in light, dark, neon, finstat and inverse, Tab reaches the button, the ring is solid
  2px, and it has 4.55–7.88:1 against the chip and 5.56–7.62:1 against the page. **Red** 5/15 on Birko.Web main
  (`outline: none`, only the box-shadow wash).
- verify 0 failing; keyboard-check 244/244; Components `tsc` clean.
- The shared ring tokens, measured against the page (a 3px wash composited on `--b-bg`): `--b-focus-ring` 1.23:1
  (base), 1.41:1 (dark), 1.26:1 (finstat), 1.99:1 (neon); `--b-focus-ring-danger` 1.26:1 (base), 1.31:1 (dark). All
  below 3:1, and about 35 components use them: filed as [[TASK-983]], since changing them restyles every control.

## Out of scope

- The dead button's look when not focused.

## Human test plan

- [x] In Reps (or the Playground `b-sync-status` card with a dead count), Tab to the "N not saved" button in light and
      dark: the focus ring is plainly visible on the pink chip.
  - _Run 2026-11-25 by Reps (built against task/TASK-982 084b3a2, desktop Chromium, a real server-refused set):_ a
    real Tab from the brand link focuses `button.dead` (`:focus-visible`); light draws a solid 2px rgb(202,35,35)
    outline, dark a solid 2px rgb(248,113,113), both 2px offset; in screenshots focused vs unfocused the ring is
    plainly visible in both themes. Enter still opens Reps' list. Merge waits for the user's word.

**Merged 2026-11-25** on the user's word ("merge") into Birko.Web main 7e7ef37 as part of v0.5.3 (CHANGELOG conflict
with TASK-981 resolved, both kept; the unpushed tag moved). On main after TASK-983 too: `sync-dead-focus-check` 15/15.
