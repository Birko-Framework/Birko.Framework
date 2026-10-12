---
id: TASK-572
parent: EPIC-015
feature: FEATURE-015
status: done
priority: P3
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Let an app fold its title bar into the ribbon's tab row

## Context

FinStat's desktop tester stacked a header row (app name, Commands, Settings, About) above the `Ribbon`, a
full row of height for a strip that was mostly empty, while the ribbon's own tab row had free space between
the tabs and the collapse/pin buttons. Office puts exactly these there. The `Ribbon` had no slot for it: the
tab strip was a fixed tabs / chevron / pin grid, rebuilt from scratch on every tab or size change.

## Approach

`LeadingContent` (before the first tab) and `TrailingContent` (between the tabs and the chevron) as
`object?` styled properties. The chrome is rebuilt on every tab/size change, but a control can have only one
parent, so the content lives in two `ContentControl` hosts that persist across rebuilds and are re-parented
into each new strip; a content change sets the host's content and does not rebuild. The narrow ☰ chrome
shows both slots too, so app-level commands never disappear.

## Acceptance criteria

- [x] Both slots render in the wide tab strip, in order: leading, tabs, trailing, chevron, pin
- [x] Slot content survives tab switches and collapse/expand (re-parented, never re-added to a second parent)
- [x] Changing slot content updates in place without a rebuild
- [x] Trailing content is shown on the narrow ☰ chrome
- [x] Unset slots change nothing for existing consumers

## Human test plan

Desktop tester: the app name sits left of the tabs and Commands/Settings/About right of them, with no header
row above; switching tabs, collapsing and re-expanding keep them; a narrow window still shows them next to ☰.

## Review log

- 2026-10-12 — `Leading_and_trailing_content_sit_in_the_tab_strip_and_survive_rebuilds` and
  `Trailing_content_stays_on_the_narrow_chrome` added. Full `Birko.Xaml.Avalonia.Tests` green.
