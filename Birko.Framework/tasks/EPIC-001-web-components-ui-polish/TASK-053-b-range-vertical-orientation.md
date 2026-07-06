---
id: TASK-053
parent: EPIC-001
feature: null
status: done
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

- [x] `orientation` attribute (`horizontal` default | `vertical`) added to `observedAttributes` + render.
- [x] Vertical mode: native input via `writing-mode: vertical-lr` (+ `direction: rtl` so up = more); track/fill
      geometry switches to bottom/height (`_fillStyle` + `_updateFill` branch on orientation). Single verified;
      dual (range) shares the same from/to→bottom/height geometry. **Key fix:** the vertical input is `position:absolute`
      so its large vertical min-content height doesn't blow out the layout (was 600px → uniform).
- [x] A slider-only vertical variant stacks into an equalizer bank (demo: `pg-equalizer`, 5 side by side).
- [x] Horizontal behaviour + markup unchanged — default path untouched; existing `b-range` renders fine (verify.mjs, none-empty).
- [x] Playground gallery card shows the vertical equalizer bank; README `b-range` attribute table + example updated.

## Out of scope

- The Xaml vertical slider (TASK-054).
- New value semantics — only orientation/layout changes; `value`/`change` contract is unchanged.

## Human test plan

- [x] Confirm value→position maps up=more and a bank aligns like an equalizer. — headless Chromium screenshot
      (`equalizer-web.png`): 5 vertical sliders, grey rail, blue fill bottom→thumb, thumbs at [30,55,80,45,65]
      matching their values; uniform 112px sizing (the min-content bug fixed). Live drag/keyboard is native
      `<input type=range>` behaviour (web has no unit runner — see TASK-052); cross-viewport left to the eye.

## Implementation plan

_Populated by `/tasks plan TASK-053` — leave empty until then._
