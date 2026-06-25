---
id: STORY-029
parent: EPIC-015
status: planned
created: 2026-06-25
---

# Tier 0 — single-source design tokens + multi-target generator

## User story

As a framework maintainer, I want the design tokens defined in one neutral source that generates
both the web CSS and the XAML dictionaries, so the desktop and web design systems can never drift.

## Behaviour

- New `Birko.DesignTokens` project holds `tokens.json` — the single source for all four themes (light/dark/neon/finstat), extracted from today's hand-authored `tokens.css` + `themes/*.css`.
- A generator (`generate.ts` or a C# tool — open question) emits:
  - `Birko.Web/.../css/tokens.css` (+ `themes/*.css`) — **byte-identical to the current hand-authored files** (the round-trip parity proof; this is the acceptance gate).
  - `Birko.Xaml.Avalonia/Themes/Tokens.axaml` + `Theme.{Light,Dark,Neon,Finstat}.axaml`.
- Token mapping rules: `--b-color-x` → paired `Color` + `SolidColorBrush` (`BColorX` / `BColorXBrush`); spacing/radius `rem` → px baked at generation time; font families → `FontFamily`.
- Adding a new theme or token = editing `tokens.json` only; regeneration propagates to every target.
- **Edge case:** the existing `tokens.css` must be reproduced exactly (comments/ordering tolerance to be decided) — if it can't round-trip, the extraction is wrong.
- **Open:** where `tokens.json` physically lives (bridges the TS `Birko\Web` and .NET `Birko\Framework` buckets); generator language.
