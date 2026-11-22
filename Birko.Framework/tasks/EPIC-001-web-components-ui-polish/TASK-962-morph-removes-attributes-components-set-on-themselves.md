---
id: TASK-962
parent: EPIC-001
feature: null
status: in-progress
priority: P1
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A page that redraws removes attributes its components set on themselves

## Context

Reported by DraCode (2026-11-22, its TASK-183), on Birko.Web v0.4.21: when a page re-renders, the DOM morph removes
every attribute on a child element that the page's new render does not contain. b-kanban sets `stacked` on itself, so
a page redraw un-stacks the board visually while it still thinks it is stacked (DraCode works around it).

It is wider than b-kanban. Components reflect their own state as attributes on themselves: `b-sync-status` `hidden`,
`b-tabs` / `b-ribbon` `active`, `b-sidebar` `collapsed`, `b-ribbon` `expanded`/`pinned`, `b-split-panel` widths,
`b-checkbox`/`b-radio`/`b-switch` `checked`, the inputs' `value`. A page whose render does not declare one of these
loses it at its next redraw. TASK-791 already special-cased a dialog's `open` for the same reason.

## Acceptance criteria

- [ ] An attribute a component set on itself survives its page's redraw; an attribute the page rendered and then
      stops rendering is still removed, and one the page renders with a new value still updates.
- [ ] Covered at least: b-kanban `stacked`, b-sync-status `hidden`, b-tabs `active`, b-checkbox `checked`.
- [ ] No component gains listeners or loses state on re-render (rerender-leak, the full Playground suite).
- [ ] Playground check: red before, green after.

## Human test plan

1. In the Playground, narrow the gallery kanban until it stacks, then trigger a re-render of its page (switch the
   theme or locale): it stays stacked.
2. Pick a tab in a b-tabs whose page does not set `active`, then re-render the page: the picked tab stays.

## Implementation plan

1. Web.Core's morph records, per element, which attribute names its parent rendered (on the first render, by walking
   the render's template beside the live tree; on each morph, from the fresh element; for cloned-in nodes, from the
   clone). On a redraw an attribute is removed only when the parent rendered it last time and no longer does; an
   element with no record keeps today's behavior.
2. The dialog `open` special case stays (a modal opened before its first record).
3. Checks in the Playground: a parent BaseComponent rendering each covered component, the component setting its own
   attribute, the parent redrawing.
