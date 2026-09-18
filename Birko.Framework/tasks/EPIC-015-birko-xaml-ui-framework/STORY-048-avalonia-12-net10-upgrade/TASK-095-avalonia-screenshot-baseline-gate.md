---
id: TASK-095
parent: STORY-048
feature: FEATURE-015
status: todo
priority: P2
assignee: ai
created: 2026-07-29
depends-on: []
blocks: [TASK-092]
pr: null
github-issue: null
jira-key: null
---

# Screenshot baseline gate for the Avalonia suite (build it *before* the Av12 bump)

## Context

**The Avalonia suite is visually blind.** Verified 2026-07-29 by grepping the whole test project for
`baseline|golden|snapshot|ComparePixels|GetPixel|expected.png` — **zero hits**. 18 tests call
`CaptureRenderedFrame(window)` and then `frame?.Save(...)` to a directory, and assert only structural
facts alongside it. `ChartTests.cs:88` is representative:

```csharp
var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
frame?.Save(Path.Combine(dir, "chart.png"));
Inner(chart).Series!.Count().Should().Be(2);   // <- the only assertion
```

So **144/144 green does not mean visually unchanged.** That is tolerable on a stable toolkit and
actively dangerous for TASK-092, which changes the rendering engine underneath (SkiaSharp 2.88 → 3)
*and* the base control themes (Fluent 12 restyled its own). A visual regression would ship green.

This is the **same class of gap** as the CSS drift found on 2026-07-29: generated CSS had been
hand-edited and `CssParityTests` had been red on main, while the AXAML dictionaries had only a
`verify` CLI verb and nothing ran a CLI verb by itself. That was closed with `AxamlParityTests`
gating from the *suite*. Same fix shape here: the artifact must be compared by something that runs
on every build.

**Sequencing matters and is the point of this task.** The baseline must be captured on **Avalonia
11.2.3**, so the bump has a real before-picture to be diffed against. Build it after the bump and it
just enshrines whatever 12 happens to render, which is worth nothing.

### Design notes

- Headless Skia is already enabled — `TestApp.BuildAvaloniaApp()` uses
  `AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }`, so real pixels are available.
  No infrastructure change needed.
- Compare with a **tolerance** (per-pixel delta + a max-differing-pixel-fraction), not byte equality
  — antialiasing and font rasterization vary across machines and SkiaSharp patches. A strict
  byte-compare will flap in CI and get disabled, which is worse than no gate.
- Commit baselines as test assets; on failure write the actual + a diff image next to the expected so
  the reviewer can see *what* moved without re-running locally.
- Re-baselining must be a deliberate, reviewable act (an explicit switch/env var), never automatic.
- The 32 Avalonia test classes were all made to pass in isolation on 2026-07-29 — don't regress that:
  the gate must not depend on another test having rendered first.

## Acceptance criteria

- [ ] A shared comparison helper (capture → compare against a committed baseline → on mismatch write actual + diff) usable from the existing screenshot tests.
- [ ] Tolerance-based comparison with the threshold chosen deliberately and commented; verified to actually **fail** by perturbing one baseline (the same way `AxamlParityTests` was proven).
- [ ] Baselines captured on **Avalonia 11.2.3** and committed, covering at minimum: chart, form field types, ribbon, shell, mobile shell, modal, drawer, kanban, command palette, object tree, XML viewer, markdown editor, tree.
- [ ] An explicit re-baseline path (env var or flag) that is not the default and is documented.
- [ ] Every gated test still passes **in isolation** (no ambient-`Application` or render-order dependency).
- [ ] Baseline set is complete-by-construction — a test that captures a frame but has no baseline fails rather than silently skipping (the `Themes/`-holds-exactly-the-generated-set trick from `AxamlParityTests`).
- [ ] `Recent Updates` entry; the gap and its fix recorded in `Birko.Xaml.Avalonia.Tests/CLAUDE.md`.

## Out of scope

- The Avalonia 12 bump (TASK-092) — this task only builds the net.
- Cross-platform baselines (Linux/macOS renderers differ); Windows-only is fine, note the limitation.
- Web-side visual regression (`Birko.Web.*` verifies via the Playground) — different toolchain.

## Human test plan

- [ ] Deliberately restyle one token (e.g. a primary brush), run the suite, and confirm the failure message + diff image make the change obvious to someone who didn't write the test.
- [ ] Confirm the baselines are legible as reviewable artifacts in a PR diff (image-sized, sensibly named), not an opaque blob dump.

## Implementation plan

_Populated by `/tasks plan TASK-095` — leave empty until then._
