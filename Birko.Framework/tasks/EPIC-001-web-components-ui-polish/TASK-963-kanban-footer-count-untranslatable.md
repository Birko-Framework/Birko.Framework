---
id: TASK-963
parent: EPIC-001
feature: null
status: done
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

- [x] The footer text comes from a `bwc.kanban` key with the count interpolated (singular and plural), English
      fallback unchanged; overridable like the other kanban labels.
- [x] locale-keys-check passes; a Playground check shows a translated footer.

## Evidence

Birko.Web `task/TASK-961` 9c62d34 (v0.4.22), Playground 096d8a9: backport-smoke with Slovak-like strings added for
`bwc.kanban.cardCount`: v0.4.21 showed "2 cards | 1 card", after: "2 kariet | 1 karta". locale-keys-check 23/23.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22 in the Playground: "all passed". Merged into
Birko.Web main 2d02508, tagged v0.4.22 (not pushed).

## Human test plan

N/A — fully covered by automated tests (a locale override changes the footer text).

## Implementation plan

1. Keys `bwc.kanban.cardCount` ("{count} card") and `cardCountPlural` ("{count} cards") in en.json; render through
   `this.label(...)`, picked by count.
2. A backport-smoke check with an i18n override.
