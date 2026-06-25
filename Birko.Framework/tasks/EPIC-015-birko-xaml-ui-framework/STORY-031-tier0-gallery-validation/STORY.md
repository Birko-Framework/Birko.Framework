---
id: STORY-031
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
