---
id: TASK-123
parent: STORY-056
feature: FEATURE-015
status: done
priority: P2
assignee: ai
created: 2026-07-31
depends-on: [TASK-121, TASK-122]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Panel height under mixed sizes, and extending the clipping guard

## Context

Uniform sizes make panel height easy: every item in a group is the same size, so the group's height is
the variant's height. Mixed sizes break that — **required height becomes the height of the tallest
column**, and a group's tallest column depends on which configuration the ladder picked.

There is already a field-proven failure here. **Symbio TASK-306**: the consumer sized
`--b-ribbon-panel-height` for the *preferred* variant, which looked perfect at 1600px and **clipped two
links at 1024px**, because the ribbon had degraded to a variant the height was never sized for. The rule
that came out of it is the one this task must generalise:

> Size the panel for the tallest variant the ribbon can **reach**, not the one it prefers.

Mixed sizes make that harder to compute by eye, which is precisely why the derivation has to be written
down rather than left to each consumer.

## Approach

Derive the height from the ladder, not from the preferred configuration: the required panel height is
the maximum, over every configuration the group can *reach*, of that configuration's tallest column.
`popup` is a floor, not an exception — a collapsed group is short, so it never drives the maximum.

Two things worth settling while here:

- **Can the framework compute it?** If the ladder is finite and enumerable (which TASK-119's
  determinism requirement should guarantee), the reachable set is known and the framework can expose
  the number rather than making every consumer derive it. That is strictly better than documenting a
  formula — a documented formula is a thing consumers get wrong at 1024px.
- **If it cannot**, then document the derivation concretely, with the Clipboard example worked through,
  and say which measurements the consumer needs.

Either way the answer belongs in the ribbon docs next to `--b-ribbon-panel-height`, because the token
is where a consumer meets the problem.

## Extending Symbio's guard

`tests/ui-e2e/ribbon-clipping-check.spec.ts` in Symbio already sweeps **tabs, tokens and widths** —
that breadth is what catches this class, and it needs extending to mixed groups. Note this is a
consumer-repo change: file it in Symbio's own `tasks/` tree (the polyrepo rule — single-sub-project work
stays in that sub-repo), and cross-link from here rather than editing it from this repo.

The framework side needs its own coverage that does not depend on a consumer: the playground should
carry a mixed group at a width that forces degradation, and assert nothing clips.

## Acceptance criteria

- [x] The rule for deriving `--b-ribbon-panel-height` under mixed sizes is written down, with the
      Clipboard example worked through
- [x] Decided and recorded: whether the framework exposes the computed height or the consumer derives
      it — and if the latter, why the former was not possible
- [x] The derivation accounts for **every reachable configuration**, not the preferred one — the
      Symbio TASK-306 failure is the named regression case
- [x] A playground check renders a mixed group at a width that forces degradation and asserts no
      clipping, at more than one width
- [x] The docs beside `--b-ribbon-panel-height` carry the rule
- [x] A follow-up task is filed **in Symbio's own tasks tree** to extend
      `ribbon-clipping-check.spec.ts` to mixed groups, and cross-linked here: **Symbio TASK-1133**
      (`tasks/EPIC-026-ui-component-extensions/TASK-1133-ribbon-clipping-spec-covers-mixed-size-groups.md`, filed
      2026-11-25 by the Symbio session; waits for v0.5.7 pushed and a Symbio rebuild)

## Decision

**The framework exposes the number** (web): `b-ribbon.requiredPanelHeight`, and one `console.warn` per
(needed, ceiling) pair when `--b-ribbon-panel-height` is below it. It was possible because the ladder is finite and
`_measureAndScale` already renders every group at every step in a probe to get the widths, so the heights come from the
same pass at no extra layout cost. Only the steps the ladder can reach count: `_groupStart(group)` (the preferred step,
raised by `minSize`) and tighter; a roomier step is never shown and counting it over-estimated (measured 113 vs the
correct 112). A documented formula alone lost because, as this task says, a formula is what a consumer gets wrong at
1024px. **Avalonia gets no property:** its `RibbonGroupsPanel` has no ceiling (the panel is as tall as the tallest
picked variant), so it cannot clip; the README says so.

## Evidence

Birko.Web `task/TASK-123` 4790fee (joins v0.5.7), Playground c521e41.
- The ceiling is read from the token through the measurement probe, not from the panel's `max-height`, which animates
  over 300ms on expand: read mid-animation it gave a false warning at 101px and, deduped on the needed height alone,
  suppressed the real one (both seen, fixed).
- Clipboard measured at default tokens (7px panel padding top and bottom): `large` 71px, `medium` 98px, `small`
  98px, so 112px needed; a `large`-preferring ribbon shows an 85px panel at 900px and needs 113px (with border) at 600px,
  the TASK-306 shape. Worked into the README beside the token.
- `ribbon-mixed-check` 18/18: a 900→200px sweep finds no command below the panel's bottom (descendants compared, since
  groups stretch to the panel and the group box never sticks out); `requiredPanelHeight` 112 ≤ the 112px default
  ceiling; no warning at the default; a 60px token is warned about ("is 60px, but this tab needs 112px") and the sweep
  sees the cut. **Red** on Birko.Web main: 16/18, the two `requiredPanelHeight` / warning checks.
- ribbon-a11y 22/22, category-ribbon 19/19, contextual-ribbon 13/13, verify 0 failing (one navigation timeout, passed on
  re-run and on main), Components `tsc` clean.

## Out of scope

- Editing Symbio. Cross-link and file there; this repo is the aggregator and only hosts cross-cutting
  work.
- Making the panel scroll. The ribbon body resizes, it never scrolls — a scroll offset destroys the
  spatial memory the ribbon exists to provide.

## Human test plan

- [x] Narrow a window carrying a mixed-size ribbon from wide to ~1024px in steps, watching for a
      clipped row. This is the exact failure Symbio shipped: it looked perfect at 1600px, so a check at
      one comfortable width proves nothing. Do it on both skins.

**Human test passed 2026-11-25** (the user, both skins: "seem to be working"). Merged below.

**Merged 2026-11-25** on the user's word ("merge") into Birko.Web main f6c30c3, local tag v0.5.7 moved onto it (not pushed). On main: Components `tsc` clean, ribbon-mixed 18/18, ribbon-a11y 22/22, category-ribbon 19/19, contextual-ribbon 13/13, verify 0 failing.

**Field result 2026-11-26 (Symbio TASK-1133, done on Symbio's local main).** On its first run `requiredPanelHeight` caught a real gap: every Symbio tab needed 118px against Symbio's `--b-ribbon-panel-height: 8rem` (112px), and the warning fired. A `small` group (three rows) needs 104px plus 14px padding; at 769–1024px the first group overflowed its own 98px box by 7px (scrollHeight 105 vs 98). Nothing important sat there, so nothing looked wrong, and Symbio's older panel / track overflow checks never measured a group's own overflow. Symbio raised its token to 8.5rem (119px). Its spec now adds a mixed Clipboard fixture and checks, on every tab at 1600 / 1280 / 1024 / 900px, no command below the panel, `requiredPanelHeight` ≤ the token, no warning, and that sizes really mixed; mutation back to 8rem is red, naming the tab and the 118px. Gate 3: 578 tests, 0 failed.
