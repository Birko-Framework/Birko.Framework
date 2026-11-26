---
id: TASK-993
parent: STORY-038
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-26
depends-on: [TASK-599]
blocks: []
related: [TASK-599]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Should the stronger danger / warning colours apply beyond badges?

## Context

Deferred from [[TASK-599]] by the user (2026-11-26, "lets defer it into separate task but for now B"). A phone check in
daylight found the danger and warning badges readable but subtle, and the user picked option B (a stronger tint and a
deeper text) for **b-badge only**, through its own `--b-badge-{danger,warning}-{bg,text}` tokens. The shared
`--b-color-{danger,warning}-light` / `-text` pair stayed as it was, and other components use it:

- `danger-light`: b-data-table, b-dropdown-menu (delete items), b-json-diff (removed lines), b-stat, b-sync-status,
  b-tree-menu
- `danger-text`: b-json-diff, b-kanban, b-stat, b-sync-status, b-tree-menu, shared-styles
- `warning-light`: b-markdown-editor, b-stale-banner, b-sync-status
- `warning-text`: b-json-diff

So a danger stat, a stale-data banner or a removed diff line can now look calmer than a danger badge next to it.

## Decide

- Move the shared pair to B's values everywhere (badges then fall back to it and the badge tokens can go), or
- keep the shared pair calm for large areas (banners, diff lines, row highlights) and strengthen only the small
  "status" elements (stat, sync status, kanban markers), or
- leave it: badges are the alarm, the rest is context.
- Also weigh D's extra cue from the TASK-599 options page: a border in the full hue and a shape (▲ / ●), which survives
  sunlight and colour blindness; and the Avalonia badge, which has no danger / warning variant at all yet.

Show the affected components before and after, in light and dark, before changing anything (a visible change).

## Acceptance criteria

- [ ] The user's decision recorded here, with why the others lost.
- [ ] Built as decided in tokens.json (generated, never hand-edited CSS), with the contrast gate green.
- [ ] A Playground check that pins the decided colours on each affected component, red before.

## Out of scope

- The badge itself: decided in TASK-599.

## Human test plan

- [ ] On a phone in daylight, both themes: a stale banner, a danger stat and a JSON diff with removed lines next to a danger
      badge; the result reads as intended by the decision.
