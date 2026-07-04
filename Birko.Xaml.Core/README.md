# Birko.Xaml.Core

Platform-neutral core for the Birko XAML UI framework (EPIC-015). Deliberately **thin** and
**Avalonia-free** so both the Avalonia skin (today) and a future WPF skin reference one shared
Core rather than source-including it twice.

## What's here

### Theming (STORY-030)
- **`Theming.ThemeInfo`** — `record (Id, Label, IsDark, Icon?)`, mirroring the Birko.Web shell's `ThemeOption`.
- **`Theming.BirkoThemes`** — the four built-in themes (`light`/`dark`/`neon`/`finstat`) as metadata, matching the token sheets from `Birko.DesignTokens` and the web `BUILTIN_THEMES`.
- **`Theming.IThemeManager`** — platform-neutral switcher (`Available` / `Current` / `ThemeChanged` / `SetTheme`).

### i18n (STORY-032)
- **`Localization.II18n` / `I18n`** — locale dictionaries, `this[key]` indexer (with fallback → key), `SetLocale` (raises `INotifyPropertyChanged` + `LocaleChanged`), `Translate(key, args)` with `{placeholder}` interpolation. `I18n.Instance` is the process-wide singleton (mirrors Birko.Web's `i18n`). The `{l:Tr}` markup extension lives in `Birko.Xaml.Avalonia` (it must return a platform binding) and resolves through this.

### Base ViewModels (STORY-032, on CommunityToolkit.Mvvm)
- **`Mvvm.BasePageViewModel`** — `IsBusy`/`IsLoaded`/`Title`, `L(key)` localization, `LoadAsync`, and live re-localization (re-raises on `LocaleChanged`).
- **`Mvvm.CrudViewModelBase<T>`** — `ObservableCollection<T>`, `SelectedItem`, `EditingItem`, and **permission-gated** `Refresh`/`Create`/`Edit`/`Delete`/`SaveEditing`/`CancelEdit` commands (`CanCreate`/`CanEdit`/`CanDelete` drive `CanExecute`).
- **`Mvvm.ListPageViewModel<T>`** — adds client-side `SearchText` + `Filtered`.
- **`Mvvm.DetailPageViewModel<T>`** — load-by-id (or new), `Model`, permission-gated `Save`/`Cancel`.

### Data port (STORY-032)
- **`Data.ICrudDataSource<T>`** — the thin CRUD port the VMs depend on (Guid-keyed, store-shaped: `GetAll`/`Get`/`Save`/`Delete`/`NewInstance`). **Not** `IAsyncBulkStore<T>` directly — see the CLAUDE.md note on the assembly-vs-projitems boundary. A consumer adapts their Birko.Data store to this port in their own assembly:

```csharp
sealed class StoreAdapter<T>(IAsyncBulkStore<T> store) : ICrudDataSource<T> where T : AbstractModel {
    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) => (await store.ReadAsync(ct)).ToList();
    public Task<T?> GetAsync(Guid id, CancellationToken ct = default) => store.ReadAsync(id, ct);
    public Task<Guid> SaveAsync(T item, CancellationToken ct = default) => store.SaveAsync(item, ct: ct);
    public Task DeleteAsync(T item, CancellationToken ct = default) => store.DeleteAsync(item, ct);
    public T NewInstance() => store.CreateInstance();
}
```

Everything else (HTTP, data, sync, config) is delegated to existing `Birko.*` projects, not re-implemented.

## Convention note

Real `net8.0` class-library assembly (not `.shproj`/`.projitems`) — the EPIC-015 deviation. **Must
stay Avalonia-free** (WPF-addendum constraint #1): no `using Avalonia.*`. Anything Avalonia-specific
lives in `Birko.Xaml.Avalonia`.
