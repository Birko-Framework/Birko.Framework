# Birko.Xaml.Shell — CLAUDE.md

App chrome + page views (EPIC-015 / STORY-036). See `README.md` for usage. Platform-neutral shell +
navigation VMs live in `Birko.Xaml.Core` (constraint #3); only views/chrome are here.

## Structure

- `ViewLocator.cs` — VM→View: naming convention (`*ViewModel`→`*View`) then generic base-page
  mapping (Split before List, since `SplitPageViewModel<T>` derives from `ListPageViewModel<T>`).
- `Views/ShellView` — sidebar + header (title + theme switcher + user area) + content region + status
  bar, with a **Ctrl+K `CommandPalette`** overlaying the whole shell (bound to `ShellViewModel.PaletteCommands`
  / `IsPaletteOpen`, populated from modules + themes). The user area (avatar + name) shows when
  `ShellViewModel.UserName` is non-empty, with a Flyout of `UserCommands`.
- `Views/{List,Detail,Split}PageView` — generic page views over the Core base VMs.

## Conventions / gotchas

- Real Avalonia `net8.0` `.csproj` (Avalonia 11.2.3). References `Birko.Xaml.Core` + `Birko.Xaml.Avalonia`.
  Registered in `.slnx` (`/Xaml/`) + `.code-workspace`; not in the aggregator.
- **Page views use `x:CompileBindings="False"`** — their DataContext is a *generic* base VM
  (`ListPageViewModel<T>` etc.) with no fixed `x:DataType`, so bindings are reflection-based. `ShellView`
  could compile-bind to `ShellViewModel` (concrete) but stays uniform with the page views.
- Views bind the VM's `Fields` schema → the `Form` control (`Birko.Xaml.Avalonia`); create/edit is
  inline (`EditingItem` + `SaveEditing`/`CancelEdit`), gated by the VM permission flags.
- The demo list shows entity `ToString()` — a real app sets a `ListBox` `ItemTemplate` / display member.

## Scope / deferred (STORY-036 in-progress)

Delivered: the **sidebar shell** (`BSidebarAppShell` analogue) + nav + ViewLocator + page views, the
**Ctrl+K command palette** (populated from modules + themes), and the **header user area** (avatar +
name + `UserCommands` dropdown, hidden when no user). **Deferred:** ribbon chrome (`BAppShell`), a
tenant switcher, a `FormModal` page-shape (inline edit covers create/edit today),
`TransitioningContentControl` animations, ListBox restyle.

## Testing

Navigation (pure), ViewLocator resolution, and a headless `ShellView` render + screenshot live in
`Birko.Xaml.Avalonia.Tests` (it references Shell + reuses that project's Fluent+tokens TestApp).
