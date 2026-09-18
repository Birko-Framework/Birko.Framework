# Birko.DesignTokens

Single-source design-token generator for the Birko design system (EPIC-015 / STORY-029).

`tokens.json` is the **one place** design-token values live. A small C# tool reads it and emits
every consuming target, so the web (CSS) and desktop (Avalonia XAML) design systems can never
drift:

```
tokens.json  ──►  Birko.Web.Components/css/tokens.css  (+ themes/*.css)   [byte-identical]
             └─►  Birko.Xaml.Avalonia/Themes/*.axaml                       [Color+Brush / px / FontFamily]
```

## Commands

```bash
# Everyday command — regenerate all targets from tokens.json:
dotnet run --project Birko.DesignTokens -- generate

# Confirm the on-disk CSS still matches tokens.json (CI / pre-commit); exit 1 on drift:
dotnet run --project Birko.DesignTokens -- verify

# Bootstrap / re-derive tokens.json from the current hand-authored CSS (rarely needed):
dotnet run --project Birko.DesignTokens -- extract
```

Path resolution follows the Birko `$(BirkoSrc)` convention: `--root <birkoRoot>` wins, then the
`BIRKO_SRC` env var, then it walks up to find the folder containing both `Framework` and `Web`.

## How byte-identical CSS parity works

Each CSS file is modeled (in `tokens.json`) as `prologue` + `{selector} {` + ordered body lines
+ `}` + `epilogue`. Every physical body line is one node: a structured **`var`** (name / value /
verbatim trailing comment — the trailing text preserves the hand-authored comment-column
alignment) or verbatim **`raw`** (comments, blank lines). Token **values** are single-sourced on
the `var` node; comments and layout round-trip verbatim. The acceptance gate is
`emit(tokens.json) == the committed CSS`, proven by the test suite and `verify`.

Line endings: the repo canonically stores these files as **LF** (autocrlf may present CRLF in a
working tree). The token model is kept in canonical LF, and comparisons normalize CRLF→LF, so
generation is byte-stable regardless of a machine's checkout settings.

## AXAML mapping (STORY-029 first cut)

- `--b-color-x` (hex / `rgb()` / `rgba()`) → `<Color x:Key="BColorX">` + `<SolidColorBrush x:Key="BColorXBrush">`. `rgba` alpha folds into Avalonia `#AARRGGBB`.
- `rem`/`px` lengths → `<x:Double>` with **rem baked to px at 16px root**; unitless `0` → `0`.
- `--b-font*` → `<FontFamily>`.
- `var(--x)` references resolve to the active theme's value, so each theme dictionary is full and self-contained (a theme swap is a whole-dictionary replace).
- Composite/motion tokens (shadows, focus rings, transitions, cubic-bezier easings, durations, gradients) are **deferred to STORY-030** (Avalonia theme system / `ThemeVariant` / `DynamicResource` wiring).

## Convention note

This is the **first real, buildable `.csproj` in the `Birko\Framework` bucket** — every other
sibling is a `.shproj`/`.projitems` shared project. Justified: it is a build-time code-generation
**tool** (needs build output), not a runtime shared library, so it is **not** imported into the
`Birko.Framework.csproj` aggregator. See `CLAUDE.md` and EPIC-015 for the documented deviation.
