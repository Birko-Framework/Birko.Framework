---
id: TASK-057
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

# Xaml Form field types: MultiSelect / Tags / File

## Context

The remaining `b-form` field types with **no Xaml counterpart control** (deferred in TASK-055's
out-of-scope): multi-select, tag-input, and file upload. This closes the field-type parity gap by
adding the three controls + wiring them into the schema-driven `Form`.

- **MultiSelect** — pick several of `Options`; a restyled `ListBox` (`SelectionMode=Multiple`, reusing
  the token `ListBoxItem` theme). Binds to an `IList` on the model (synced via SelectionChanged, since
  `SelectedItems` isn't a bindable property).
- **Tags** — freeform chips: a `WrapPanel` of removable chip borders + a `TextBox` (Enter adds,
  Backspace/× removes). Binds to an `IList<string>` on the model (seeded when null).
- **File** — a read-only path `TextBox` + a "Browse…" `Button` that opens `TopLevel.StorageProvider`'s
  file picker and writes the chosen path to a `string` model prop.

Core stays Avalonia-free (the `FieldType` values are neutral; the controls/logic live in the Avalonia `Form`).

## Acceptance criteria

- [x] `FieldType` gains `MultiSelect` / `Tags` / `File`; `Form` renders each, bound to the model.
- [x] MultiSelect: multi-`ListBox` (`SelectionMode=Multiple|Toggle`) over `Options`, synced to an `IList` prop on SelectionChanged; initial model selection reflected.
- [x] Tags: `WrapPanel` chip input over an `IList<string>` (seeded when null) — Enter adds, ✕/Backspace removes; chips re-render on change.
- [x] File: read-only path `TextBox` (bound to a `string` prop) + Browse `Button` via `TopLevel.StorageProvider` (no-op when unsupported/headless).
- [x] Tests: MultiSelect + Tags render/bind headlessly (`FormFieldTypesTests`); File renders; + a screenshot. Gallery DemoForm shows all three. Avalonia suite **111→116**.
- [x] `Recent Updates` entry; `Birko.Xaml.Core` stays Avalonia-free (Core suite 41).

## Out of scope

- Multi-file selection, drag-drop upload, upload progress (File is single-path pick).
- Async/remote option loading and tag validation/autocomplete.

## Human test plan

- [x] MultiSelect/Tags: covered by headless bind tests + screenshot (selection highlighted, chips render). Gallery launched clean (25s).
- [ ] **File pick (manual — needs you):** run the gallery, click Browse, pick a file, confirm the path shows +
      the model updates. The OS file dialog can't be driven headlessly, so this one step is left for a human.

## Implementation plan

_Populated by `/tasks plan TASK-057` — leave empty until then._
