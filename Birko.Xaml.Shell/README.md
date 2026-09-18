# Birko.Xaml.Shell

The Birko.Web app shell + page shapes on Avalonia (EPIC-015 / STORY-036): a sidebar app chrome,
navigation, and generic list/detail/split page views that bind the `Birko.Xaml.Core` base
ViewModels. Build a desktop CRUD app with the same shape as its Birko.Web counterpart.

## Pieces

- **`ViewLocator`** (`IDataTemplate`) — resolves a page ViewModel to its View: `*ViewModel → *View`
  naming convention for custom pages, else the generic `Split`/`List`/`DetailPageView` for the
  `Birko.Xaml.Core` base VMs. Add it to `Application.DataTemplates` (or a content region).
- **`Views.ShellView`** — the app chrome: sidebar module nav + header (module title + theme
  switcher) + content region (bound to `INavigationService.Current` through the ViewLocator) +
  status bar. Binds to a `ShellViewModel`.
- **Generic page views** — `ListPageView` (gated New/Edit/Delete toolbar + search + list + inline
  create/edit `Form`), `DetailPageView` (`Form` + Save/Cancel), `SplitPageView` (master list +
  detail `Form` over a `SplitPanel`).

Platform-neutral navigation + shell VMs live in **`Birko.Xaml.Core`** (`Navigation/`, `Mvvm/ShellViewModel`,
`Mvvm/SplitPageViewModel`) so a future WPF skin reuses them (WPF-addendum constraint #3).

## Wiring an app

```csharp
var data = new StoreAdapter<Contact>(myBirkoStore);        // adapt Birko.Data → ICrudDataSource
var fields = new[] { new FormField { Name = "Name", Required = true }, /* … */ };

var nav = new NavigationService().Register(
    new ModuleDefinition { Id = "contacts", Label = "Contacts",
        CreateViewModel = () => { var vm = new SplitPageViewModel<Contact>(data) { Fields = fields }; vm.LoadAsync(); return vm; } });

var shell = new ShellViewModel(nav, new AvaloniaThemeManager()) { Title = "My App" };
nav.Navigate("contacts");
// window.Content = new ShellView { DataContext = shell };
```

## Scope (STORY-036 MVP) / deferred

Delivered: navigation, **sidebar** shell chrome (the `BSidebarAppShell` analogue), ViewLocator,
and the list/detail/split page views. **Deferred:** the **ribbon** chrome (`BAppShell`), command
palette, user-area / tenant switcher, a `FormModal` dialog (inline edit covers create/edit for now),
and content transition animations.

## Convention note

Real Avalonia `net8.0` assembly (EPIC-015 break). Registered in `Birko.Framework.slnx` (`/Xaml/`) +
`.code-workspace`; referenced via `ProjectReference`, not the aggregator.
