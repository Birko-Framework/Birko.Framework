---
id: TASK-733
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-730]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: group dialog launcher

## Context

FEATURE-019 D4 (approved by the user 2026-10-22): A group can show a small corner button (↘) next to its name that opens "more options" for that group.

No such affordance exists; apps put a "More…" command into the group instead.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] A group can opt in to a launcher; it sits beside the bottom label and emits a group-launcher event with the group id
- [ ] Named for screen readers (i18n, per-group label), keyboard-reachable in the arrow-key order
- [ ] Hidden or moved into the flyout header when the group is folded
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Click and keyboard-activate a launcher; check its tooltip and the event in the Playground
