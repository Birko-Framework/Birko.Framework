---
id: TASK-003
feature: FEATURE-001
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

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] Render `b-pagination` at `size="sm"`, default, and `size="lg"` side by side and confirm the chip proportions scale sensibly (no clipped numbers or misaligned arrows)
- [ ] Render `b-dropdown-menu` at `size="sm"` and confirm the trigger + menu items follow the text-scale category (font/padding shrink together, menu still readable)
- [ ] Confirm the **default** (no `size`) rendering of all three is pixel-unchanged from before the change (compare against current build)
- [ ] If `b-breadcrumb` sizing was implemented, verify the variant against its driving consumer use case; if it was skipped, confirm the deferral note is present in CLAUDE.md
- [ ] Verify in Chromium + Firefox
