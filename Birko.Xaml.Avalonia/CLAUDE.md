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

- **Everything under `Themes/` is GENERATED** by `Birko.DesignTokens` — never hand-edit. To change a
  token, edit `Birko.DesignTokens/tokens.json` and run its `generate` command (`verify` fails on
  drift for the AXAML as well as the CSS).
- **One file per theme, so consumers ship only what they offer.** `Themes/Tokens.{Light,Dark,Neon,Finstat}.axaml`
  each hold a single `ThemeDictionaries` entry; `Themes/Tokens.Brushes.axaml` holds the shared
  brushes; `Themes/Tokens.axaml` is the back-compat aggregate merging all five. Two includes:
  - `BirkoTheme.axaml` — all four themes (unchanged; what existing consumers use).
  - `BirkoTheme.Core.axaml` — light + dark only; add `Themes/Tokens.<Theme>.axaml` per extra theme.

  Core+extras beats all-in by ~43% of token payload (23 KB vs 41 KB). Mirrors the web side, where
  each alternate theme is its own opt-in CSS file.
- **Light + Dark are BOTH mandatory in any composition.** `ThemeVariant.Dark` has no
  `InheritVariant`, so a light-only app resolves *nothing* under OS dark mode. Neon/Finstat are
  safely omissible because they inherit Dark/Light. Pinned by `ThemeCompositionTests`.
- **ThemeDictionaries, not swapped MergedDictionaries.** Setting `RequestedThemeVariant` re-resolves
  every `DynamicResource` live (proven in tests). This is what STORY-030's "wired to ThemeVariant"
  means. Splitting across files is safe because Avalonia resolves `ThemeDictionaries` entries found
  in *merged* dictionaries — also pinned by `ThemeCompositionTests`, since the whole split rests on it.
- **`BThemeId` is how the loaded themes are detected.** Each generated dictionary declares
  `<x:String x:Key="BThemeId">` naming itself, and `AvaloniaThemeManager.DetectThemes` reads it, so
  `Available` is *derived* from what was merged instead of a second hand-maintained list that can
  drift. Presence alone cannot work: an omitted variant inherits silently, so any key would still
  resolve — the value has to say which dictionary answered. Pass `available:` explicitly to override.
- **Custom variants work.** Spike-verified that Avalonia resolves arbitrary `ThemeVariant` keys
  (not just Light/Dark) via `TryGetResource(key, variant)`, falling back to the variant's
  `InheritVariant`. `BirkoThemeVariants.Neon` inherits Dark, `.Finstat` inherits Light.
- **Key identity matters.** The `ThemeDictionaries` keys are `{x:Static themes:BirkoThemeVariants.X}`
  — the *same* static instances used for `RequestedThemeVariant`. Do NOT switch to bare string
  keys for the custom variants; identity is what makes the lookup match.
- **Brushes live in their own sheet** (`Themes/Tokens.Brushes.axaml`), one per color token,
  `Color="{DynamicResource BColorX}"`. Colors live per-variant in `ThemeDictionaries`. So one brush
  instance tracks the active variant — no per-theme brush duplication. Controls bind to
  `{DynamicResource BColorXBrush}`. Being theme-independent, this sheet is required alongside *any*
  theme subset — omit it and every `B*Brush` key breaks.

## Controls (`Controls/`)

Token-driven `ControlTheme`s, split by category and merged by `Controls/Controls.axaml` (which
`BirkoTheme.axaml` includes). Every visual value is `{DynamicResource B*}` — no hard-coded
colors/sizes — so controls re-theme live. Add new controls to the matching category file and, if
it's a new category, to the `Controls.axaml` merge list.

- **Buttons.axaml** — `Button`
- **Inputs.axaml** — `TextBox` (single + multiline via `AcceptsReturn`), `ComboBox` + `ComboBoxItem`, `Slider` (TASK-054: horizontal + vertical, `Track`+`Thumb`+RepeatButton template, cross-axis set per-orientation via `:horizontal`/`:vertical`; sub-themes `BSliderRepeat`/`BSliderThumb` — the equalizer-capable slider), `CalendarDatePicker` + `TimePicker` (TASK-056: light restyle — `BasedOn` Fluent + Birko token setters on the resting surface; flyout internals stay Fluent. Form composes `DateTime` (date+time) and `DateRange` (two pickers) field types)
- **Toggles.axaml** — `CheckBox`, `RadioButton`, `ToggleSwitch` (declares the required `PART_MovingKnobs`/`PART_SwitchKnob`)
- **Surfaces.axaml** — `BCard` (ContentControl theme), `TabControl` + `TabItem`
- **Indicators.axaml** — `BBadge`, `BTag` (ContentControl themes), `ProgressBar`, `BusySpinner` (custom, rotating `Arc`)
- **Overlays.axaml** — `ToolTip`, `MenuFlyoutPresenter` + `MenuItem` (dropdown menus)
- **Breadcrumb** (`Controls/Breadcrumb.cs`) — ContentControl building crumbs + separators in code. `ItemsSource` takes plain values (static text) **or** `Birko.Xaml.Core.Navigation.BreadcrumbItem`s; a non-last item with a `Run` action or `Href` renders as a clickable text link (the `BBreadcrumbLink` ControlTheme in `Nav.axaml` — chrome-free Button, secondary→primary+underline on hover, resolved in code via `Application.Current.TryGetResource`), invoking `Run` and raising `ItemInvoked` on click. The last crumb is the current location and is never clickable (web `b-breadcrumb` parity).
- **Tree.axaml** (`b-tree-menu`, STORY-035) — `TreeView` + `TreeViewItem` restyle: chevron (`PART_ExpandCollapseChevron`), token hover/selected, indented children, `:empty` hides the chevron on leaves
- **`CommandPalette`** (`b-command-palette`, STORY-035/036) — `Controls/CommandPalette.cs` + template in Blocks.axaml: overlay with a search box filtering `Commands` (`Birko.Xaml.Core.Commands.CommandItem`), keyboard nav (Up/Down/Enter/Esc), invokes `Run` + closes. Toggle via `IsOpen` (wire Ctrl+K in a shell)
- **`ObjectTree`** (`b-object-tree` + `b-json-viewer`, STORY-035) — `Controls/ObjectTree.cs`: `Source` (object graph) or `Json` (string) → recursive tree over the restyled `TreeView`, type-colored monospaced values (string=success, number=primary, bool=warning, null=muted); walks `JsonNode`/dictionaries/enumerables/POCO props; invalid JSON falls back to a raw-string leaf. **Selection is queryable**: every node is `Tag`ged with its `(path, value)`, surfaced as read-only `SelectedValue` + `SelectedPath` (`user.roles[0]`) with a `SelectionChanged` event, so a host can copy/serialize/drill into the selected node. `SelectedValue` is null both for "nothing selected" and for a selected null node — use `SelectedPath` to tell them apart. A rebuild (new `Source`/`Json`) clears the selection
- **`XmlViewer`** (`b-xml-viewer`, STORY-035) — `Controls/XmlViewer.cs`: `Xml` (string) → `XDocument` → tree over the restyled `TreeView` (elements = `<tag>` primary, `@attributes`/`#text` leaves with success-colored values); invalid XML → raw-string leaf
- **`Kanban`** (`b-kanban`, STORY-035) — `Controls/Kanban.cs` over `Birko.Xaml.Core.Kanban.{KanbanColumn,KanbanCard}`: horizontal token-styled columns (header + live count), each an `ItemsControl` bound to `column.Cards` (`ObservableCollection`, so model moves update the board live), card surfaces via `FuncDataTemplate`. Best-effort pointer drag-drop between columns (`DragDrop.DoDragDrop`/`DropEvent`); recursive card nesting out of scope for this first cut
- **`FormModal`** (`b-form-modal` / the epic's `FormModal<T>`, STORY-036) — `Controls/FormModal.cs`: composes `Modal` + `Form` + Save/Cancel; bind `IsOpen`/`Title`/`Fields`/`Model` + `SaveCommand`/`CancelCommand` (Save runs the command then closes). A create/edit dialog page-shape.
- **`RibbonChunkButton`** (`Controls/RibbonChunkButton.cs`, TASK-100) — the button a collapsed group folds
  into. A `Button` plus an automation peer implementing `IExpandCollapseProvider`, so a screen reader
  announces "collapsed, expandable" rather than "button", with the state raised on flyout open/close.
  Needed because Birko has no KeyTips: a collapsed group is the only route to its commands, so an
  unannounced one removes them from screen-reader users specifically. The web equivalent is
  `aria-expanded` + `aria-haspopup`; XAML has no attribute to set, hence a peer.
- **`Ribbon`** (`b-ribbon` / `BAppShell` chrome, STORY-036) — `Controls/Ribbon.cs` over `Birko.Xaml.Core.Ribbon.{RibbonTab,RibbonGroup,RibbonItem}`: a tab strip whose active tab shows labeled groups of icon+label command buttons; `Tabs` + `SelectedIndex`, item `Run` on click. **Collapsible** (`IsCollapsed` — tabs-only mode): a chevron at the strip's end toggles it, and clicking the already-active tab toggles too (Office-style). Token-styled, rebuilt on change. **Overflow (TASK-097):** both the tab strip and the groups row sit in `WrapScrollable` — a `ScrollBarVisibility.Hidden` `ScrollViewer` flanked by chevron buttons that appear only while there is more content that way (mirroring `b-ribbon`'s web affordance). The bar is `Hidden`, not `Auto`, because a visible horizontal bar would add its height to a 34px tab strip, so the ribbon's height would change with the window width; the chevrons sit in `Auto` columns and are collapsed while invisible, costing no layout when everything fits. Chevron visibility syncs off `ScrollChanged`/`LayoutUpdated`, and since `Extent`/`Viewport` change on resize that covers a narrowing window with no rebuild. The collapse chevron stays *outside* the tab scroller so it can never scroll out of reach. **Progressive scaling (STORY-049/TASK-099+100):** the groups row is a `RibbonGroupsPanel`, **not** a scroller — the ribbon body resizes, it never scrolls, because a scroll offset destroys the spatial memory the ribbon exists to provide. The panel builds every group at every variant, measures them, and applies what `Birko.Xaml.Core.Ribbon.RibbonScaling` picks: `Large` (32px icon above label) → `Medium` (16px icon, label beside, 3 per column) → `Small` (icon only, name in the tooltip) → `Popup` (the whole group as one chunk button whose flyout holds it at `Large`), and below even that a **compact** chunk that drops the group name too. `PreferredGroupSize` (default `Medium`) is the look at full width; `Large`/`Small` are opt-in. `ResolvedGroupSizes` exposes the outcome. Measured floors on a six-group tab: 509px labelled chunks, 166px compact. Two things that are load-bearing and easy to break: unchosen variants are **parked off-screen, not hidden** (see the gotchas — hidden controls measure zero), and the group **gap tightens with the variant** while the *decision* uses the full gap (conservative: over-degrading merely leaves room, under-degrading clips). The tab strip still scrolls — the documented exception, as in Office Web.
- **`BChart`** (`b-chart`, STORY-035) — `Controls/BChart.cs` over **LiveCharts2** (`LiveChartsCore.SkiaSharpView.Avalonia`, the one external UI dep beyond Avalonia — modern/animated, MVVM-first, targets Avalonia + WPF). Bind `Series` (Core `ChartSeries`), `Kind` (Line/Column), `Labels`; series colored from the token palette (`BColorPrimary`/`Info`/`Success`/`Warning`/`Danger` → `SKColor`, resolved on attach). LiveCharts animates on load, so a single headless frame captures it mid-animation.
- **`MarkdownEditor`** + **`MarkdownRenderer`** (`b-markdown-editor`, STORY-035) — `Controls/MarkdownEditor.cs` (split editor `TextBox` two-way `Markdown` + live preview) and a dependency-free `MarkdownRenderer` (static): a common Markdown subset (ATX headings, `**bold**`/`*italic*`/`` `code` ``/`[text](url)`, unordered lists, fenced code, `---`) → token-styled Avalonia controls. Swap in Markdig later for full CommonMark
- **DataGridStyles.axaml** (`data-table`) — token restyle of Avalonia `DataGrid`. **Styles, not resources** (DataGrid ships its theme as Styles): add `<StyleInclude Source="avares://Birko.Xaml.Avalonia/Controls/DataGridStyles.axaml" />` to `Application.Styles` after `FluentTheme`. It includes DataGrid's Fluent theme then layers Birko tokens (header band via `--b-table-header-*`, cell text/font, grid lines). Needs the `Avalonia.Controls.DataGrid` package.
- **Blocks.axaml** + control classes — the building blocks: **`Form`** (schema-driven, `Controls/Form.cs` — code-built from `Fields`+`Model`, no XAML template), **`Drawer`** (slide-in overlay, `IsOpen`/`Placement`, backdrop-click closes), **`SplitPanel`** (master/detail over `GridSplitter`, responsive `:collapsed` below `CollapseWidth`), **`Modal`** (centered dialog over a backdrop, `IsOpen`/`Title`, backdrop-click closes — compose a "FormModal" by putting a `Form` + Save/Cancel in its content).

Named `ContentControl` themes (`BCard`/`BBadge`/`BTag`) are applied via `Theme="{StaticResource BCard}"`.
`Form` binds each `FormField` to `Model.[Name]` (reflection binding, two-way); it pairs with `CrudViewModelBase.EditingItem` / `DetailPageViewModel.Model`. **Field types (TASK-055):** Text/TextArea/Number/Percent/Password/Email/Search → `TextBox` (Password sets `PasswordChar`; Number/Percent clamp to `Min`/`Max` on commit), Checkbox→`CheckBox`, Switch→`ToggleSwitch`, Select→`ComboBox`, Radio/OptionGroup→`RadioButton` group (equality-converter binding, vertical/horizontal), Markdown→`MarkdownEditor`, Range→`Slider`, Date/Time/DateTime/DateRange→`CalendarDatePicker`/`TimePicker` (+ composites), MultiSelect→multi-`ListBox` (TASK-057), Tags→`WrapPanel` chip input (TASK-057), File→path box + Browse via `StorageProvider` (TASK-057). `FormField.Default` seeds a null model prop at bind time; `Hint` renders muted under the field.

## Scope / deferred

- **STORY-030** = tokens + swap. **STORY-034** = the Tier-1 restyle sweep (above).
- **Tier-1 is complete** — nothing deferred. (DataGrid is Styles-based and needs the separate
  `StyleInclude`; see the DataGrid entry above.) Spinner animations are scoped inside `Arc.Styles`
  (a top-level ControlTheme animation `Style` silently breaks theme application — see the gotcha below).
- Composite/motion **tokens** (shadows, focus rings, transitions, easings, gradients) are still not
  mapped — add them when a control needs them.
- The CSS-only scoped `inverse` theme is not emitted to AXAML.

## Control-theme gotchas (learned)

- A `double` token can't bind to `CornerRadius` — radius tokens are emitted as `CornerRadius`
  resources by the generator (do not "fix" them back to `x:Double`).
- Some native controls enforce required template parts (`ToggleSwitch`, and heavier ones); the XAML
  compiler reports `AVLN2205`. Either supply the parts correctly or defer — don't ship a half-templated control.
- `TextPresenter.Foreground` is a plain CLR setter (not bindable) — let `Foreground` inherit from the parent `TextBox` instead of binding it.
- **Animations belong inside the templated element's own `.Styles`, not as a top-level ControlTheme animation `Style`** — a `<Style Selector="^ /template/ X"><Style.Animations>` at the ControlTheme level silently broke the whole theme's application (control got no template). `BusySpinner` puts its rotation in `Arc.Styles` with a `RotateTransform` + `RotateTransform.Angle` animation.
- Custom control names can collide with Avalonia's (`Spinner` → `Avalonia.Controls.Spinner`); the loading indicator is `BusySpinner`.
- **Off-screen is not out of the tab order.** Parking a control off-screen (or clearing
  `IsHitTestVisible`) leaves it **focusable**, so `Tab` walks through invisible controls. Set
  `IsEnabled = false` to remove the whole subtree from keyboard navigation; it does not affect measurement.
  The ribbon's parked size variants had three extra tab stops per group before this.
- **A `Flyout` does not dismiss on `Escape` by itself**, and `Popup.IsLightDismissEnabled` delivers neither
  `Escape` nor click-away. All three cost a shipped bug here. If an overlay must be dismissable, own the
  dismissal — a `TopLevel` `KeyDown`/`PointerPressed` handler — rather than assuming the primitive does it.
- **Window-level shortcuts need a `TopLevel` handler, not `OnKeyDown`.** A `ContentControl` is not
  focusable, so an `OnKeyDown` override on one never runs. `Ctrl+F1` on the ribbon was unreachable this way
  while its test passed, because the test raised the event straight at the control.
- **`Measure()` on an `IsVisible = false` control yields `DesiredSize` zero.** `MeasureCore` short-circuits.
  So a panel that hides its alternatives and then measures them gets zeros, not widths — which is how
  `RibbonGroupsPanel` under-degraded and clipped its last group. Park alternatives **off-screen**
  (`Arrange` at a large negative X, panel `ClipToBounds`, `IsHitTestVisible = false`) instead of hiding
  them. Flipping `IsVisible` per pass is also wrong: it invalidates measure, so the panel re-layouts forever.
- **A `ScrollViewer` measures its content with infinite width** — that is what scrolling means. Any panel
  that wants to adapt to the available width cannot sit inside one and read `availableSize`. Feeding it the
  scroller's *viewport* instead works but lags a pass, and if the scroller's own chrome (chevrons) has
  hysteresis you get a feedback loop where the same window width resolves differently depending on drag
  direction. The ribbon's groups row ended up with no scroller at all for exactly this reason.

## Accessibility gotchas (learned the hard way, TASK-100)

Four defects in one review pass, all in controls whose *behaviour* was already correct and tested. Every one
of them made the ribbon unusable — not merely imperfect — for keyboard or screen-reader users.

- **A button whose content is a panel has NO accessible name.** Avalonia derives one from `Content` only when
  it is a string; anything else falls through to `Content.ToString()`, so assistive tech is handed
  `"Avalonia.Controls.StackPanel"` — or, for an icon-only button, the bare glyph. Every ribbon command was
  anonymous this way. **Set `AutomationProperties.SetName` explicitly on every focusable control**, and route
  it through one helper (`Ribbon.Describe`) so a new button cannot quietly skip it. A `ToolTip` is not a
  substitute in general — name it as well as tipping it.
  - A test asking only that the name be **non-empty is vacuous** for exactly this reason: the `ToString()`
    fallback satisfies it. Assert the name is a string the *user* would recognise.
  - Mark decorative glyphs `AutomationProperties.AccessibilityView = Raw`, or an emoji gets read out beside
    the name it decorates.
- **There is no focus visual on `Button` anywhere in this skin** (only `Inputs.axaml` styles `:focus`), so
  keyboard focus is invisible in every consumer app — a WCAG 2.4.7 failure, and it faked two separate "Tab is
  broken" bug reports. Tracked as **TASK-103**; the ribbon has a scoped ring in the meantime. If you add a
  focus indicator, **keep its layout footprint constant** (change the brush, not the thickness) — a ring that
  resizes its control reflows the row, and for the ribbon that re-triggers scaling.
- **Rebuilding a tree destroys focus.** `Ribbon.Rebuild()` replaces its whole `Content`, so the focused
  control is disposed and focus falls back to the window root: activating a tab by keyboard threw the user
  out of the ribbon entirely. Note whether focus was inside before tearing down (`TopLevel.FocusManager`)
  and restore it afterwards, posted at `DispatcherPriority.Loaded` since the new tree is not yet laid out.
- **Don't say the state twice.** A `Button` carrying `IExpandCollapseProvider` *is* announced with its state
  by Narrator, so also wording "collapsed" into `GetLocalizedControlTypeCore()` produced "Export, collapsed
  group, collapsed". `LocalizedControlType` says what the control **is** ("group"); the pattern owns the
  **state**, and unlike the property it raises a change notification. Verify against a real screen reader
  before theorising about what one reads — the theory that led here was inferred from the wrong control.

`b-ribbon` had all of this right already (`aria-label` on every item, `aria-hidden` on icons, a focus ring on
tabs and items). When porting a web component, **port its ARIA discipline too** — the XAML equivalents are
`AutomationProperties.Name` / `AccessibilityView.Raw` / an automation peer, and none of them are automatic.

## Testing

`Birko.Xaml.Avalonia.Tests` (Avalonia.Headless.XUnit, net8.0): loads the real `Tokens.axaml`,
asserts per-variant resolution, a live `RequestedThemeVariant` swap re-resolving a `DynamicResource`
brush, and the `AvaloniaThemeManager`. Headless needs an `[assembly: AvaloniaTestApplication]` +
`[AvaloniaFact]`/`[AvaloniaTheory]`.
