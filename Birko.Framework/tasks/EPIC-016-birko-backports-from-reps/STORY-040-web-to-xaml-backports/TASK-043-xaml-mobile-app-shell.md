---
id: TASK-043
parent: STORY-040
feature: null
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: [TASK-050]
pr: null
github-issue: null
jira-key: null
---

# Xaml mobile app-shell (BMobileAppShell equivalent)

## Context

`Birko.Web.Shell` gained `BMobileAppShell` from Reps (STORY-038 / origin TASK-038): a fixed top-bar
+ safe-area bottom-nav driven by a declarative `Surface[]` nav-model. The 2026-07-06 Web→Xaml review
rated this the **strongest** Web→Xaml candidate: `Birko.Xaml.Shell` ships only the desktop *sidebar*
(`BSidebarAppShell`) and *ribbon* (`BAppShell`) shells and has **no** mobile/bottom-nav shell — yet
the web `Surface[]` nav-model maps almost 1:1 onto Xaml.Core's existing `ModuleDefinition`
(Id/Label/Icon/route). So the nav-model layer already exists; only the mobile chrome is missing.

Respect the family split: platform-neutral view-model/nav logic in `Birko.Xaml.Core` (which is
contractually Avalonia-free), the shell view/chrome in `Birko.Xaml.Shell`. The web `safe-area-inset`
CSS maps to Avalonia `SafeAreaPadding`/insets on mobile backends.

## Acceptance criteria

- [x] A mobile app-shell view in `Birko.Xaml.Shell` (fixed top-bar + bottom-nav) sibling to the
      existing `ShellView`/`RibbonShellView`, driven by the existing `ModuleDefinition` nav-model. — `Views/MobileShellView.axaml`.
- [x] Bottom-nav renders one item per surface (icon + label) and switches the active surface via the
      existing `INavigationService`; active item is visually indicated. — screenshot: Home highlighted primary, Log/Stats muted.
- [x] Safe-area / inset handling wired so it behaves on a mobile viewport (no-op on desktop). — `MobileShellView.axaml.cs` via `IInsetsManager` (null on desktop → no-op).
- [x] No new nav-model type invented — reuse `ModuleDefinition`. — `MobileNavItem` is a *projection* (Id/Label/Icon + observable `IsActive`), not a new nav model.
- [x] Tests in `Birko.Xaml.Core.Tests` for the shell view-model (surface list → active-surface switching). — `MobileShellViewModelTests` (6 tests) + 4 headless render tests in `Birko.Xaml.Avalonia.Tests`.
- [x] `Recent Updates` entry added. — `Birko.Framework/CLAUDE.md` + `Birko.Xaml.Shell/CLAUDE.md`.

## Out of scope

- The gallery showcase (TASK-050).
- Offline/sync integration (TASK-046/047).
- Android/iOS TFM work on `Birko.Xaml.Avalonia` — deliver desktop-runnable; mobile-target is a separate precondition.

## Human test plan

- [x] Host the shell in a scratch Avalonia window sized to a phone viewport; confirm bottom-nav
      switches surfaces and the top-bar stays fixed. — headless render at 390×780; screenshot shows fixed
      top-bar + content + bottom-nav with the active surface highlighted; switch test green.
- [x] Confirm `Birko.Xaml.Core` still has no Avalonia dependency (the neutral half compiles alone). — `CoreIsAvaloniaFreeTests` green; `MobileNavItem` uses only CommunityToolkit.Mvvm.

## Implementation plan

_Populated by `/tasks plan TASK-043` — leave empty until then._
