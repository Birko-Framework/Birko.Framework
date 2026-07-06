# Birko.Xaml.Core.Tests

xUnit + FluentAssertions tests for [`Birko.Xaml.Core`](../Birko.Xaml.Core).

## Coverage

- **`CoreIsAvaloniaFreeTests`** — asserts the `Birko.Xaml.Core` assembly references no `Avalonia.*`,
  keeping the core platform-neutral so a future WPF skin can reuse it unchanged.
- **`I18nTests`** — active-locale indexer, missing-key echo, fallback-locale lookup, `{placeholder}`
  interpolation, and the `PropertyChanged`/`LocaleChanged` events (and same-locale no-op) from `SetLocale`.
- **`FormatterTests`** — locale-independent `Duration`, culture-driven `Number`/`Currency`/`Percent`,
  `DateStyle` variants, live reflow on locale change, and invariant fallback for an unknown culture.
- **`ViewModelTests`** — `CrudViewModelBase<T>` (load, create/save, delete, permission-gated commands,
  locale re-emit), `DetailPageViewModel<T>` (load + save gating) and `ListPageViewModel<T>` (client-side filter)
  over the `ICrudDataSource<T>` port.
- **`MobileShellViewModelTests`** — `ShellViewModel` bottom-nav `MobileNavItem` projection and active-surface
  tracking across `Navigate`, `Back`, and `NavigateCommand`.
- **`MirrorDataSourceTests`** — `MirrorDataSource<T>` online pass-through + mirror population, offline fallback,
  remote-404 eviction, and online `GetAsync` upsert, asserting `SyncStatus`.

## Test framework

- xUnit
- FluentAssertions

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
