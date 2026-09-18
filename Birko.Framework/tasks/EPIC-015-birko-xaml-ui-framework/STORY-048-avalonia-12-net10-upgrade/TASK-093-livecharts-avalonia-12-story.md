---
id: TASK-093
parent: STORY-048
feature: FEATURE-015
status: todo
priority: P2
assignee: human
created: 2026-07-29
depends-on: []
blocks: [TASK-092]
pr: null
github-issue: null
jira-key: null
---

# Decide the LiveCharts story for Avalonia 12 (the only blocker on the bump)

## Context

**This is the one thing standing between us and Avalonia 12** — not the API surface, which turned out
to be two small code fixes (TASK-092). Verified on NuGet 2026-07-29:

| Version | Avalonia dep | SkiaSharp | TFMs | Status |
|---|---|---|---|---|
| `2.0.5` (**latest stable**) | `11.0.0` | 2.88 (via `LiveChartsCore.SkiaSharpView`) | `net6.0`, `net8.0`, `netstandard2.0` | incompatible with Av12 |
| `2.1.0-dev-798` | `12.0.0` | 3.x | `net8.0`, `net10.0` | works — spike-tested |

Avalonia 12 requires **SkiaSharp 3.119.4** and Avalonia majors are **not binary compatible**, so
LiveCharts 2.0.5 cannot be used against Avalonia 12 on either axis. There is no stable 2.1.0 — the
only published 2.1.0 builds are `-dev-*` prereleases (`-247`, `-292`, `-365`, `-570`, `-798`).

In the spike, `2.1.0-dev-798` restored cleanly, `BChart.cs` compiled unchanged, and `ChartTests`
passed. So the technical path works — the question is whether `Birko.Xaml.Avalonia`, a framework
library that consumer solutions depend on, should carry a `-dev` prerelease into their dependency
graphs. That's a judgment call for a human, hence `assignee: human`.

`BChart` came from EPIC-015 STORY-035 (Tier-2 composites, "chart on LiveCharts2") and is currently a
hard `PackageReference` in `Birko.Xaml.Avalonia.csproj:20` — every consumer of the whole XAML library
pulls LiveCharts + SkiaSharp whether they chart or not.

### The three options

1. **Ship on `2.1.0-dev-798`.** Fastest; unblocks TASK-092 immediately. Cost: a prerelease in a
   framework library, pinned exactly (never floating), with a follow-up to move to stable.
2. **Wait for a stable 2.1.0.** Zero risk, unknown date — parks the whole `net10.0` move behind a
   third party's release schedule.
3. **Decouple `BChart` behind an interface** so charts can lag the toolkit — e.g. move the LiveCharts
   dependency into a `Birko.Xaml.Avalonia.Charts` sibling, leaving the core library chart-free. Most
   work, but it also fixes the standing "everyone pays for SkiaSharp" problem and matches the
   framework's own optional-sibling pattern (`Birko.Models.*.SQL`, the per-backend store projects).

Option 3 is the one that stops this recurring at Avalonia 13 — recommend it if the extra scope is
acceptable, otherwise option 1 with a tracked follow-up.

## Acceptance criteria

- [ ] Check whether a stable `LiveChartsCore.SkiaSharpView.Avalonia 2.1.0` has shipped since 2026-07-29 (re-query NuGet — this may have resolved itself).
- [ ] One of the three options chosen, with the reasoning recorded here (not just in a commit message).
- [ ] If option 1 — the version is pinned exactly, the prerelease is called out in `Birko.Xaml.Avalonia/CLAUDE.md`, and a follow-up task to move to stable is filed.
- [ ] If option 3 — the split is designed (which project, what interface `BChart` sits behind, how the gallery wires it) and filed as its own task; `Birko.Xaml.Avalonia` no longer pulls SkiaSharp for non-charting consumers.
- [ ] `ChartTests` green under whichever path is chosen.
- [ ] TASK-092 unblocked with a concrete version to pin.

## Out of scope

- The bump itself (TASK-092).
- Replacing LiveCharts with a different charting library — a much bigger call; only raise it if 2.1.0 turns out to be abandoned.

## Human test plan

- [ ] Render the gallery's chart demo (both series) and confirm axes, legend, colors and animation still look right — SkiaSharp 2.88→3 is a rendering-engine major, and `ChartTests` only asserts `Series.Count() == 2`.

## Implementation plan

_Populated by `/tasks plan TASK-093` — leave empty until then._
