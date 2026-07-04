---
id: STORY-032
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-04
---

## Resolution (2026-07-04)

`Birko.Xaml.Core` grew from theme abstractions into the thin platform-neutral core: **i18n** +
**base ViewModels** + a **CRUD port**, all Avalonia-free (enforced by a test). **15 Core tests + a
headless `{l:Tr}` test** green.

- **i18n** — `II18n`/`I18n` (+ `I18n.Instance`): locale dicts, indexer with fallback, `SetLocale`
  raising `INotifyPropertyChanged` + `LocaleChanged`, `Translate` with `{placeholder}` interp.
- **`{l:Tr}` markup extension** — lives in `Birko.Xaml.Avalonia` (must return an Avalonia `Binding`),
  resolving through the Core singleton and re-resolving live on `SetLocale`.
- **Base VMs on CommunityToolkit.Mvvm** — `BasePageViewModel` (busy/loaded/title + live
  re-localization), `CrudViewModelBase<T>` (observable items, selection, **permission-gated** commands),
  `ListPageViewModel<T>` (client-side search), `DetailPageViewModel<T>` (load-by-id, gated Save).

**Two decisions worth recording:**
- **CRUD port, not `IAsyncBulkStore<T>` directly.** Birko.Data.* are shared `.projitems` (compiled
  into each importer); Birko.Xaml.* are real assemblies. Importing the store projitems into Core
  would duplicate those types against a consumer's aggregator. So Core defines `ICrudDataSource<T>`
  and the consumer supplies a ~10-line adapter in the assembly that owns the Birko.Data types.
- **Avalonia doesn't observe INPC on indexers**, so `{l:Tr}` binds a per-binding source's real
  `Value` property (refreshed on `LocaleChanged`) rather than `I18n[key]` directly.

Open question (i18n on `Birko.Localization` vs standalone) → **standalone** for now (Core stays
dependency-light: CommunityToolkit.Mvvm only). Can bridge to `Birko.Localization` later if needed.

# Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free)

## User story

As an app developer, I want a thin platform-neutral core (i18n + base CRUD/list/detail
ViewModels), so localized, MVVM-shaped pages work the same on any XAML platform.

## Behaviour

- `Birko.Xaml.Core` is a plain `net8.0` assembly with **no `using Avalonia.*`** (WPF addendum constraint #1 — enforced, ideally by a build/analyzer check).
- **i18n** — an `I18n` singleton (mirrors Birko.Web.Core's) + a `{l:Tr Key=…}` markup extension that re-resolves on locale change (binding to the singleton's `INotifyPropertyChanged` indexer). May sit on `Birko.Localization` rather than reinvent — open question.
- **Base ViewModels** — `BasePageViewModel`, `CrudViewModelBase<T>` (filters, `ObservableCollection<T>`, Create/Edit/Delete commands, permission flags), `ListPageViewModel<T>`, `DetailPageViewModel<T>` — built on CommunityToolkit.Mvvm (works on Avalonia + WPF).
- Token-consumption *abstractions* live here; Avalonia-specific wiring stays in `Birko.Xaml.Avalonia`.
- **Behaviour proof:** a VM resolves a localized string, raises change notifications, and re-emits on `setLocale`; CRUD commands gate on permission flags.
- Deliberately thin — everything else (HTTP/data/sync/config) is delegated to existing Birko.Framework projects, not re-implemented (see EPIC Web.Core reuse table).
