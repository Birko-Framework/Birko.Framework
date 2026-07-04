---
id: STORY-031
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-04
---

## Resolution (2026-07-04) — GATE: **GO**

Built the first restyled ControlThemes (`Button`, `TextBox` + reusable `Card`/`Badge` themes) in
`Birko.Xaml.Avalonia/Controls/Controls.axaml` (+ a one-include `BirkoTheme.axaml`), and a runnable
`Birko.Xaml.Gallery` desktop app. **15 headless tests** (Avalonia.Headless.XUnit) prove controls
resolve the correct token values per variant and re-theme live; a Skia screenshot test renders the
gallery to a PNG per theme.

**Visual parity confirmed** across light/dark/neon/finstat — colors match the shared token source,
and finstat's **flat/square corners** come through (proving the radius token re-themes). Verdict:
**GO** — Tier-1 (STORY-034) proceeds.

**Fix surfaced by the gate:** a `double` radius token can't bind to a `CornerRadius` property
(runtime `InvalidCastException`), so the STORY-029 generator now emits `--b-radius*` tokens as
`CornerRadius` resources (not `x:Double`). CSS byte-parity unaffected.

**Deferred:** the full restyled-control sweep (STORY-034); composite/motion tokens; Card/Badge as
first-class custom controls (currently named `ControlTheme`s on `ContentControl`).

# Tier 0 validation — Avalonia gallery app + first restyled controls

## User story

As a framework maintainer, I want a tiny Avalonia gallery app showing 3–4 restyled controls
across all themes, so I can prove the token pipeline works before committing to the full control sweep.

## Behaviour

- A minimal Avalonia gallery app (mirrors how the Birko.Web Playground validated components) renders 3–4 representative controls — e.g. `Button`, `TextBox`/`b-input`, `b-card`, `b-badge`.
- The theme switcher cycles light/dark/neon/finstat and every control re-themes live.
- Side-by-side visual comparison against the Birko.Web equivalents confirms parity (colors, spacing, radius, typography match).
- This story is the **go/no-go gate**: if parity holds and runtime swap works, Tier-1 (STORY-034) proceeds; if not, the token mapping (STORY-029/030) is corrected first.
- Gallery app is a dev/validation artifact, not a shipped consumer — kept in the framework workspace.
