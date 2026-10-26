---
id: TASK-775
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P3
assignee: ai
created: 2026-10-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko `ControlTheme` for Avalonia's `Expander`, matching `b-accordion`

## Context

FEATURE-020 D10 + D12. Birko.Xaml.Avalonia has no Expander theme, so an `Expander` under a Birko theme renders with
Fluent's look. Style it to match `b-accordion` (`Birko.Web.Components/src/layout/b-accordion.ts`): header, chevron,
borders, spacing, hover/focus. Exclusive single-open groups are deferred (D11).

## Acceptance criteria

- [x] `ControlTheme` for `Expander` registered in the Birko theme; header, chevron rotation, border, padding and
      hover/focus use theme tokens; all four expand directions still work.
- [x] Keyboard: Space/Enter toggles; focus visual as other Birko buttons (TASK-103).
- [x] Headless test: the theme applies, toggling expands and collapses the content.
- [x] Gallery page with stacked expanders; docs and Recent Updates updated.

## Out of scope

- An accordion group where opening one section closes the others (D11, deferred).

## Human test plan

1. Gallery expander page: compare with the web Playground's `b-accordion` side by side — header, chevron and spacing
   look alike.
2. Click and keyboard-toggle a section: it opens and closes, the chevron turns.
3. Check under each theme.

## Implementation plan

## Review log

- 2026-10-26 — Built; **verify** (Human test plan needs a person at the Gallery, beside the web Playground's
  `b-accordion`). `Controls/Surfaces.axaml`: `{x:Type Expander}` + `BExpanderHeader` — a bordered `BBgBrush` surface
  (`BRadius`), a full-width header row (`BControlMinHeight`, 12,8 padding, medium weight, `BBgSecondaryBrush` on hover,
  a `BBorderFocusBrush` frame on keyboard focus, muted when disabled), a chevron turned 90° when open, the panel under a
  divider. `:up` / `:left` / `:right` re-dock the header. No code: Avalonia's `Expander` keeps its behaviour.
- Tests: `ExpanderThemeTests` 8 + 2 screenshots. A first draft set the header's `IsChecked` as a local value and then
  found `IsExpanded = false` no longer reached it — that replaces the two-way template binding, which real input never
  does; the test now toggles by key, and the gotcha is in the project CLAUDE.md. **Proved they can fail:** the Up
  direction docked at the top and no chevron turn failed 2. Avalonia suite 384/384.
- Looked at (headless, Light and Dark): open, closed and disabled sections read as `b-accordion` does. Not compared side
  by side with the web Playground yet — that is Human test plan step 1.
- Gallery: Composites tab, "Expander — the b-accordion look": open, closed, upward and disabled sections.
- Recent Updates: folded into the same-day entry; the oldest entry ("Grants.SQL's lookup indexes…") rolled into
  CHANGELOG.md to stay under 10 KB, as earlier today.
