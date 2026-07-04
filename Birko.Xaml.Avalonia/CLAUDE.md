# Birko.Xaml.Avalonia — CLAUDE.md

The Avalonia skin (EPIC-015). See `README.md` for usage. This file records conventions and the
non-obvious theme-system decisions.

## Convention deviations (deliberate)

- Real Avalonia `net8.0` `.csproj` (Avalonia 11.2.3), not `.shproj`/`.projitems` — the EPIC-015
  break (compiled AXAML through `.projitems` is fragile). Referenced via `ProjectReference`; not in
  the `Birko.Framework.csproj` aggregator. Registered in `Birko.Framework.slnx` (`/Xaml/`) +
  `.code-workspace`.
- **TFM `net8.0`**, not the framework's `net10.0` — Avalonia 11.2.3 targets net6.0/net8.0. Core is
  also net8.0. A net10.0 consumer can still reference these.

## Theme system — how and why

- **`Themes/Tokens.axaml` is GENERATED** by `Birko.DesignTokens` — never hand-edit. To change a
  token, edit `Birko.DesignTokens/tokens.json` and run its `generate` command.
- **ThemeDictionaries, not swapped MergedDictionaries.** A single `ResourceDictionary` with a
  `ThemeDictionaries` entry per theme. Setting `RequestedThemeVariant` re-resolves every
  `DynamicResource` live (proven in tests). This is what STORY-030's "wired to ThemeVariant" means.
- **Custom variants work.** Spike-verified that Avalonia resolves arbitrary `ThemeVariant` keys
  (not just Light/Dark) via `TryGetResource(key, variant)`, falling back to the variant's
  `InheritVariant`. `BirkoThemeVariants.Neon` inherits Dark, `.Finstat` inherits Light.
- **Key identity matters.** The `ThemeDictionaries` keys are `{x:Static themes:BirkoThemeVariants.X}`
  — the *same* static instances used for `RequestedThemeVariant`. Do NOT switch to bare string
  keys for the custom variants; identity is what makes the lookup match.
- **Brushes live at the root**, one per color token, `Color="{DynamicResource BColorX}"`. Colors
  live per-variant in `ThemeDictionaries`. So one brush instance tracks the active variant — no
  per-theme brush duplication. Controls bind to `{DynamicResource BColorXBrush}`.

## Controls (`Controls/`)

Token-driven `ControlTheme`s, split by category and merged by `Controls/Controls.axaml` (which
`BirkoTheme.axaml` includes). Every visual value is `{DynamicResource B*}` — no hard-coded
colors/sizes — so controls re-theme live. Add new controls to the matching category file and, if
it's a new category, to the `Controls.axaml` merge list.

- **Buttons.axaml** — `Button`
- **Inputs.axaml** — `TextBox` (single + multiline via `AcceptsReturn`), `ComboBox` + `ComboBoxItem`
- **Toggles.axaml** — `CheckBox`, `RadioButton`
- **Surfaces.axaml** — `BCard` (ContentControl theme), `TabControl` + `TabItem`
- **Indicators.axaml** — `BBadge`, `BTag` (ContentControl themes), `ProgressBar`
- **Overlays.axaml** — `ToolTip`
- **Blocks.axaml** + control classes — the STORY-033 building blocks: **`Form`** (schema-driven, `Controls/Form.cs` — code-built from `Fields`+`Model`, no XAML template), **`Drawer`** (slide-in overlay, `IsOpen`/`Placement`, backdrop-click closes), **`SplitPanel`** (master/detail over `GridSplitter`, responsive `:collapsed` below `CollapseWidth`).

Named `ContentControl` themes (`BCard`/`BBadge`/`BTag`) are applied via `Theme="{StaticResource BCard}"`.
`Form` binds each `FormField` to `Model.[Name]` (reflection binding, two-way); it pairs with `CrudViewModelBase.EditingItem` / `DetailPageViewModel.Model`.

## Scope / deferred

- **STORY-030** = tokens + swap. **STORY-034** = the Tier-1 restyle sweep (above).
- **Deferred (need more than a mechanical restyle, tracked in STORY-034 / candidates for STORY-035):**
  `ToggleSwitch` (enforces `PART_MovingKnobs`/`PART_SwitchKnob` + knob-animation logic), a `Spinner`
  (custom control + rotation animation), `Menu`/dropdown, `table`, `data-table` (Avalonia `DataGrid`
  is a separate package + row-height parity), `modal` overlay, `breadcrumb`.
- Composite/motion **tokens** (shadows, focus rings, transitions, easings, gradients) are still not
  mapped — add them when a control needs them.
- The CSS-only scoped `inverse` theme is not emitted to AXAML.

## Control-theme gotchas (learned)

- A `double` token can't bind to `CornerRadius` — radius tokens are emitted as `CornerRadius`
  resources by the generator (do not "fix" them back to `x:Double`).
- Some native controls enforce required template parts (`ToggleSwitch`, and heavier ones); the XAML
  compiler reports `AVLN2205`. Either supply the parts correctly or defer — don't ship a half-templated control.
- `TextPresenter.Foreground` is a plain CLR setter (not bindable) — let `Foreground` inherit from the parent `TextBox` instead of binding it.

## Testing

`Birko.Xaml.Avalonia.Tests` (Avalonia.Headless.XUnit, net8.0): loads the real `Tokens.axaml`,
asserts per-variant resolution, a live `RequestedThemeVariant` swap re-resolving a `DynamicResource`
brush, and the `AvaloniaThemeManager`. Headless needs an `[assembly: AvaloniaTestApplication]` +
`[AvaloniaFact]`/`[AvaloniaTheory]`.
