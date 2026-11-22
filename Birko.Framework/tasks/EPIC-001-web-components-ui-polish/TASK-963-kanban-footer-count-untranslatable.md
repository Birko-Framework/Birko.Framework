---
id: TASK-963
parent: EPIC-001
feature: null
status: in-progress
priority: P3
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: the column footer "N cards" cannot be translated

## Context

Found 2026-11-22 while checking DraCode's request (FEATURE-023). The column footer is the hard-coded English
`${count} card(s)`: no `bwc.kanban.*` key, so a translated board still says "3 cards".

## Acceptance criteria

- [ ] The footer text comes from a `bwc.kanban` key with the count interpolated (singular and plural), English
      fallback unchanged; overridable like the other kanban labels.
- [ ] locale-keys-check passes; a Playground check shows a translated footer.

## Human test plan

N/A — fully covered by automated tests (a locale override changes the footer text).

## Implementation plan

1. Keys `bwc.kanban.cardCount` ("{count} card") and `cardCountPlural` ("{count} cards") in en.json; render through
   `this.label(...)`, picked by count.
2. A backport-smoke check with an i18n override.
