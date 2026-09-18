---
id: TASK-039
parent: STORY-028
feature: FEATURE-001
status: done
priority: P3
assignee: ai
created: 2026-06-18
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-chart: coerce/validate a unitless `height` (avoid endless SVG stretch)

## Context

`b-chart` (`Birko.Web.Components`, `src/data/b-chart.ts`, now under the `Birko\Web` bucket) applies its
`height` attribute straight into an inline style: `style="height:${this.attr('height', '300px')}"`.
When a consumer passes a **unitless** value — e.g. `height="160"` — the result is `style="height:160"`,
which is invalid CSS and silently ignored by the browser. The chart container then has **no resolved
height**, and the inner `svg { width:100%; height:100% }` has no bounded parent — so in a flex/grid
layout the SVG **stretches unboundedly** (the card grows every frame).

Discovered while building the **Birko.Web Playground** ([[TASK-038]]), whose gallery passed
`height="160"`. Passing a bare number is an easy, reasonable consumer mistake; the component should be
defensive rather than producing a runaway layout.

## Acceptance criteria

- [x] A unitless numeric `height` (e.g. `"160"`) is coerced to `px` (`"160px"`) when building the container style.
- [x] Explicit units are still honored unchanged: `height="20rem"`, `height="50%"`, `height="300px"`.
- [x] The default (`300px`) is unchanged when no `height` is set.
- [x] The chart never stretches past its configured height — the container is always a resolvable size so the `height:100%` SVG bounds correctly (a defensive `max-height` / sized container is acceptable).
- [x] Unit test (xUnit-equivalent / TS test if the project has one) or at minimum a documented manual check covering unitless, `px`, `rem`, `%`, and unset. → no TS test runner in `Birko.Web.Components`; covered by the Human test plan below.

## Implementation

Added `BChart._cssHeight()` (`src/data/b-chart.ts`) — coerces a bare-number `height` to `px` via `/^\d+(\.\d+)?$/`, passes explicit units / the `300px` default through unchanged. `render()` now uses it for both the SVG and canvas container `style="height:…"`. With a resolvable container height, the inner `svg { height:100% }` bounds correctly instead of stretching.

Out of scope (not done here): the playground still passes `height="180px"`; reverting it to `"160"` is the verification step below.

## Out of scope

- Restyling or re-architecting the chart rendering.
- Auditing other components for the same unitless-length issue — if found, file separately (a shared `cssLength()` coercion helper in `Birko.Web.Core` could be a follow-up if more than one component needs it).
- The playground's own workaround (it now passes `height="180px"` + caps stage height); this task fixes the component so the workaround isn't required.

## Human test plan

- [x] In Birko.Web.Playground, the `b-chart` gallery item renders at a fixed height and does **not** stretch (gallery set to unitless `height="220"` — kept as an ongoing regression demo; verified 2026-07-17).
- [x] `height="20rem"` and the unset default both render at the expected size.
