---
id: TASK-041
parent: STORY-028
feature: null
status: done  # todo | in-progress | review | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-06-19
depends-on: [TASK-039]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Extract a shared `coerceCssLength` helper and fix the unitless-length bug across components

## Context

[[TASK-039]] fixed the unitless-`height` bug locally in `b-chart` and flagged a shared helper as a
follow-up *"if more than one component needs it"*. An audit confirmed it does: **six** components
interpolate a consumer-supplied length attribute raw into an inline `style`, so a bare number
(`height="160"`, `width="120"`, `max-height="400"`) produces an invalid declaration the browser
drops — and a `height:100%`/`width:100%` child then stretches unboundedly.

Affected: `b-chart` (`height`), `b-skeleton` (`width`/`height`/`size`), and `b-json-viewer` /
`b-object-tree` / `b-pre` / `b-xml-viewer` (`max-height`). (`b-progress`, `b-file-upload`,
`cell-renderers` hardcode `%`, so they were never affected.)

## Acceptance criteria

- [x] New pure helper `coerceCssLength(value, unit = 'px')` in `Birko.Web.Core` (`src/css/length.ts`), exported from the package barrel — bare number → `${n}${unit}`; explicit units / keywords / `var()` / `calc()` / nullish pass through (nullish → `''`).
- [x] `BaseComponent.lengthAttr(name, fallback, unit = 'px')` convenience (sibling to `numAttr`) that reads an attribute and coerces it.
- [x] `b-chart` refactored to `this.lengthAttr('height', '300px')`; its local `_cssHeight()` removed.
- [x] `b-skeleton` (`width`/`height`/`size`) and the four viewers (`max-height`) routed through `lengthAttr` — closing the same latent bug.
- [x] No behavior change for existing valid inputs; the playground bundles cleanly (`node build.js` green).

## Out of scope

- Migrating the playground's own `height="180px"` workaround back to `"160"` (that's [[TASK-039]]'s verification step).
- Auditing `Birko.Web.Shell` for the same pattern — file separately if found.

## Human test plan

- [x] Shared with [[TASK-039]]: in the playground, a `b-chart` / `b-skeleton` given a unitless size renders at a fixed size and does not stretch. (Headless check 2026-07-17: `b-skeleton width="180"` → inner `width:180px`.)
- [x] A viewer (`b-json-viewer` etc.) with `max-height="400"` scrolls its body at 400px. (Headless check 2026-07-17: unitless `max-height="400"` → inner `max-height:400px`, `overflow-y:auto`, clientHeight ~421 vs scrollHeight 3850.)
