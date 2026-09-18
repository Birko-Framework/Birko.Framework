# Birko.Xaml.Core.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Xaml.Core` — the Avalonia-free XAML platform core
(i18n, base MVVM ViewModels, the CRUD data-source port, locale-aware `Formatter`, navigation/shell
view-models, and `MirrorDataSource`).

## Project Location

`C:\Source\Birko.Xaml.Core.Tests\`

## Scope

- `CoreIsAvaloniaFreeTests` — enforces the Avalonia-free constraint: asserts the `Birko.Xaml.Core`
  assembly references no `Avalonia.*` assembly, so a future WPF (or other XAML) skin can reuse it unchanged.
- `I18nTests` — the `I18n`/`II18n` service: active-locale indexer resolution, missing-key echo,
  fallback-locale lookup, `{placeholder}` interpolation, and the `PropertyChanged` (`Item[]`, `Locale`) /
  `LocaleChanged` events raised by `SetLocale` (including the same-locale no-op).
- `FormatterTests` — the locale-aware `Formatter`: locale-independent `Duration`, culture-driven
  `Number`/`Currency`/`Percent`, `DateStyle` variants, live reflow when the bound locale changes, and
  invariant fallback for an unknown culture tag.
- `ViewModelTests` — base MVVM view-models over the `ICrudDataSource<T>` port: `CrudViewModelBase<T>`
  (load, create/save, delete, permission-gated commands, locale re-emit), `DetailPageViewModel<T>`
  (load + save gating) and `ListPageViewModel<T>` (client-side filtering).
- `MobileShellViewModelTests` — `ShellViewModel` mobile nav model: `MobileNavItem` list projected from
  modules in order and its live active-surface tracking across `Navigate`, `Back`, and `NavigateCommand`.
- `MirrorDataSourceTests` — `MirrorDataSource<T>` online/offline sync: pass-through with mirror population,
  offline fallback to the mirror, remote-404 eviction, and online `GetAsync` upsert, with `SyncStatus` assertions.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj, `net8.0`, `ImplicitUsings` + `Nullable` enabled. References the
  `Birko.Xaml.Core.csproj` assembly (a real project reference, not `.projitems`). Pulls in only
  CommunityToolkit.Mvvm transitively through the core — no Avalonia.
- One test class per source type; test both success and failure paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
