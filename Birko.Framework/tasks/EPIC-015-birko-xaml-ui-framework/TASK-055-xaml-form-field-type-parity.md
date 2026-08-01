---
id: TASK-055
parent: EPIC-015
feature: FEATURE-015
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

- [x] `FieldType` gains `Switch, Markdown, Password, Email, Search, Percent, Radio, OptionGroup`; `Form` renders each via the named existing control, two-way bound. — `Form.cs` switch: Switch→ToggleSwitch, Markdown→MarkdownEditor, Password→TextBox(PasswordChar), Radio/OptionGroup→RadioButton group (equality-converter binding), Number/Percent→clamped TextBox, Email/Search→TextBox.
- [x] `FormField` gains `Min` / `Max` / `Step` / `Default` / `Hint` (neutral, in Core). — `FormField.cs`; Min/Max clamp on commit for Number/Percent, `Step` carried for slider/spinner consumers (TASK-054), `Default` seeds a null model prop, `Hint` renders muted under the field.
- [x] `Birko.Xaml.Core` stays Avalonia-free. — `CoreIsAvaloniaFreeTests` green (Core suite 41).
- [x] Tests + gallery. — the `Form` control is Avalonia-side, so tests landed in **`Birko.Xaml.Avalonia.Tests`** (`FormFieldTypesTests`, 8 headless: render-type per field, switch/radio write-back, initial-selection, default-applied, numeric clamp, hint). Gallery `DemoForm` now exercises Email/Password/Switch/Radio/Number+Hint. (Core side is pure data — nothing behavioural to test there.)
- [x] `Recent Updates` entry.

## Out of scope

- **Range** slider field type → TASK-054.
- **Date / DateTime / Time / DateRange** → TASK-056 (no Xaml date/time pickers exist yet).
- **MultiSelect / Tags / File** → need new Tier-1/2 controls; file separately when prioritized.
- `custom` (arbitrary-template escape hatch) — a design decision, deferred.

## Human test plan

- [x] Render a Form exercising each new field type; confirm two-way binding, defaults, and Number/Percent
      Min/Max clamp. — covered by the 8 headless `FormFieldTypesTests`; gallery launched clean (30s, no runtime
      error) with the enhanced DemoForm. Required-asterisk is pre-existing Form behaviour (unchanged).

## Implementation plan

_Populated by `/tasks plan TASK-055` — leave empty until then._
