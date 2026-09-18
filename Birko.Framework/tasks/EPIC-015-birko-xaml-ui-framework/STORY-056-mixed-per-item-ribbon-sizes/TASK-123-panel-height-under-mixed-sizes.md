---
id: TASK-123
parent: STORY-056
feature: FEATURE-015
status: todo
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

- [ ] The rule for deriving `--b-ribbon-panel-height` under mixed sizes is written down, with the
      Clipboard example worked through
- [ ] Decided and recorded: whether the framework exposes the computed height or the consumer derives
      it — and if the latter, why the former was not possible
- [ ] The derivation accounts for **every reachable configuration**, not the preferred one — the
      Symbio TASK-306 failure is the named regression case
- [ ] A playground check renders a mixed group at a width that forces degradation and asserts no
      clipping, at more than one width
- [ ] The docs beside `--b-ribbon-panel-height` carry the rule
- [ ] A follow-up task is filed **in Symbio's own tasks tree** to extend
      `ribbon-clipping-check.spec.ts` to mixed groups, and cross-linked here

## Out of scope

- Editing Symbio. Cross-link and file there; this repo is the aggregator and only hosts cross-cutting
  work.
- Making the panel scroll. The ribbon body resizes, it never scrolls — a scroll offset destroys the
  spatial memory the ribbon exists to provide.

## Human test plan

- [ ] Narrow a window carrying a mixed-size ribbon from wide to ~1024px in steps, watching for a
      clipped row. This is the exact failure Symbio shipped: it looked perfect at 1600px, so a check at
      one comfortable width proves nothing. Do it on both skins.
