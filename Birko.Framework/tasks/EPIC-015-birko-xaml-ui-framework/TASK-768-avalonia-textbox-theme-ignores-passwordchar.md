---
id: TASK-768
parent: EPIC-015
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The Birko Avalonia TextBox theme ignores `PasswordChar`, so a password field shows its value

## Context

Reported 2026-10-26 by the ClientApi.CSharp session: the FinStat desktop tester's Settings modal has a "Private Key"
field declared `FieldType.Password`, and under the Birko Finstat theme the key is shown in plain text.

`Form` sets `PasswordChar = '●'` for that field type (`Birko.Xaml.Avalonia/Controls/Form.cs:196`). The Birko `TextBox`
`ControlTheme` replaces Avalonia's template, and its `TextPresenter` (`Birko.Xaml.Avalonia/Controls/Inputs.axaml:22`)
binds `Text`, `Margin`, `TextWrapping`, `TextAlignment` and `VerticalAlignment` — but not `PasswordChar` or
`RevealPassword`. The presenter is what masks, so the property is accepted on the `TextBox` and dropped. Every
`TextBox` with a `PasswordChar` under any Birko theme is affected, not only `Form`.

Related: FEATURE-020 (filed from the same request) — not a decision of it.

## Acceptance criteria

- [ ] The template's `TextPresenter` takes `PasswordChar` and `RevealPassword` from the `TextBox`.
- [ ] A regression test in `tests/Birko.Xaml.Avalonia.Tests` renders a `TextBox` with `PasswordChar` under the Birko
      theme and asserts the presenter masks it; the test fails on the current template.
- [ ] A `Form` `FieldType.Password` field is covered the same way.
- [ ] Check the other properties Avalonia's own template passes to the presenter (caret / selection brushes,
      `LineHeight`, `LetterSpacing`) and list any further gaps here rather than widening this fix silently.
- [ ] **Found 2026-10-26 by TASK-774:** the template has no watermark presenter either, so `Watermark` never shows on any
      Birko `TextBox` (TagInput's and the Form's placeholders included). Decide here whether it is fixed with
      `PasswordChar` or filed separately; it is the same template.

## Out of scope

- A "show password" toggle button inside the field.

## Human test plan

1. Run the Birko.Xaml Gallery, open a form with a password field, type into it under each theme (Light, Dark, Neon,
   Finstat): only `●` characters show.
2. In the desktop tester, open Settings and check that "Private Key" is masked.

## Implementation plan

## Review log
