---
id: TASK-968
parent: EPIC-001
feature: FEATURE-023
status: in-progress
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: a board can hide the column footer

## Context

FEATURE-023 D1 (DraCode): the "N cards" footer under each column repeats the header count. A board setting hides it; boards that do not set it keep it, so Symbio's boards do not change. Built on `task/TASK-968` for Birko.Web v0.5.2.

## Acceptance criteria

- [ ] `showColumnFooter: false` in the board configuration hides every column's footer, in columns and stacked layouts.
- [ ] Without it the footer shows as before.
- [ ] Playground check.

## Human test plan

1. In the Playground gallery's kanban, switch "column footer" off: the "N cards" lines under the columns go. Switch it on: they come back.
