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

### Forms schema (STORY-033)
- **`Forms.FormField` / `FieldType`** — platform-neutral field schema (`Name`/`Label`/`Type`/`Required`/`Options`/`Placeholder`/`ReadOnly`). The Avalonia `Form` control renders it into labeled, two-way-bound inputs; a WPF `Form` would consume the same schema.

### Navigation
- **`Navigation.BreadcrumbItem`** — a crumb model (`Label` + optional `Href` / `Run`), the XAML analogue of Birko.Web's `b-breadcrumb` `{ label, href? }` items. The Avalonia `Breadcrumb` renders non-last items with a `Run`/`Href` as clickable links (last = current location). Also: `Navigation.ModuleDefinition` / `INavigationService` / `MobileNavItem` (the module/route map + shell nav).

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

### Comparing JSON (FEATURE-020 / TASK-770)
- **`Diff.JsonDiff.Diff(a, b, options?)`** — structural diff of two `System.Text.Json.Nodes.JsonNode` values, a port of
  Birko.Web.Core's `diffJson`. The result carries the comparison three ways: a flat `Changes` list keyed by path, the
  merged `Tree` of both sides (each node `Equal` / `Added` / `Removed` / `Changed` / `Modified` / `Ignored`), and
  `Summary` counts. Side A is the "old" value, side B the "new" one.

```csharp
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Birko.Xaml.Core.Diff;

var hosts = new Regex(@"https?://[a-z0-9.-]*finstat\.sk");
var r = JsonDiff.Diff(JsonNode.Parse(baselineBody), JsonNode.Parse(otherBody), new JsonDiffOptions
{
    Ignore = ["**.RequestId", "Items[*].Ref"],
    Normalize = [new JsonNormalizer("**", (v, _) =>
        v is JsonValue s && s.TryGetValue<string>(out var str) ? JsonValue.Create(hosts.Replace(str, "{sk}")) : v)],
    Arrays = [new JsonArrayRule("Items") { Key = "Id" }, new JsonArrayRule("Tags") { Match = JsonArrayMatch.Unordered }],
});

r.Equal;                       // false
r.Summary;                     // JsonDiffSummary { Added = 1, Removed = 0, Changed = 2 }, .Total = 3
foreach (var c in r.Changes)   // c.Kind, c.Path ("Items[1].Price"), c.OldPath (side A's, when paired elsewhere),
    Console.WriteLine(c);      // c.OldValue / c.NewValue as received, c.HasOldValue / c.HasNewValue
```

- **Paths** read like property access: `Items[2].Ref`; a key that is not an identifier is quoted (`["a.b"]`); the root
  is `""`. **Patterns** (in `Ignore`, `Normalize`, `Arrays`) add `*` (any one segment), `[*]` (any one index) and `**`
  (any number of segments, including none), and match the **whole** path. A malformed pattern throws
  `FormatException`. `Diff.JsonPath.Format` / `Matches` are public for callers that build their own rules.
- `Normalize` maps a value on both sides before comparing (chained in order); changes still report the values as
  received. Return a new node; never modify the one you are given.
- `Arrays`: item n with item n by default; `Match = Unordered` pairs equal items; `Key = "Id"` (or `KeySelector`) pairs
  records by key, falling back to unordered for items without one. A pair is reported at its side-B index, with
  `OldPath` / `OldSegments` giving its side-A position; a removed item is listed under its side-A index.
- **Parity:** the web engine is the engine of record. Both run the shared vectors in
  `Birko.Web.Core/src/diff/diff-vectors.json` (`JsonDiffVectorTests` here, `diff-vectors-smoke` in the Playground;
  `diff-parity.yml` in CI). Two JavaScript behaviours are reproduced because they are observable: object keys are
  visited in JavaScript's own-key order (integer-like keys first, ascending), and numbers compare as doubles (`1` equals
  `1.0`).

Everything else (HTTP, data, sync, config) is delegated to existing `Birko.*` projects, not re-implemented.

## Convention note

Real `net8.0` class-library assembly (not `.shproj`/`.projitems`) — the EPIC-015 deviation. **Must
stay Avalonia-free** (WPF-addendum constraint #1): no `using Avalonia.*`. Anything Avalonia-specific
lives in `Birko.Xaml.Avalonia`.
