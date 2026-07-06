---
id: STORY-036
parent: EPIC-015
status: in-progress
created: 2026-06-25
---

## Progress (2026-07-04)

Delivered the shell **MVP** — a working desktop CRUD app shape end-to-end.

**Done:**
- **Navigation (Core, Avalonia-free):** `ModuleDefinition` (the `buildModuleRoutes`/`ModuleManifest`
  analogue), `INavigationService`/`NavigationService` (module map + history + `Current`), `ShellViewModel`
  (nav + `IThemeManager` + title, Navigate/Back/SetTheme commands), `SplitPageViewModel<T>`. Page-base
  VMs get a `Fields` schema.
- **`Birko.Xaml.Shell` (Avalonia):** `ViewLocator` (naming convention + generic base-page mapping),
  `ShellView` (**sidebar** chrome: module nav + header title + theme switcher + content region + status
  bar — the `BSidebarAppShell` analogue), and generic `ListPageView` (gated toolbar + search + inline
  create/edit `Form`), `DetailPageView` (`Form` + Save/Cancel), `SplitPageView` (master list + detail
  `Form` over `SplitPanel`).
- **Proven:** 6 headless tests (navigation, ViewLocator resolves Split/List/Detail, shell renders the
  active page via the locator, nav swaps the page) + a full-shell screenshot. Avalonia suite now 38.
  Constraint #3 honored (nav + shell VMs are Avalonia-free in Core).

## Update (2026-07-06)

- **Command palette wired (Ctrl+K):** `ShellViewModel` builds `PaletteCommands` from modules
  (navigate) + themes (switch), exposes `IsPaletteOpen`/`OpenPaletteCommand`; `ShellView` overlays a
  `CommandPalette` bound to it, opened by a `Ctrl+K` `KeyBinding`. Tested (commands built,
  OpenPalette opens, palette command navigates, Ctrl+K binding → `IsOpen`).
- **User area:** header shows an avatar + `UserName` (hidden when empty) with a Flyout of
  `UserCommands` (`RunUserCommand`). Screenshot-verified.

- **Tenant switcher:** header `ComboBox` bound to `ShellViewModel.Tenants`/`CurrentTenant`, shown when
  `HasMultipleTenants`. Screenshot-verified.
- **`FormModal` page-shape:** reusable `FormModal` control (`Birko.Xaml.Avalonia`) composing `Modal` +
  `Form` + Save/Cancel (`IsOpen`/`Title`/`Fields`/`Model`/`SaveCommand`/`CancelCommand`). Verified
  (hosts modal+form, Save runs command + closes, Cancel closes, screenshot). The epic's `FormModal<T>`.

- **Ribbon (`BAppShell` chrome):** `Ribbon` control (`Birko.Xaml.Avalonia`) over Core
  `RibbonTab`/`RibbonGroup`/`RibbonItem` — tab strip + active tab's labeled groups of icon+label
  command buttons, `SelectedIndex` switching, item `Run`. Verified (render/click/switch tests +
  screenshot). Covers `b-ribbon`.

**Remaining / deferred:** a first-class **RibbonAppShell view** composing the `Ribbon` over the
content region (the control exists; a shell wired to `ShellViewModel.RibbonTabs` is a thin follow-up),
`TransitioningContentControl` animations, ListBox restyle/display templating.

# Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation

## User story

As an app developer, I want the Birko.Web page shapes and app chrome on XAML, so I can build a
desktop CRUD app with the same list/detail/split/form-modal patterns and ribbon/sidebar shell.

## Behaviour

- **Page bases** (base VMs from STORY-032 + generic views): `BasePage`, `ListPageViewModel<T>` (auto-fetch list + toolbar + create/edit modal + delete confirm + permissions), `DetailPageViewModel<T>` (load-by-id, form bind, save/cancel), `SplitPageViewModel<T>` (master-detail over `SplitPanel`), and `FormModal<T>` (dialog + `Form` + Save/Cancel).
- **App chrome** — `Birko.Xaml.Shell` mirrors `BCoreAppShell → BSidebarAppShell → BAppShell`: ribbon → sidebar → status bar, command palette, theme switcher, optional user area / tenant switcher.
- **Navigation** — re-architected from Birko.Web's hash router into Avalonia `ViewLocator` + region/`TransitioningContentControl` + a module/route map (the `buildModuleRoutes` analogue).
- Behavior (data load, CRUD orchestration, permission gating) carries over from Birko.Web ~verbatim; only the view templates are new.
- **Dependency:** sits on STORY-032 (base VMs), STORY-033 (Form/Drawer/SplitPanel), STORY-034/035 (controls). Build last.
- **Constraint #3:** keep page-base VMs + navigation logic platform-neutral (in `Core`/a `Shell.Core`); only views/chrome live in the Avalonia assembly, so a WPF skin reuses the VMs.
