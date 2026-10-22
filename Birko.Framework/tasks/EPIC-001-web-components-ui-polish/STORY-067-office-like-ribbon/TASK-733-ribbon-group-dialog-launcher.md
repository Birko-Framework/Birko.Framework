---
id: TASK-733
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
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

- [x] A group can opt in to a launcher; it sits beside the bottom label and emits a group-launcher event with the group id
- [x] Named for screen readers (i18n, per-group label), keyboard-reachable in the arrow-key order
- [x] Hidden or moved into the flyout header when the group is folded
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Click and keyboard-activate a launcher; check its tooltip and the event in the Playground

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Look, picked by the user from three:** option A, Office's corner arrow. The ↘ is an inline SVG drawn in
  `currentColor`, muted and tinted on hover.
- **API.**
  - `RibbonGroup.launcher?: boolean` and `launcherLabel?: string` (default "More {label} options",
    `bwc.ribbon.launcher`).
  - Event `group-launcher` → `{ tabId, groupId }`, never an `item-click`. An unpinned ribbon collapses after it, as
    after a command.
- **Placement.** Absolute at the group's bottom-right, with the label padded so the name stays centred and clear of
  it.
  - A folded group shows it in a flyout header ("Font ↘"); a click there closes the flyout.
  - The mobile menu lists it under the group's commands.
- **Reach.** An arrow-key stop: it sits under the right end of the bottom row, so ↓ from the group's last command
  reaches it. A tooltip target with its name, and a key-tip target. In a flyout it is also reachable with the arrows.
- **Gate.** Playground `keyboard-check.mjs` 147/147, with 9 new checks using real clicks and keys.
  - The checks: only groups that ask get one, named; corner placement clear of the name; a click emits and runs no
    command; tooltip; ↓ reaches it and Enter emits; key tip; flyout header and closing; the mobile item firing once
    after three hover previews.
  - The check crashes at its first launcher step on the TASK-736 ribbon. `verify.mjs` clean; entry-points 409/409;
    `tsc` clean. Checked in Chrome (Font ↘ in the gallery).

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
