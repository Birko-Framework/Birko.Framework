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
- **Base VMs (STORY-032):** `Mvvm.BasePageViewModel`, `CrudViewModelBase<T>`, `ListPageViewModel<T>`,
  `DetailPageViewModel<T>` on CommunityToolkit.Mvvm (the ONLY dependency Core takes — platform-neutral).
- **Data port (STORY-032):** `Data.ICrudDataSource<T>`.

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
