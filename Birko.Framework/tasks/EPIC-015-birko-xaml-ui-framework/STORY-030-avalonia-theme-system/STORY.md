---
id: STORY-030
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
