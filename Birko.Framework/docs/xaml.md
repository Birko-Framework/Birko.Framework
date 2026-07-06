# XAML / Desktop UI Guide (Birko.Xaml)

## Overview

Birko.Xaml is the **desktop counterpart to Birko.Web** — an Avalonia-first XAML UI framework
whose design system, theming, i18n, and MVVM shapes are the same as the web stack, driven from
the **same single source of design tokens**. A screen built for the web has a direct desktop
analogue, and the two can never drift on colors, spacing, or radii because both are generated
from one `tokens.json`.

| Project | TFM | Purpose |
|---|---|---|
| `Birko.DesignTokens` | net10.0 | Build-time token generator. `tokens.json` → **byte-identical** web CSS + Avalonia AXAML. CLI: `generate` / `verify` / `extract`. |
| `Birko.Xaml.Core` | net8.0 | **Avalonia-free** platform core — theming abstractions, i18n, base MVVM ViewModels, form/nav/command/kanban/ribbon/chart models, and the `ICrudDataSource<T>` port. Only dependency: CommunityToolkit.Mvvm. |
| `Birko.Xaml.Avalonia` | net8.0 | The Avalonia skin — theme system (4 runtime-swappable variants), ~20 restyled Tier-1 controls, building blocks (Form/Drawer/SplitPanel/Modal), and 7 Tier-2 composites. |
| `Birko.Xaml.Shell` | net8.0 | Application shell — sidebar **and** ribbon chrome, navigation, generic list/detail/split page views, command palette (Ctrl+K), user/tenant areas, page transitions. |

> These are the first real, buildable `.csproj` **assemblies** in the `Birko\Framework` bucket
> (every other sibling is a `.shproj`/`.projitems` shared project). They ship as assemblies
> because compiled AXAML through `.projitems` is fragile. Reference them via `ProjectReference`,
> **not** the `Birko.Framework.csproj` aggregator. The TFM is `net8.0` because that's what
> Avalonia 11.2.3 targets; a net10.0 consumer can still reference them.

The runnable **gallery** (`Birko.Xaml.Gallery`) lives in the `Birko\Consumers` bucket — a
demo/validation app, not a framework project — mirroring `Birko.Web.Playground`.

## Single-source design tokens (`Birko.DesignTokens`)

`tokens.json` is the one source of truth for every design token (colors, spacing, radii, fonts,
etc.). A small C# tool generates every target so the web (CSS) and desktop (Avalonia XAML) design
systems can **never** drift.

```bash
# Everyday: regenerate CSS + AXAML from tokens.json
dotnet run --project Birko.DesignTokens -- generate

# CI / pre-commit: fail (exit 1) if on-disk CSS differs from tokens.json
dotnet run --project Birko.DesignTokens -- verify

# Bootstrap / re-derive tokens.json from the current CSS
dotnet run --project Birko.DesignTokens -- extract
```

- **Byte-identical CSS parity is the acceptance gate.** Regenerating the 5 web CSS files
  (`tokens.css` + `themes/{dark,neon,finstat,inverse}.css`) is byte-for-byte identical to the
  committed files — values are single-sourced on `tokens.json`, while comments and layout
  round-trip verbatim. Line endings are canonically LF and comparisons normalize CRLF→LF, so
  generation is machine-independent (no dependency on a checkout's `autocrlf` state).
- **AXAML output** — `Birko.Xaml.Avalonia/Themes/Tokens.axaml`: one `ResourceDictionary` whose
  `ThemeDictionaries` hold one entry per theme. `--b-color-x` → a `Color` plus a paired
  `SolidColorBrush` (`BColorX` / `BColorXBrush`); rem/px → `x:Double` (rem baked at 16px);
  `--b-radius*` → `CornerRadius` resources (a `double` can't bind to a `CornerRadius`);
  `--b-font*` → `FontFamily`.
- `tokens.json` is deliberately **language-neutral** — swapping the C# generator for a
  TypeScript one later is a one-file emitter rewrite against the same golden CSS.

## Theme system (`Birko.Xaml.Avalonia`)

Four built-in themes, matching Birko.Web: **light**, **dark**, **neon**, **finstat**. Switch at
runtime with no restart.

```csharp
using Birko.Xaml.Avalonia.Theming;

IThemeManager themes = new AvaloniaThemeManager();
themes.SetTheme("dark");                 // live re-theme — every DynamicResource re-resolves
foreach (var t in themes.Available) { /* t.Id, t.Label, t.Icon */ }
```

How it works (the non-obvious parts):

- **ThemeDictionaries, not swapped MergedDictionaries.** A single `Tokens.axaml` holds a
  `ThemeDictionaries` entry per theme; setting `RequestedThemeVariant` re-resolves every
  `{DynamicResource}` live.
- **Custom variants work.** Avalonia resolves arbitrary `ThemeVariant` keys (not just
  Light/Dark) — `BirkoThemeVariants.Neon` inherits Dark, `.Finstat` inherits Light.
- **Key identity is the trick.** `ThemeDictionaries` keys are
  `{x:Static themes:BirkoThemeVariants.X}` — the *same* static instances assigned to
  `RequestedThemeVariant`, so lookup matches by identity, not fragile string keys.
- **Brushes at root, colors per variant** — one `SolidColorBrush` per token
  (`Color="{DynamicResource BColorX}"`) so a swap updates the brush without duplicating it four
  times. Controls bind `{DynamicResource BColorXBrush}` / `{DynamicResource BRadius}`.

Wire it once in `App.axaml` (order matters — Fluent base, then DataGrid styles, then Birko):

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Birko.Xaml.Avalonia/Controls/DataGridStyles.axaml" />
</Application.Styles>
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Birko.Xaml.Avalonia/BirkoTheme.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

## Controls

Token-driven `ControlTheme`s (every visual value is `{DynamicResource B*}` — no hard-coded
colors/sizes), split by category under `Controls/` and merged by `Controls.axaml` (which
`BirkoTheme.axaml` includes).

- **Tier-1 (~20 restyled native controls)** — `Button`, `TextBox` (single + multiline), `ComboBox`,
  `CheckBox`, `RadioButton`, `ToggleSwitch`, `Card`, `TabControl`, `Badge`, `Tag`, `ProgressBar`,
  `ToolTip`, dropdown `Menu`, `BusySpinner`, `Breadcrumb` (crumbs are clickable when fed
  `BreadcrumbItem`s with a `Run`/`Href` — the last is the current location; parity with web
  `b-breadcrumb`), `TreeView`, `ListBoxItem`, and the
  token-restyled `DataGrid` (its theme ships as **Styles**, so it's a separate `StyleInclude`).
- **Building blocks** — `Form` (schema-driven from `FormField[]` + a model, two-way reflection
  binding), `Drawer` (slide-in overlay), `SplitPanel` (master/detail over a `GridSplitter`,
  responsive collapse), `Modal` (centered dialog over a backdrop), and `FormModal`
  (Modal + Form + Save/Cancel — the create/edit dialog shape).
- **Tier-2 composites (7)** — `tree-menu`, `CommandPalette`, `ObjectTree` (`b-object-tree` +
  `b-json-viewer`), `XmlViewer`, `Kanban`, `MarkdownEditor` (+ dependency-free `MarkdownRenderer`),
  and `BChart` over **LiveCharts2** (`LiveChartsCore.SkiaSharpView.Avalonia` — the only external
  UI dependency beyond Avalonia; series are colored from the token palette).

### Schema-driven Form

```csharp
form.Fields = new[]
{
    new FormField { Name = "FullName", Label = "Full name", Required = true },
    new FormField { Name = "Email",    Label = "Email", Placeholder = "you@example.com" },
    new FormField { Name = "Subscribed", Label = "Subscribed", Type = FieldType.Checkbox },
    new FormField { Name = "Plan", Label = "Plan", Type = FieldType.Select,
                    Options = new object[] { "Free", "Pro", "Enterprise" } },
};
form.Model = new Contact();
```

The `FormField` schema lives in `Birko.Xaml.Core` (Avalonia-free, reusable by a future WPF `Form`);
the control is the Avalonia view. It pairs directly with `CrudViewModelBase.EditingItem` /
`DetailPageViewModel.Model`.

## `Birko.Xaml.Core` — Avalonia-free platform core

The thin core has **no Avalonia dependency** (enforced by a test: the assembly references no
`Avalonia.*`), so its logic is reusable by a future WPF skin.

- **i18n** — `II18n` / `I18n` (+ `I18n.Instance` singleton, mirrors Birko.Web's `i18n`): locale
  dictionaries, `this[key]` indexer (fallback → key), `SetLocale`, `Translate(key, args)` with
  `{placeholder}` interpolation. The `{l:Tr}` markup extension lives in `Birko.Xaml.Avalonia`
  (it must return an Avalonia `Binding`).
- **Base ViewModels** (CommunityToolkit.Mvvm) — `BasePageViewModel` (busy/loaded/title +
  re-localization), `CrudViewModelBase<T>` (observable items, selection, editing slot,
  **permission-gated** Create/Edit/Delete/Save/Refresh), `ListPageViewModel<T>` (client-side
  search), `DetailPageViewModel<T>` (load-by-id), `SplitPageViewModel<T>`, and `ShellViewModel`
  (navigation + theme + palette + user/tenant + ribbon).
- **`ICrudDataSource<T>` port — deliberately NOT `IAsyncBulkStore<T>`.** The `Birko.Data.*` are
  shared `.projitems` (compiled into each importing assembly) while `Birko.Xaml.*` are real
  assemblies — importing the store projitems into Core would duplicate those types against a
  consumer's aggregator. So Core owns a thin CRUD port and the consumer supplies a ~10-line
  store→port adapter in the assembly that already has the Birko.Data types.

## `Birko.Xaml.Shell` — the app shell

A working desktop CRUD app shape: two interchangeable chromes over the same `ShellViewModel`.

- **`ShellView`** — the sidebar chrome (`BSidebarAppShell` analogue): module nav + header (title,
  tenant switcher, theme switcher, user area) + content region + status bar.
- **`RibbonShellView`** — the ribbon chrome (`BAppShell` analogue): a `Ribbon` (collapsible,
  Office-style) over the content region + status bar.
- **`ViewLocator`** — `*ViewModel → *View` naming convention, falling back to the generic
  base-page mapping (Split before List, since Split derives from List).
- Generic **`ListPageView`** (permission-gated New/Edit/Delete toolbar + search + inline create/edit
  `Form`), **`DetailPageView`** (`Form` + Save/Cancel), **`SplitPageView`** (master list + detail).
- **Command palette (Ctrl+K)** over the nav modules + themes; **cross-fade page transitions**
  (both shells' content regions use a `TransitioningContentControl`).

> Generic page views bind a base VM with no fixed `x:DataType`, so they use
> `x:CompileBindings="False"` (reflection-based bindings) — documented in the project CLAUDE.md.

## Testing

Headless Avalonia (`Avalonia.Headless.XUnit` + Skia): the real `Tokens.axaml` loads, per-variant
resolution is asserted, a live `RequestedThemeVariant` swap re-resolves a `DynamicResource` brush,
controls render, and Skia **screenshots** are captured per theme for visual parity (env
`BIRKO_SHOTS`). Test projects: `Birko.DesignTokens.Tests` (token round-trip / CSS parity / AXAML),
`Birko.Xaml.Core.Tests` (i18n, CRUD VMs, permission gating, Avalonia-free enforcement),
`Birko.Xaml.Avalonia.Tests` (theme system + all controls + screenshots).

## Status

**EPIC-015 is complete (8/8 stories).** A WPF skin is deferred — it would share the same tokens
and Core ViewModels and fork only the control templates. See
[tasks/EPIC-015](../tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md).

## See also

- [Web Components Guide](web.md) — the web stack these tokens/VMs mirror.
