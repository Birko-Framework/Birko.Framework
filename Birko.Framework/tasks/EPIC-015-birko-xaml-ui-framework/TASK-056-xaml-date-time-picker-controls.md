---
id: TASK-056
parent: EPIC-015
feature: null
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Xaml date & time picker controls + field types

## Context

The standout Xaml Form gap: **no date/time pickers exist at all**. `b-form` (Web) has four —
`b-date-picker` / `b-datetime-picker` / `b-time` / `b-date-range-picker` — with no Xaml counterpart,
and `Forms.FieldType` has no date/time entries. These are real controls (not just field-type wiring),
which is why they're split out from the field-type-parity task (TASK-055).

Avalonia ships natives to restyle rather than build from scratch: **`CalendarDatePicker`** and
**`TimePicker`** (and `Calendar`). A **date-range** picker has no native and is a custom composite
(two calendars / a range-aware calendar), mirroring the web `b-date-range-picker`.

## Acceptance criteria

- [x] Token-restyled `CalendarDatePicker` + `TimePicker` ControlThemes (`Inputs.axaml`). — **light restyle**: `BasedOn` Fluent's theme + Birko token setters on the resting surface (border/bg/radius/font); resolves at runtime (whole theme loads, 111 tests green). The flyout calendar/clock **internals keep Fluent** — a full grid re-template is a deferred follow-up.
- [x] A `DateTime` composite (date + time) and a `DateRange` composite (start/end). — `Form.BuildDateTime` (date+time pickers, handlers recombine into one `DateTime?`) and `BuildDateRange` (two pickers over a shared `DateRange` value class, seeded when null).
- [x] `Forms.FieldType` gains `Date` / `Time` / `DateTime` / `DateRange`; `Form` renders each two-way bound (Core Avalonia-free — suite 41). — Date→`CalendarDatePicker.SelectedDate`, Time→`TimePicker.SelectedTime`; composites via handlers. `Forms.DateRange` value type added to Core.
- [x] Localization follows the active culture. — the native pickers format per `CultureInfo.CurrentCulture` automatically (screenshot shows `5. 1. 2026`); no explicit Formatter wiring needed.
- [x] Tests + gallery. — `FormFieldTypesTests`: Date/Time bind, DateTime combine, DateRange seed+write, + a datetime-fields screenshot (Avalonia suite **106→111**). Gallery `DemoForm` shows all four.
- [x] `Recent Updates` entry.

## Out of scope

- The cheap field-type wire-ups (TASK-055) and the Range slider (TASK-054).
- A bespoke calendar-rendering engine — restyle Avalonia's natives; only date-range is custom.

## Human test plan

- [x] Pick a date / time / datetime / range and confirm the bound model updates + culture display. — covered by
      4 binding tests + the `datetime-fields.png` screenshot (culture-formatted `5. 1. 2026`, composites laid out,
      tokenized rounded borders); gallery DemoForm exercises all four. Cross-theme visual + flyout polish left to the eye.

## Implementation plan

_Populated by `/tasks plan TASK-056` — leave empty until then._
