---
id: TASK-055
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

# Xaml Form field-type parity with b-form (wire existing controls + FormField props)

## Context

The schema-driven Xaml `Form` supports only **5** field types — `Text, TextArea, Number, Checkbox,
Select` (`Birko.Xaml.Core/Forms/FormField.cs`) — vs `b-form`'s ~22. This task closes the **cheap half**
of the gap: the field types whose backing Avalonia control **already exists**, so it's wiring + a
`Form` mapping, not new controls. It also fills the `FormField` prop gaps that even the existing types
need.

Wire these (existing control in parentheses):
- `Switch` (`ToggleSwitch`, Tier-1) · `Markdown` (`MarkdownEditor`, Tier-2)
- `Password` / `Email` / `Search` (`TextBox` variants — PasswordChar / input semantics)
- `Percent` (a `Number` variant) · `Radio` / `OptionGroup` (`RadioButton`, needs a small radio-group items wrapper)

`FormField` prop gaps to add (present on `b-form` fields, absent in Xaml): `Min` / `Max` / `Step`
(numeric/range), `Default`, `Hint`. Keep `Birko.Xaml.Core` Avalonia-free — `FieldType` + props are
neutral; only the `Form` control (Avalonia) grows the new render branches.

## Acceptance criteria

- [ ] `FieldType` gains `Switch, Markdown, Password, Email, Search, Percent, Radio, OptionGroup`; `Form` renders each via the named existing control, two-way bound.
- [ ] `FormField` gains `Min` / `Max` / `Step` / `Default` / `Hint` (neutral, in Core).
- [ ] `Birko.Xaml.Core` stays Avalonia-free (enforced by the existing `CoreIsAvaloniaFreeTests`).
- [ ] Tests in `Birko.Xaml.Core.Tests` (schema→field mapping, defaults) + a gallery Form showing the new types.
- [ ] `Recent Updates` entry.

## Out of scope

- **Range** slider field type → TASK-054.
- **Date / DateTime / Time / DateRange** → TASK-056 (no Xaml date/time pickers exist yet).
- **MultiSelect / Tags / File** → need new Tier-1/2 controls; file separately when prioritized.
- `custom` (arbitrary-template escape hatch) — a design decision, deferred.

## Human test plan

- [ ] In the gallery, render a Form exercising each new field type; confirm two-way binding, required
      asterisk, defaults, and that a Percent/Number field honours Min/Max/Step.

## Implementation plan

_Populated by `/tasks plan TASK-055` — leave empty until then._
