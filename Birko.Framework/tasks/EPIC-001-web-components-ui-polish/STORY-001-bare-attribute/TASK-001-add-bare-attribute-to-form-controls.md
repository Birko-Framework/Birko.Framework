---
id: TASK-001
parent: STORY-001
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-002]
pr: null
github-issue: null
jira-key: null
---

# Add `bare` attribute to all form controls

## Context

Eight components need a `bare` boolean attribute that strips the `.field` wrapper — no label slot, no error-message row below. Intended for inline use cases (toolbars, table cells, floating action bars) where the outer stacked chrome is unwanted. Pairs with `size="sm"` for dense layouts.

## Acceptance criteria

- [ ] `b-input` supports `bare="true"`; rendering skips `.field` / label / error span
- [ ] Same for `b-select`, `b-multi-select`, `b-textarea`, `b-tag-input`, `b-search-input`, `b-date-picker`, `b-datetime-picker`
- [ ] Error / label state still surfaced via attributes (just not rendered by the component)
- [ ] Tests added for at least three representative components
- [ ] `Birko.Web.Components/CLAUDE.md` updated with the `bare` convention

## Out of scope

- Migration of `b-editable-table` cells onto these bare variants (TASK-002)
- Bare variants on non-form components
