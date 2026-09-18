# Birko.Xaml.Core — CLAUDE.md

Platform-neutral core for the Birko XAML UI framework (EPIC-015). See `README.md` for the surface.

## Hard rule — stay Avalonia-free

WPF-addendum constraint #1: **no `using Avalonia.*` anywhere in this assembly.** One Avalonia type
in a base VM and the future WPF skin can't reuse Core. Anything platform-specific (theme swap
mechanism, markup extensions, control templates) lives in `Birko.Xaml.Avalonia` (or a future
`Birko.Xaml.Wpf`), implementing the neutral interfaces defined here.

## Convention deviations

Real `net8.0` class-library `.csproj` (not `.shproj`/`.projitems`) — the EPIC-015 break, so the
skins reference one shared assembly. Not in the `Birko.Framework.csproj` aggregator (that's for
`.projitems` shared projects). Registered in `Birko.Framework.slnx` (`/Xaml/`) + `.code-workspace`.

## Current contents

- **Theming (STORY-030):** `ThemeInfo`, `BirkoThemes` (the 4 built-ins — keep in sync with the
  `Birko.DesignTokens` sheets + web `BUILTIN_THEMES`), `IThemeManager`.
- **i18n (STORY-032):** `Localization.II18n` / `I18n` (+ `I18n.Instance` singleton). The `{l:Tr}`
  markup extension is NOT here — it must return a platform `Binding`, so it lives in
  `Birko.Xaml.Avalonia`. Core holds only the Avalonia-free logic.
- **Formatting (EPIC-016 / TASK-044):** `Localization.IFormatter` / `Formatter` — the XAML analogue
  of Birko.Web's `createFormatter`, bound to an `II18n` and resolving `CultureInfo` from the active
  locale at call time. `Duration(seconds)` is locale-independent (exact web parity); `Date` / `Time` /
  `DateTime` / `Number` / `Currency` / `Percent` map onto .NET `CultureInfo`. `Currency`'s symbol is
  driven by the currency *code* (not the culture), matching the web's Intl behaviour.
- **Base VMs (STORY-032):** `Mvvm.BasePageViewModel`, `CrudViewModelBase<T>`, `ListPageViewModel<T>`,
  `DetailPageViewModel<T>` on CommunityToolkit.Mvvm (the ONLY dependency Core takes — platform-neutral).
- **Data port (STORY-032):** `Data.ICrudDataSource<T>`.
- **Navigation:** `Navigation.{ModuleDefinition, INavigationService, NavigationService, MobileNavItem}` + `Navigation.BreadcrumbItem` (crumb model — `Label`/`Href`/`Run`; the Avalonia `Breadcrumb` makes non-last items with a target clickable, web `b-breadcrumb` parity).
- **Ribbon (STORY-036; scaling model STORY-049/TASK-098):** `Ribbon.{RibbonTab, RibbonGroup, RibbonItem}`
  + `Ribbon.RibbonGroupSize` (`Large`/`Medium`/`Small`/`Popup`, declared roomiest-first so a measure pass
  can compare with `<`/`>`). `RibbonGroup` carries `Icon` (drawn only on the collapsed `Popup` chunk
  button), `ScalingPriority` and `MinSize` for Office-style progressive scaling. **`ScalingPriority` is
  importance — a LOWER value degrades FIRST**, which is Birko's convention and deliberately *not* an
  assertion about RibbonX's numeric sense; say so wherever it's re-documented. Defaults
  (`ScalingPriority = 0`, `MinSize = Popup`) reproduce pre-TASK-098 rendering exactly. The fields are
  consumed by the degrade pass below. Keep in step with the `RibbonGroupSize` / `RibbonGroup` mirror in
  web `b-ribbon.ts` — the two are designed together, never retrofitted one side at a time.
- **Ribbon scaling policy (STORY-049/TASK-099):** `Ribbon.RibbonScaling.Resolve(groups, available,
  preferred, gap)` + `Ribbon.RibbonGroupMetrics`. Given each group's width per variant, its priority and
  its floor, it picks a variant per group so the row fits, degrading the **least important first, one step
  at a time**. Renderer-free on purpose: the *rendering* is forked (AXAML vs CSS) but the *policy* must
  not be, so both skins call the same algorithm — `Birko.Web.Components/src/nav/ribbon-scaling.ts` mirrors
  it and the playground's `ribbon-scaling-smoke` asserts the **same numeric table** as
  `RibbonScalingTests` so the two cannot drift. Three properties are load-bearing:
  - **Deterministic** — a pure function of the arguments, never of the currently-applied layout. Feeding
    the applied layout back in is what makes a scaling ribbon oscillate at a boundary; a test walks widths
    down and back up and compares.
  - **`MinSize` is a preference, not a guarantee** — breached least-important-first rather than letting the
    row overflow, because unreachable commands are worse than a group being less legible than its author
    wanted. (Office has no hard floor either.) Still honoured whenever any arrangement fits.
  - **An unmeasured variant costs the nearest roomier one**, so a renderer that measured only some variants
    over-estimates instead of letting the row "fit" by accident and clip.
- **Forms (STORY-033; field types EPIC-016/TASK-055):** `Forms.FormField` + `FieldType` (21 types:
  Text/TextArea/Number/Percent/Range/Password/Email/Search/Checkbox/Switch/Select/MultiSelect/Radio/OptionGroup/Tags/File/Markdown/Date/Time/DateTime/DateRange;
  `Forms.DateRange` is the value type for the DateRange field).
  `FormField` carries `Min`/`Max`/`Step`/`Default`/`Hint`/`Options`/`Required`/`ReadOnly` — all neutral;
  the Avalonia `Form` control maps each `FieldType` to a restyled control.
- **Offline mirror (EPIC-016 / TASK-046):** `Data.MirrorDataSource<T>` — network-first read-through over
  the port (refresh mirror on read, fall back offline, evict on 404), with an observable `SyncStatus`.
- **Device (EPIC-016 / TASK-045, 048):** `Device.IWakeLock`, `Device.IAudioCue` (+ `AudioCueOptions`) —
  neutral device-capability contracts; Avalonia implementations live in `Birko.Xaml.Avalonia`.

## The `ICrudDataSource<T>` port — why not `IAsyncBulkStore<T>` directly

The `Birko.Data.*` stores are shared **`.projitems`** (compiled into each importing assembly).
`Birko.Xaml.*` are **real assemblies**. If Core imported `Birko.Data.Stores.projitems`, those types
would be baked into `Birko.Xaml.Core.dll` AND into a consumer's own aggregator (which also imports
them) → duplicate-type conflicts (the exact hazard root CLAUDE.md warns about). So Core owns a small
CRUD port and the consumer supplies a ~10-line adapter from their store in the assembly that already
has the Birko.Data types. Do **not** "fix" this by importing the store projitems into Core.

## i18n live re-localization gotcha

Avalonia does **not** observe `INotifyPropertyChanged` on **indexer** accessors, so binding straight
to `I18n[key]` won't refresh on `SetLocale`. The `{l:Tr}` extension therefore binds a small
per-binding source's real `Value` property (refreshed on `LocaleChanged`). Keep `I18n` raising
`"Item[]"` for WPF/direct-indexer consumers, but don't rely on it in Avalonia.

## Testing

Theme-system behaviour is proven in `Birko.Xaml.Avalonia.Tests` (headless), since it needs the
Avalonia skin to exercise `IThemeManager`. Pure-Core logic added later (i18n, VM commands) should
get its own `Birko.Xaml.Core.Tests`.
