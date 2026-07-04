# Birko.Xaml.Core

Platform-neutral core for the Birko XAML UI framework (EPIC-015). Deliberately **thin** and
**Avalonia-free** so both the Avalonia skin (today) and a future WPF skin reference one shared
Core rather than source-including it twice.

## What's here (STORY-030 seed)

- **`Theming.ThemeInfo`** — `record (Id, Label, IsDark, Icon?)`, mirroring the Birko.Web shell's `ThemeOption`.
- **`Theming.BirkoThemes`** — the four built-in themes (`light`/`dark`/`neon`/`finstat`) as metadata, matching the token sheets from `Birko.DesignTokens` and the web `BUILTIN_THEMES`.
- **`Theming.IThemeManager`** — platform-neutral switcher (`Available` / `Current` / `ThemeChanged` / `SetTheme`), the XAML analogue of the web shell's `setTheme`. The Avalonia skin implements it over `RequestedThemeVariant`; a WPF skin would implement it over swapped `MergedDictionaries`.

## Growing later (STORY-032)

i18n (`I18n` singleton + `{l:Tr}` markup extension) and the base ViewModels
(`CrudViewModelBase<T>`, `ListPageViewModel<T>`, `DetailPageViewModel<T>` on CommunityToolkit.Mvvm)
land here. Everything else (HTTP, data, sync, config) is delegated to existing `Birko.*`
projects, not re-implemented.

## Convention note

Real `net8.0` class-library assembly (not `.shproj`/`.projitems`) — the EPIC-015 deviation. **Must
stay Avalonia-free** (WPF-addendum constraint #1): no `using Avalonia.*`. Anything Avalonia-specific
lives in `Birko.Xaml.Avalonia`.
