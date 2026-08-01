---
id: TASK-096
parent: STORY-048
feature: FEATURE-015
status: todo
priority: P2
assignee: ai
created: 2026-07-29
depends-on: [TASK-092]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Roll Avalonia 12 out to consumer repos in lockstep

## Context

**Avalonia majors are not binary compatible** — the upstream release notes are explicit that all
third-party libraries must be recompiled against the new version. So the moment TASK-092 ships
`Birko.Xaml.Avalonia` built against Avalonia 12.1.0, any consumer still on 11.2.3 breaks — and
because `Birko.Xaml.*` are referenced as **real assemblies** (the EPIC-015 convention break: real
`.csproj` class libraries, not `.shproj`/`.projitems` source includes), a mismatch surfaces as a
load/bind failure at runtime rather than a compile error at build time. That's the bad direction to
find out.

Consumers live in their own repos (`Birko/Consumers/…`), outside this aggregator, so they can't be
fixed in the same commit as TASK-092 — hence this task, and hence the ordering: land TASK-092, then
sweep consumers immediately, not eventually.

The gallery app is also a consumer: EPIC-015's completion note records "the gallery lives in
Consumers/", and `Birko.Xaml.Avalonia.Tests` references `Birko.Xaml.Shell` specifically so the
parity-screenshot test can render the real `GalleryView`.

### What each consumer needs

- `TargetFramework` → `net10.0` (Avalonia 12 dropped `net6.0`/`netstandard2.0`; `net8.0` still works
  but defeats the point).
- Its own direct `Avalonia*` `PackageReference`s → 12.1.x, including `Avalonia.Desktop`,
  `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` and any platform backends the framework projects
  don't reference.
- **`Avalonia.Diagnostics` is removed in 12** → replace with `AvaloniaUI.DiagnosticsSupport` and
  `AttachDevTools()` → `AttachDeveloperTools()`. This hits app-startup code, which is consumer-side
  only, so TASK-092's build never sees it.
- `AppBuilder` review: `.UseSkia()` now wants an explicit `.UseHarfBuzz()` + the `Avalonia.HarfBuzz`
  package for text shaping (per the 12 docs; headless rendered fine without it, a windowed app is the
  real test — same open question as TASK-092's human test plan).
- Any consumer-side use of the APIs that broke here: drag-drop (`DataObject`/`DoDragDrop`/
  `e.Data`), `GotFocus`/`LostFocus` handlers typed as `GotFocusEventArgs`, `Gestures.*` events in
  XAML (the prefix is dropped — the events moved to `InputElement`), `Window.SystemDecorations` /
  `ExtendClientAreaChromeHints`, `OpenFileDialog`/`SaveFileDialog`, `IStyleable`.
- Consumer test projects using `Avalonia.Headless.XUnit` need the same xunit v2 → v3 swap as
  TASK-092, **including keeping `xunit.runner.visualstudio`** or `dotnet test` silently finds no tests.

## Acceptance criteria

- [ ] Every repo referencing `Birko.Xaml.Avalonia` / `Birko.Xaml.Shell` enumerated (search `Birko/Consumers/`; don't rely on memory of which ones exist).
- [ ] Each one on `net10.0` + Avalonia 12.1.x, building clean.
- [ ] `Avalonia.Diagnostics` → `AvaloniaUI.DiagnosticsSupport` with `AttachDeveloperTools()` wherever DevTools was attached.
- [ ] Each consumer's own suite green; any consumer headless-xunit project migrated to v3 with `dotnet test` still discovering tests.
- [ ] The gallery app launches and its demos render (it is the de-facto visual smoke test for the whole catalogue).
- [ ] No repo left referencing Birko.Xaml at Avalonia 11 — including any that only reference it transitively through an aggregator project.

## Out of scope

- The framework-side bump (TASK-092) and its warning sweep (TASK-094).
- Non-Avalonia consumers of unrelated `Birko.*` projects — they are unaffected by this and must not be dragged into the change.
- Migrating consumers to `net10.0` for reasons other than Avalonia.

## Human test plan

- [ ] Launch each consumer app and click through its main flows — a binary-compatibility mismatch shows up as a runtime load failure, which no build step catches.
- [ ] Open DevTools in a consumer app after the `AttachDeveloperTools()` swap and confirm the inspector still attaches.
- [ ] Check text rendering and font fallback in a real window (the `.UseHarfBuzz()` question).

## Implementation plan

_Populated by `/tasks plan TASK-096` — leave empty until then._
