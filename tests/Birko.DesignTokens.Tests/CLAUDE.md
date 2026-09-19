# Birko.DesignTokens.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.DesignTokens`, the build-time token
generator that turns `tokens.json` into web CSS and Avalonia AXAML (via the
`generate` / `verify` / `extract` CLI verbs).

## Project Location

`tests/Birko.DesignTokens.Tests/`

## Scope

- `CssParityTests` — the STORY-029 acceptance gate: regenerating each sheet from
  `tokens.json` reproduces the hand-authored web CSS byte-for-byte (line-ending
  normalized to LF). Also asserts all five source sheets are present
  (light/dark/neon/finstat/inverse), every sheet has a selector and non-empty body,
  each token is single-sourced within a sheet, and that `CssIo.Extract` → `CssIo.Emit`
  round-trips the live CSS losslessly (independent of `tokens.json`).
- `AxamlEmitterTests` — `AxamlEmitter.Generate` emits a single well-formed
  `Tokens.axaml`; one `ThemeDictionaries` entry per variant (Light/Dark/Neon/Finstat);
  custom variants keyed by `{x:Static themes:BirkoThemeVariants.*}`; primary colour and
  `var()` references resolve per variant; all variant dictionaries expose the same key
  set (swap safety); every colour has a root `SolidColorBrush` linked by
  `DynamicResource`. Plus conversion unit tests for `TryColor` (hex/rgb/rgba → Avalonia
  hex), `TryLengthToPx` (rem baked at 16px), and `ToKey` (token name → PascalCase key).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, nullable + implicit usings enabled).
  References the real `Birko.DesignTokens.csproj` via `ProjectReference` — it is a
  buildable CLI tool exposing a public model + emitters, not a `.projitems` shared project.
- One test class per concern; assert byte-parity where relevant.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
