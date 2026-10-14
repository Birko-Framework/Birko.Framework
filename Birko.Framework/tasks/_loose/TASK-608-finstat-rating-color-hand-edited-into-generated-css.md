---
id: TASK-608
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-046]
pr: null
github-issue: null
jira-key: null
---

# `--b-rating-color` was hand-edited into the generated `finstat.css`, so token parity is red

## Context

Birko.Web `ddef16e` (Web TASK-579, `b-rating`) added `--b-rating-color: #a77400` straight into
`Birko.Web.Components/css/themes/finstat.css`, a file generated from `Birko.DesignTokens/tokens.json` and bannered
DO NOT EDIT. `CssParityTests` failed on `themes/finstat.css` (measured locally, 1 of 56). token-parity.yml is
path-filtered to this repo's token files, so no run surfaced it; its last red run (`829034b9`, 2026-10-11, `dark.css`
and `tokens.css`) predates the edit and those two files match again today.

Adding the token to the finstat sheet alone fixes the CSS but fails `All_variant_dictionaries_expose_the_same_key_set`:
the Avalonia variants are built from the light base plus the theme's overrides, so a theme-only token is a key only
one variant has. Dropping it from the AXAML would silently lose finstat's colour (rule 41).

## Acceptance criteria

- [x] `tokens.json` carries `--b-rating-color` in the base sheet (`var(--b-color-warning)`, what `b-rating.ts` already
      falls back to) and finstat's `#a77400` override, with TASK-579's comment
- [x] Regenerated: `tokens.css` gains the base line; `finstat.css` is byte-identical to Web `ddef16e`; every AXAML
      variant gains `BRatingColor`, and `Tokens.Brushes.axaml` gains `BRatingColorBrush`
- [x] `Birko.DesignTokens.Tests` 56/56; `dotnet run --project Birko.DesignTokens -- verify` reports no drift

## Out of scope

- Why a hand edit still reaches a generated file: Web carries a parity gate (`262ef12`); whether it ran on `ddef16e`
  is not checked here.

## Outcome

Fixed at the source. The non-finstat themes render as before (the base value is the component's own fallback).
Two commits: this one, and the Birko.Web one that adds the `tokens.css` line.
