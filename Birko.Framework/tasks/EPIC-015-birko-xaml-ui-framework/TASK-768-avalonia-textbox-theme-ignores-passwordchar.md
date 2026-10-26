---
id: TASK-768
parent: EPIC-015
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] The template's `TextPresenter` takes `PasswordChar` and `RevealPassword` from the `TextBox`.
- [x] A regression test in `tests/Birko.Xaml.Avalonia.Tests` renders a `TextBox` with `PasswordChar` under the Birko
      theme and asserts the presenter masks it; the test fails on the current template.
- [x] A `Form` `FieldType.Password` field is covered the same way.
- [x] Check the other properties Avalonia's own template passes to the presenter (caret / selection brushes,
      `LineHeight`, `LetterSpacing`) and list any further gaps here rather than widening this fix silently.
- [x] **Found 2026-10-26 by TASK-774:** the template has no watermark presenter either, so `Watermark` never shows on any
      Birko `TextBox` (TagInput's and the Form's placeholders included). Decide here whether it is fixed with
      `PasswordChar` or filed separately; it is the same template.

## Out of scope

- A "show password" toggle button inside the field.

## Human test plan

- [x] 1. Run the Birko.Xaml Gallery, open a form with a password field, type into it under each theme (Light, Dark, Neon,
   Finstat): only `●` characters show.
- [x] 2. In the desktop tester, open Settings and check that "Private Key" is masked.
- [x] 3. Gallery: a placeholder shows in an empty text box (the ribbon's "Sheet name", the tag input's "IČO") and disappears
   once you type.
- [x] 4. Select text in any text box: the selection is highlighted, in each theme.
- [x] 5. Type a value longer than the box (or paste a long key into "Private Key"): the text scrolls so the caret stays
   visible; a multiline box with many lines scrolls vertically.

## Implementation plan

## Review log

- 2026-10-26 — Built; **verify** (Human test plan steps need a person at the Gallery and in the tester). Regression tests
  first: `TextBoxThemeTests` — **all 6 failed on the old template**, including one the report did not name: selection
  and caret position never reached the presenter either, so a Birko text box never drew a selection.
- Fix in `Controls/Inputs.axaml`: the presenter now gets `CaretIndex`, `SelectionStart` / `SelectionEnd`,
  `PasswordChar`, `RevealPassword`, caret / selection brushes, `CaretBlinkInterval`, `LineHeight`, `LetterSpacing`, as
  Avalonia's own template passes them; a `PART_Watermark` shows the placeholder while the text is empty. Because this
  theme replaces Fluent's, its selection / caret brush setters were gone too — set from tokens now.
- **Further gap, fixed openly rather than only listed** (the criterion said list, not widen silently): the template
  had no `ScrollViewer`, so a value longer than the box ran out of it and the caret left the box — measured with an
  80-character value in a 120 px box — and the reported field holds exactly such a value. Added `PART_ScrollViewer`
  (horizontal hidden bar, vertical scrolling for multiline); 2 more tests, both failing before.
- Remaining differences from Avalonia's template, deliberately left: `InnerLeftContent` / `InnerRightContent` and the
  data-validation error presenter are not in the Birko template. Nothing in Birko uses them today.
- Side effect, seen on the screenshots: placeholders appear everywhere they were declared and never showed — the
  ribbon's "Sheet name" field, TagInput, Form fields with `Placeholder`. TagInput's chip row is now centred so its
  placeholder sits in the middle of the field.
- Tests: `TextBoxThemeTests` 11 (+ 2 screenshots); Avalonia suite 395/395, Core 88/88. Screenshots looked at in Light
  and Dark (masked password, placeholder, highlighted selection), plus the form modal and ribbon fields for regressions.
- 2026-10-26 — Human test plan run by František Bereň in the Gallery: steps 1, 3, 4, 5 passed. Step 2 (the desktop
  tester's Settings "Private Key", new build) not run yet, so the task stays at **verify** until it is.
- 2026-10-26 — Step 2 passed: František Bereň confirmed "Private Key" masked in the desktop tester's Settings, on a build
  whose Birko.Xaml.Avalonia.dll (11:37:22) postdates this fix (43b00437, 11:36:36). Closed (verify → done).
