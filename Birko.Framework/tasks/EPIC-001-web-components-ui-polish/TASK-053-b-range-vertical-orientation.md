---
id: TASK-053
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-range: vertical orientation (equalizer-style slider)

## Context

`b-range` (`Birko.Web.Components/src/inputs/b-range.ts`) is **hardcoded horizontal** — verified
2026-07-06: no `orientation`/`vertical` in `observedAttributes`; `.range-slider` is `height:1.5rem`,
the track/fill are pinned `left:0;right:0` at `top:50%`, the native `<input type="range">` is
`width:100%`, and `_pct()` / `_updateFill()` compute `left%`/`width%`. So there is no way to stack it
as a mixer/equalizer bank today.

Add an opt-in vertical mode so a row of `b-range`s reads like an equalizer. This is a real change, not
a CSS flip:
- native input needs `writing-mode: vertical-lr` (modern) — avoid the deprecated `appearance: slider-vertical`;
- the custom `.range-track` / `.range-track-fill` overlay needs `top`/`height` geometry instead of `left`/`width`
  (i.e. `_updateFill` + the inline fill styles branch on orientation);
- a compact, slider-only layout (reuse `display="slider"`) so several stack cleanly with aligned baselines.
- A blunt `transform: rotate(-90deg)` is explicitly rejected — it breaks pointer hit-geometry + the number inputs.

Xaml counterpart: **TASK-054** (Birko.Xaml has no slider at all yet).

## Acceptance criteria

- [ ] `orientation` attribute (`horizontal` default | `vertical`) added to `observedAttributes` + render.
- [ ] Vertical mode: native input via `writing-mode: vertical-lr`; track/fill overlay geometry switches to
      top/height; single **and** dual (range) modes both work vertically.
- [ ] A slider-only vertical variant stacks into an equalizer bank with aligned tracks (demo proves ≥4 side by side).
- [ ] Horizontal behaviour + markup unchanged (default path untouched); keyboard + `aria` intact in both orientations.
- [ ] Playground gallery card shows a vertical equalizer bank; component README design-token/attribute table updated.

## Out of scope

- The Xaml vertical slider (TASK-054).
- New value semantics — only orientation/layout changes; `value`/`change` contract is unchanged.

## Human test plan

- [ ] In the playground, drag a vertical slider and confirm value tracks correctly top→bottom (and both thumbs in range mode);
      confirm keyboard arrows work; confirm a 4-up bank aligns like an equalizer at a mobile + desktop width.

## Implementation plan

_Populated by `/tasks plan TASK-053` — leave empty until then._
