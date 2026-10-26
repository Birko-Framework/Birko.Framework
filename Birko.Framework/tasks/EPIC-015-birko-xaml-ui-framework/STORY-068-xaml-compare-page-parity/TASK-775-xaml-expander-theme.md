---
id: TASK-775
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `ControlTheme` for `Expander` registered in the Birko theme; header, chevron rotation, border, padding and
      hover/focus use theme tokens; all four expand directions still work.
- [ ] Keyboard: Space/Enter toggles; focus visual as other Birko buttons (TASK-103).
- [ ] Headless test: the theme applies, toggling expands and collapses the content.
- [ ] Gallery page with stacked expanders; docs and Recent Updates updated.

## Out of scope

- An accordion group where opening one section closes the others (D11, deferred).

## Human test plan

1. Gallery expander page: compare with the web Playground's `b-accordion` side by side — header, chevron and spacing
   look alike.
2. Click and keyboard-toggle a section: it opens and closes, the chevron turns.
3. Check under each theme.

## Implementation plan

## Review log
