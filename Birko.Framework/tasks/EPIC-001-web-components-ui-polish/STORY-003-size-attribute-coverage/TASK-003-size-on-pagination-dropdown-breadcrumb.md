---
id: TASK-003
parent: STORY-003
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# size attribute on b-pagination, b-dropdown-menu, b-breadcrumb

## Context

The `size` convention is documented in `Birko.Web.Components/CLAUDE.md` (five categories: vertical-footprint / text-scale / width / shape-weight / inline-chip). `b-button` and `b-badge` are normalized. These three components still lack `size`.

## Acceptance criteria

- [ ] `b-pagination` accepts `size="sm"` and `size="lg"` (chip category)
- [ ] `b-dropdown-menu` accepts `size="sm"` (text-scale category)
- [ ] `b-breadcrumb` sized only if a concrete consumer use case appears; document the decision either way
- [ ] Existing default size unchanged on all three
- [ ] CLAUDE.md updated with the new variants

## Out of scope

- Adding `size` to other components without a use case
