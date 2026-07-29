---
id: TASK-094
parent: STORY-048
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-07-29
depends-on: [TASK-092]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Clear the 28 Avalonia 12 obsolete warnings (`Watermark`, `Bitmap.Save`)

## Context

The Avalonia 12 bump (TASK-092) compiles with **28 warnings and 0 errors** — enumerated exactly from
the 2026-07-29 scratchpad build. None block anything, so they were deliberately split out to keep
TASK-092's diff reviewable. They matter because `CLAUDE.md` § Code Style holds new code to a
no-nullable-warnings bar, and a standing 28-warning baseline is how a real warning gets lost.

### 10 × `TextBox.Watermark` → `PlaceholderText`

Renamed in 12; old property kept `[Obsolete]`. Also `UseFloatingWatermark` → `UseFloatingPlaceholder`
(not currently used — check before assuming).

C# (`warning CS0618`):
- `Birko.Xaml.Avalonia/Controls/Form.cs:164, 194, 219, 226, 273, 330`
- `Birko.Xaml.Avalonia/Dialogs/DialogService.cs:126`

AXAML (`Avalonia warning AVLN5001`):
- `Birko.Xaml.Avalonia/Controls/Blocks.axaml:47`
- `Birko.Xaml.Shell/Views/ListPageView.axaml:13`
- `Birko.Xaml.Shell/Views/SplitPageView.axaml:10`

Pure rename — but check the restyled `TextBox` ControlTheme in `Inputs.axaml` for a
`Watermark`-targeting selector or `TemplateBinding`, which the grep for the property name alone
would miss.

### 18 × `Bitmap.Save(string, int?)` → the `BitmapEncoderOptions` overload

All in the screenshot-dumping tests, one call each in: `ChartTests:88`, `CommandPaletteTests:93`,
`DialogServiceTests:218`, `FormFieldTypesTests:283, 313, 342`, `FormModalTests:99`, `KanbanTests:104`,
`MarkdownEditorTests:76`, `MobileShellTests:94`, `ModalTests:71`, `ObjectTreeTests:78`,
`RibbonTests:141, 149`, `ShellTests:159, 287`, `TreeTests:84`, `XmlViewerTests:70`.

If **TASK-095** lands first, these call sites are already being reworked into the comparison helper —
fix them there instead of twice, and this task shrinks to the `Watermark` rename. Check TASK-095's
state before starting.

## Acceptance criteria

- [ ] All `Watermark` usages moved to `PlaceholderText` (7 C# + 3 AXAML); the restyled `TextBox` ControlTheme checked for selectors/bindings referencing the old name.
- [ ] All 18 `Bitmap.Save` calls on the `BitmapEncoderOptions` overload — or routed through TASK-095's helper if that landed first.
- [ ] `Birko.Xaml.Avalonia`, `Birko.Xaml.Shell` and `Birko.Xaml.Avalonia.Tests` build with **0 warnings**.
- [ ] Suites still green: Avalonia 144, Core 41, DesignTokens 42.
- [ ] Placeholders still visibly render in forms, dialogs, list/split page search boxes and the command palette.

## Out of scope

- The bump itself (TASK-092) — this task assumes Avalonia 12 is already in.
- Any behavioural change to placeholder or screenshot semantics; rename + overload swap only.

## Human test plan

- [ ] Open a form, the prompt dialog, a list page and a split page — every placeholder that showed text on 11.x still shows it (a missed ControlTheme selector fails silently, showing an empty box rather than throwing).

## Implementation plan

_Populated by `/tasks plan TASK-094` — leave empty until then._
