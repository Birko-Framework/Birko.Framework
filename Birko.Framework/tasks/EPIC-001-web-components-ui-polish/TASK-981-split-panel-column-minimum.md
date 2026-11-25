---
id: TASK-981
parent: EPIC-001
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-979, TASK-525]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-split-panel`: a consumer-set minimum width per column, honoured during the drag

## Context

Asked for by DraCode (its TASK-197, 2026-11-25), found while re-running TASK-979. A resizable `b-split-panel` clamps
the separator drag and the arrow keys to a fixed `MIN_COLUMN_PX = 120` per column (`b-split-panel.ts`). DraCode's chat
panel must stay at least 320 px wide, so today it lets the drag go below that and corrects the width on
`split-resize`; with `--b-split-transition` set (TASK-979) that correction animates, so a drop below the minimum
visibly springs back.

## Acceptance criteria

- [x] New attributes `master-min` and `detail-min` (CSS lengths in px; default 120 px each, today's value) set each
      column's minimum; the pointer drag and the arrow keys clamp to them, so the column stops at its minimum while
      dragged and the drop commits a width that already respects it (nothing moves after the drop).
- [x] Works for both shapes TASK-979 handles: a fixed master with a flexible detail, and a flexible master with a
      fixed-length detail.
- [x] When the panel is too narrow for both minimums, the behaviour is defined and documented (e.g. the resizable
      column's own minimum wins over the other's), never a negative or NaN width.
- [x] Doc comment, README and CHANGELOG describe the attributes.
- [x] Playground check (extend `split-transition-check` or a sibling) proving the drag and the keys stop at a set
      minimum, red before the change.

## Evidence

Birko.Web `task/TASK-981` a0083e9 (joins the unpushed v0.5.3), Playground 615638f. All from builds that printed
"✓ Built". `_limits` gives the master's range from `master-min` to what leaves the detail its `detail-min` (px, a bare
number, rem or em; anything else is the 120px default). Too narrow for both: with a fixed-length detail the detail's
minimum wins, otherwise the master's; a width is never negative. Both attributes are observed, so the separator's
`aria-valuemin` / `aria-valuemax` follow them.
- `split-transition-check` 24/24. New: with `detail-min="320px"` a drag stops the detail at 320 px while dragged, the
  drop commits `{1fr, 320px}` and nothing moves after the 400 ms transition; Right and End stop there too;
  aria-valuemax leaves 320 px. `master-min="15rem"` stops the drag and Home at 210 px (the Playground's root is 14px).
  A 360 px panel keeps the fixed detail at 320 px (master 20 px), or the fixed master at 200 px. **Red** 15/24 on
  Birko.Web main (every new case at 120 px).
- verify 0 failing; keyboard-check 244/244; Components `tsc` clean.

## Out of scope

- Maximum widths.
- The responsive collapse (`collapse-at`).

## Human test plan

- [x] In DraCode, with its chat panel's minimum set to 320 px: dragging the divider toward the chat panel stops at
      320 px while dragged, and letting go does not animate a correction.
  - _Run 2026-11-25 by DraCode (built against task/TASK-981 a0083e9, `master-min="320px" detail-min="280px"`):_ a
    mouse drag stops the tabs at 320 px and the chat panel at 280 px while dragged, with no gap at any step, and every
    frame for 400 ms after the drop keeps the width (nothing springs back); Home / End give 320 / 1064 and 1104 / 280,
    and the arrows do not pass a limit. Two new DraCode e2e tests (red without the attributes: 184 / 120 px) pass;
    DraCode suite 187/187. Merge waits for the user's word; DraCode's change waits for it on main.

**Changed before merge, on the user's word ("switch 981 to rem then merge"; Birko.Web mostly uses rem):** the default
minimum is 7.5rem (120px at a 16px root), a drag or a key commits the width in rem to two decimals (`split-resize` and
`persist-key` give `{ master: '1fr', detail: '22.86rem' }`), and the keys step 1rem / 4rem. A px width saved before
still loads. Birko.Web 53adcdf; Playground 2bad042 (`split-transition-check` 26/26, red 15/26 on main;
`layout-persist-smoke` updated to rem, 35/35). **Merged 2026-11-25** into Birko.Web main 4dc4e0c as part of v0.5.3
(the unpushed tag moved); on main split 26/26, overlay-footer 8/8, keyboard 244/244 (243 on one run, then 244 twice),
verify 0 failing. DraCode's handler must accept rem (its TASK-197).
