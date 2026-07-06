---
id: TASK-056
parent: EPIC-015
feature: null
status: todo
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

- [ ] Token-restyled `CalendarDatePicker` + `TimePicker` ControlThemes in `Birko.Xaml.Avalonia/Controls` (`{DynamicResource B*}` only).
- [ ] A `DateTime` composite (date + time) and a `DateRange` composite (start/end) — custom controls over the above.
- [ ] `Forms.FieldType` gains `Date` / `DateTime` / `Time` / `DateRange`; `Form` renders each two-way bound (Core stays Avalonia-free).
- [ ] Localization: month/day names + formats follow the active culture (compose with the `Formatter`/`I18n` from TASK-044 where relevant).
- [ ] Tests (theme applies + value binds per control) + a gallery section showing all four across themes.
- [ ] `Recent Updates` entry.

## Out of scope

- The cheap field-type wire-ups (TASK-055) and the Range slider (TASK-054).
- A bespoke calendar-rendering engine — restyle Avalonia's natives; only date-range is custom.

## Human test plan

- [ ] In the gallery, pick a date, a time, a datetime, and a start/end range; confirm the bound model
      values update and the display respects the active culture; check across all four themes.

## Implementation plan

_Populated by `/tasks plan TASK-056` — leave empty until then._
