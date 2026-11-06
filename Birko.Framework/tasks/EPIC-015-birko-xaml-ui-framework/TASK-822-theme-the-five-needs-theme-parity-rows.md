---
id: TASK-822
parent: EPIC-015
feature: FEATURE-021
status: done
priority: P3
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-793, TASK-795]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Theme the five `needs-theme` rows of PARITY.md

## Context

Asked by the user (2026-11-06) after a review of the parity gaps: 29 rows were not ported (24 `missing`, 5
`needs-theme`), none had a task, and porting each is its own decision (FEATURE-021 D12). The user chose the cheap half
first: the five where Avalonia already has the control and only the Birko look was missing.

| Web | Desktop |
|---|---|
| `b-qty-stepper` | `NumericUpDown` |
| `b-carousel` | `Carousel` |
| `b-color-picker` | `ColorPicker` (separate package, not referenced) |
| `b-pre` | `SelectableTextBlock` |
| `b-code-block` | `SelectableTextBlock` (no syntax highlighting) |

## Acceptance criteria

- [x] Each control takes its values from Birko tokens and re-themes on a variant switch; tested against the token, not
      a literal.
- [x] The five rows move to `themed`, and `ParityCounterpartTests` accepts each counterpart.
- [x] Rendered and looked at.
- [x] Project CLAUDE.md and README say how to use them (the ColorPicker include, the keyed text themes).

## Human test plan

In the Birko.Xaml.Gallery (or any app with the Birko theme): a `NumericUpDown`, a `Carousel`, a `ColorPicker` (with
`ColorPickerStyles.axaml` included) and a `SelectableTextBlock` with `Theme="{StaticResource BCodeBlock}"` look like
the web components in Light, then switch to Dark and Finstat.

## Outcome — 2026-11-06

- **`NumericUpDown`**, **`Carousel`**: type `ControlTheme`s `BasedOn` Fluent's (as the date pickers), token surface,
  border, radius, font, height; the stepper centres its text (it sat at the top in the first render). The carousel
  has no arrows or dots of its own, noted in its row.
- **`BPre`**, **`BCodeBlock`**: KEYED `SelectableTextBlock` themes (monospace token, tinted box; the code block is based
  on the pre). Keyed on purpose: a type theme would have made every `SelectableTextBlock` in the library monospace,
  the markdown and JSON viewers included; a test pins that a plain one keeps its font.
- **`ColorPicker`**: `Avalonia.Controls.ColorPicker` added to the csproj (`11.*`, as the other Avalonia packages).
  Merging its dictionaries into `BirkoTheme.axaml` was tried first and refused by the compiler (AVLN2000: they are not
  public), so it follows DataGrid: new `Controls/ColorPickerStyles.axaml`, a Styles file an app adds with a
  `StyleInclude`, which brings the package's Fluent template and the Birko setters. Without it a ColorPicker has no
  template at all, which the README now says.
- `ParityCounterpartTests`: the check knew one Styles file (DataGrid) and two Avalonia assemblies; it now reads both
  Styles files and the ColorPicker assembly. Its `NumericUpDown` fixtures (the example of an unstyled control) moved to
  `SplitButton`.
- Tests: `Task822ThemeTests` (5); `Birko.Xaml.Avalonia.Tests` 442/442. The headless render showed the pre and code
  boxes in a proportional font: headless Skia has only its default font, so the mono token's families fall back there;
  the property is asserted to be the `BFontMono` token.
- PARITY.md now has 0 `needs-theme` rows; 24 `missing` remain, each a separate decision.
