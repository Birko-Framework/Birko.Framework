---
id: TASK-054
parent: EPIC-015
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

- [ ] Token-restyled `Slider` ControlTheme in `Birko.Xaml.Avalonia/Controls` (Inputs), horizontal + vertical, `{DynamicResource B*}` only.
- [ ] `Forms.FieldType.Range` (+ `Min`/`Max`/`Step` on `FormField`) and `Form` renders it two-way bound to the model property.
- [ ] Gallery: a Slider demo + a vertical equalizer bank (≥4 stacked); headless test asserts the theme applies + value binds.
- [ ] `Recent Updates` entry; keep `Birko.Xaml.Core` Avalonia-free (FieldType/bounds are neutral; the control is the Avalonia view).

## Out of scope

- The web `b-range` vertical work (TASK-053).
- The other missing Xaml field types (date/time, multi-select, tags, file, …) — track separately.

## Human test plan

- [ ] In the gallery, drag horizontal + vertical sliders and confirm the bound value updates; confirm a
      4-up vertical bank aligns like an equalizer across themes.

## Implementation plan

_Populated by `/tasks plan TASK-054` — leave empty until then._
