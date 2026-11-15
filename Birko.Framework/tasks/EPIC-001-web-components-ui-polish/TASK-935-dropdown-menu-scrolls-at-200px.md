---
id: TASK-935
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: [TASK-934]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-dropdown-menu` scrolls at 200 px

Reported by the Symbio session for Symbio TASK-1071. Its user menu has 6 rows, 2 dividers and a TASK-934 heading
(~240 px), and it scrolled on a desktop screen. `b-dropdown-menu` used the shared `.dropdown-panel` sheet, whose
`max-height: 12.5rem` suits a long pick list, not an action menu. The menu was its only user: `b-select` and
`b-multi-select` carry their own 12.5rem caps, which stay.

## Change (Birko.Web v0.4.16)

- `_positionMenu` measures the menu at its natural height and caps it at the room on the side it opens: below, or
  above when it flips. It keeps 8 px clear of the viewport edge and never goes below 96 px. A menu that fits shows every
  row; only a taller one scrolls, and it stays inside the viewport.
- The shared sheet's fixed cap became a viewport fallback (`calc(100vh - 1rem)`). It gained `box-sizing: border-box`,
  so the cap includes padding and border. Without that the panel overran its computed room by its padding (measured:
  bottom 601 of 600).

## Evidence

Playground backport-smoke:
- On Birko.Web main, the 10-row check fails (clientHeight 182 of a 302 px scrollHeight).
- With the change it passes 358/358, 3 of 3 runs, with the 10-row menu at 304/304 px and the 40-row menu ending at
  592 of 600 and scrolling. verify.mjs reports 0 failing checks.

keyboard-check passes 225/225, a11y-name-check passes, and Components `tsc` is clean. The user confirmed the look from
a screenshot of a 10-row menu (2026-11-15).

One unrelated run showed `TASK-133: focusField puts focus on a member` failing once. It passed on main just before and
on all 3 reruns, and that check runs before this task's block, so it is recorded here as a one-off, not adopted as a
cause (rule 54).

## Human test plan

1. In Symbio (or the Playground), open a dropdown menu with about 10 rows on a desktop screen. All rows show and there
   is no scrollbar.
2. Shrink the window until the menu cannot fit. It flips above the trigger when there is more room there, stays on
   screen, and scrolls.
