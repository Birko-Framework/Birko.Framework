---
id: STORY-032
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
