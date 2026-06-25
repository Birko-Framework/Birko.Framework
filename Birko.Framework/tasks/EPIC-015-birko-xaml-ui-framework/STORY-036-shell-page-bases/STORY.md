---
id: STORY-036
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
