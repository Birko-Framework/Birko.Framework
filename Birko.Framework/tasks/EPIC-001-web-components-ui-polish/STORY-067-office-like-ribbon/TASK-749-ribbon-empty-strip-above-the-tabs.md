---
id: TASK-749
parent: STORY-067
feature: FEATURE-019
status: done
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: an empty strip above the tabs

## Context

Reported by the ClientApi.CSharp session (the FinStat web tester), a regression from TASK-738. The shared
`<b-tooltip class="ribbon-tooltip">` is the first child of the ribbon's shadow root and sat in normal flow: b-tooltip's
host is `inline-flex`, so its empty line box (~21px) stood above `.ribbon`. Every ribbon had an empty strip at the top.
Measured in the tester: host top 0, `.ribbon` top 21. Reproduced in the Playground: host 0, tab row 21.

## Acceptance criteria

- [x] The tab row starts at the ribbon's top
- [x] The tooltip still shows under its command
- [x] A regression check

## Human test plan

N/A — fully covered by automated tests (the gap is measured at three widths; tooltip placement is in keyboard-check).

## Outcome (2026-10-22)

- `.ribbon-tooltip` is taken out of flow in b-ribbon's styles (absolute, zero size, overflow visible). It shows as a
  top-layer popover placed from the anchor's rectangle (`showFor`), so its own box was never used.
- **Gate.** Playground `ribbon-layout-smoke` 26/26: the tab row starts at the ribbon's top at 1100, 520 and 260px
  (21.0px on the code before). `verify.mjs` clean; keyboard-check 203/203; `ribbon-a11y-check` 22/22. Checked in
  Chrome: no strip, and the Paste tooltip sits under Paste.
- Not released yet (v0.1.15 is out without it).
