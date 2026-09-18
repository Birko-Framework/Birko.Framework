---
id: TASK-054
parent: EPIC-015
feature: FEATURE-015
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

# Xaml restyled Slider (Tier-1 gap) + `Range` Form field type

## Context

Birko.Xaml has **no slider/range control** — verified 2026-07-06: no `Slider` ControlTheme in
`Birko.Xaml.Avalonia/Controls`, no range type in Core, and `Forms.FieldType` has no range entry. This
is a genuine **Tier-1 gap** (STORY-034 restyled ~20 native controls but skipped `Slider`).

Good news: Avalonia's native `Slider` supports `Orientation="Vertical"` out of the box, so this is a
*restyle + expose* job, not building slider mechanics. Deliver:
- a token-styled `Slider` ControlTheme (track/thumb/ticks via `--b-*`), working **horizontal and vertical**;
- a `Range` (slider) `FieldType` in `Forms.FormField` + the `Form` control mapping, with min/max/step
  (FormField currently lacks these numeric bounds — add them);
- a gallery demo including a **vertical equalizer bank** (the ask that surfaced this).

Web counterpart: **TASK-053** (`b-range` vertical orientation). Part of the broader Xaml Form
field-type parity gap — see the field-type-parity task if filed.

## Acceptance criteria

- [x] Token-restyled `Slider` ControlTheme in `Inputs.axaml`, horizontal + vertical, `{DynamicResource B*}` only. — custom `Track`+`Thumb`+RepeatButton template; cross-axis thickness/alignment set per orientation via `:horizontal`/`:vertical` styles; sub-themes `BSliderRepeat`/`BSliderThumb`.
- [x] `Forms.FieldType.Range` (+ the existing `Min`/`Max`/`Step`) and `Form` renders it two-way bound. — `Form.cs` Range case → `Slider` (Min/Max, Step→SmallChange/TickFrequency/snap), `RangeBase.Value` bound to the model prop.
- [x] Gallery: Slider demo + a vertical equalizer bank (6 stacked); headless tests assert the theme applies (both orientations) + value binds. — `FormFieldTypesTests` (Range bind, `Slider_uses_the_birko_template` theory, equalizer screenshot).
- [x] `Recent Updates` entry; `Birko.Xaml.Core` stays Avalonia-free (FieldType/bounds neutral; the control is the Avalonia view — Core suite 41 green).

## Out of scope

- The web `b-range` vertical work (TASK-053).
- The other missing Xaml field types (date/time, multi-select, tags, file, …) — track separately.

## Human test plan

- [x] Confirm the bound value updates and a vertical bank aligns like an equalizer. — Range-field bind test +
      `slider-equalizer.png` (6 vertical sliders: grey rail, primary fill bottom→thumb, white thumbs at their
      values); gallery launched clean (25s) with the horizontal + equalizer demo. Cross-theme visual left to the eye.

## Implementation plan

_Populated by `/tasks plan TASK-054` — leave empty until then._
