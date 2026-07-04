---
id: STORY-030
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-04
---

## Resolution (2026-07-04)

Delivered the Avalonia theme system. `Birko.Xaml.Core` (Avalonia-free: `ThemeInfo`, `BirkoThemes`,
`IThemeManager`) + `Birko.Xaml.Avalonia` (`BirkoThemeVariants`, `AvaloniaThemeManager`, generated
`Themes/Tokens.axaml`). Both are the first **real Avalonia/.NET assemblies** in the framework
bucket (the EPIC convention break), `net8.0` (Avalonia 11.2.3). **8 headless tests green** incl. a
live `RequestedThemeVariant` swap re-resolving a `DynamicResource` brush.

**Key decisions (spike-verified before building):**
- **ThemeDictionaries, not swapped MergedDictionaries** — single `Tokens.axaml` with one
  `ThemeDictionaries` entry per theme; setting `RequestedThemeVariant` re-resolves `DynamicResource`
  live. Spike proved Avalonia resolves **custom** variants (neon/finstat), not just Light/Dark, via
  `TryGetResource(key, variant)` with `InheritVariant` fallback.
- **Key identity** — `ThemeDictionaries` keys are `{x:Static themes:BirkoThemeVariants.X}`, the same
  static `ThemeVariant` instances assigned to `RequestedThemeVariant`.
- **Brushes at root, colors per-variant** — one brush per color token, `Color="{DynamicResource …}"`,
  so a swap updates it without per-theme brush duplication.
- **Generator restructured** (STORY-029's `AxamlEmitter`) to emit this single ThemeDictionaries file;
  CSS byte-parity unaffected (still `verify`-clean). `Birko.Xaml.Core` stays Avalonia-free (constraint #1).

**Deferred to later stories:** composite/motion tokens (shadows/focus-rings/transitions/easings/
gradients) — map when a control needs them (STORY-034); gallery app (STORY-031); scoped `inverse`
theme in AXAML.

# Tier 0 — Avalonia theme system + runtime ThemeVariant swap

## User story

As an app developer, I want to drop in the Birko theme dictionaries and switch theme at runtime,
so my Avalonia app matches Birko.Web's look and supports light/dark/neon/finstat out of the box.

## Behaviour

- The generated `Tokens.axaml` + `Theme.*.axaml` (from STORY-029) load as merged `ResourceDictionary`s in an Avalonia app.
- Controls reference tokens via `{DynamicResource BColorPrimaryBrush}` etc. — never hard-coded values.
- Runtime theme switch wired to Avalonia `ThemeVariant` / `RequestedThemeVariant`; changing it re-resolves all `DynamicResource` references live (no restart).
- A theme-manager helper in `Birko.Xaml.Core` exposes the available themes + the active one (the XAML analogue of the Shell theme registry / `setTheme`).
- All four themes render correctly (colors, surfaces, radius, typography) and swap cleanly.
- **Constraint:** the token-consumption helper stays Avalonia-free in `Core` where possible; anything Avalonia-specific is isolated so a future WPF skin can supply its own swap mechanism (MergedDictionaries) — see EPIC WPF addendum constraint #1.
