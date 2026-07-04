---
id: STORY-029
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-04
---

## Resolution (2026-07-04)

Delivered `Birko.DesignTokens` — `tokens.json` single source + C# generator (`extract`/`generate`/
`verify`). CSS round-trip is **byte-identical** to the committed files (all 5, proven vs `git
show HEAD:`); AXAML dictionaries (Tokens.axaml + Theme.{Light,Dark,Neon,Finstat}.axaml) emitted
into `Birko.Xaml.Avalonia/Themes/`. 32 xUnit tests green.

**Open questions resolved:**
- *Generator language / tokens.json home* → **C# tool in `Birko\Framework\Birko.DesignTokens`**
  (single .NET toolchain for EPIC-015; `tokens.json` beside it). Kept language-neutral so a
  later TS swap is a one-file emitter rewrite against the golden CSS.
- *Comment/ordering tolerance* → **exact byte-identical** (chosen gate). Per-line node model
  preserves comments + alignment verbatim; values single-sourced.
- *Line endings* → repo is canonically **LF**; model normalized to LF, comparisons EOL-independent.

**Deferred to STORY-030:** composite/motion AXAML tokens (shadows, focus rings, transitions,
easings, durations, gradients), `ThemeVariant`/`DynamicResource` wiring, and the scoped `inverse`
partial theme in AXAML (CSS-only for now).

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
