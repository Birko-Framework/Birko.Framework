# Birko.Xaml.Shell — CLAUDE.md

App chrome + page views (EPIC-015 / STORY-036). See `README.md` for usage. Platform-neutral shell +
navigation VMs live in `Birko.Xaml.Core` (constraint #3); only views/chrome are here.

## Structure

- `ViewLocator.cs` — VM→View: naming convention (`*ViewModel`→`*View`) then generic base-page
  mapping (Split before List, since `SplitPageViewModel<T>` derives from `ListPageViewModel<T>`).
- `Views/ShellView` — sidebar + header (title + theme switcher + user area) + content region + status
  bar, with a **Ctrl+K `CommandPalette`** overlaying the whole shell (bound to `ShellViewModel.PaletteCommands`
  / `IsPaletteOpen`, populated from modules + themes). The user area (avatar + name) shows when
  `ShellViewModel.UserName` is non-empty, with a Flyout of `UserCommands`. A **tenant switcher**
  (`ComboBox` bound to `Tenants`/`CurrentTenant`) shows when `HasMultipleTenants` (> 1 tenant).
- `Views/RibbonShellView` — the **ribbon** chrome variant (`BAppShell`): a `Ribbon` (bound to
  `ShellViewModel.RibbonTabs`) over the content region + status bar, in place of the sidebar. Same
  `ShellViewModel` + Ctrl+K palette.
- `Views/MobileShellView` — the **mobile** chrome variant (BMobileAppShell equivalent, EPIC-016 /
  TASK-043): fixed top-bar (active-surface title + theme switcher) + scrolling content + fixed
  bottom-nav. The bottom-nav binds `ShellViewModel.NavItems` (`MobileNavItem` — a projection of the
  same `ModuleDefinition` list, adding an observable `IsActive`); the active item highlights primary
  via `Classes.active`. Code-behind reads the platform `IInsetsManager` and pads the top-bar / bottom-nav
  for the notch + home indicator (null on desktop → no-op). Same `ShellViewModel`; no Ctrl+K palette.
- `Views/{List,Detail,Split}PageView` — generic page views over the Core base VMs.
- Both shells' content regions use a `TransitioningContentControl` (`CrossFade`) so page navigation
  fades. `ListBoxItem` is token-restyled (`Controls/Lists.axaml`) — hover/selected use tokens.

## Conventions / gotchas

- Real Avalonia `net8.0` `.csproj` (Avalonia 11.2.3). References `Birko.Xaml.Core` + `Birko.Xaml.Avalonia`.
  Registered in `.slnx` (`/Xaml/`) + `.code-workspace`; not in the aggregator.
- **Page views use `x:CompileBindings="False"`** — their DataContext is a *generic* base VM
  (`ListPageViewModel<T>` etc.) with no fixed `x:DataType`, so bindings are reflection-based. `ShellView`
  could compile-bind to `ShellViewModel` (concrete) but stays uniform with the page views.
- Views bind the VM's `Fields` schema → the `Form` control (`Birko.Xaml.Avalonia`); create/edit is
  inline (`EditingItem` + `SaveEditing`/`CancelEdit`), gated by the VM permission flags.
- The demo list shows entity `ToString()` — a real app sets a `ListBox` `ItemTemplate` / display member.

## Scope (STORY-036 done)

Delivered: **sidebar shell** (`BSidebarAppShell`) + **ribbon shell** (`BAppShell`, `RibbonShellView`),
nav + ViewLocator + List/Detail/Split page views, **Ctrl+K command palette**, **header user area**,
**tenant switcher**, reusable **`FormModal`** page-shape, **cross-fade page transitions**, and a
token-restyled `ListBoxItem`. STORY-036 complete → EPIC-015 complete.

## Testing

Navigation (pure), ViewLocator resolution, and a headless `ShellView` render + screenshot live in
`Birko.Xaml.Avalonia.Tests` (it references Shell + reuses that project's Fluent+tokens TestApp).
