---
id: TASK-773
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-26
depends-on: [TASK-772]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `JsonDiffViewer` — side-by-side view

## Context

FEATURE-020 D7 + D12. The second view of `b-json-diff`: side A and side B as aligned lines, with changed lines
highlighted. In XML mode the **original XML** is shown (via `XmlTree.ToLines`, TASK-771) while the diff runs on the
converted trees. Adds a `View` (Tree | SideBySide) property to TASK-772's control.

Open concern carried from FEATURE-020's fog: large responses. Measure a realistic tester response (hundreds of KB);
if a plain panel is too slow, virtualise — record the measurement either way.

## Acceptance criteria

- [ ] `View` property switches between the tree and side-by-side; `OnlyChanges`, labels and format apply to both.
- [ ] Lines on both sides stay aligned: an added line has a blank partner on side A and vice versa; both sides scroll together.
- [ ] XML mode shows the original XML text with highlights mapped from the tree diff.
- [ ] Headless tests: alignment of added/removed/changed lines, only-changes collapsing, XML line mapping.
- [ ] Measured render time for a large sample recorded in the Review log.
- [ ] Gallery page gains the view switch; docs and Recent Updates updated.

## Out of scope

- Inline (single-column) diff.

## Human test plan

1. Gallery diff viewer page, side-by-side view, JSON sample: each changed value is highlighted on both sides on the
   same row; added lines have an empty partner on the other side.
2. Scroll one side: the other follows.
3. Toggle "only changes": unchanged runs collapse.
4. XML sample: the original XML (with its attributes and formatting) is what's shown, highlights on the right lines.
5. Load the large sample: the view opens and scrolls without visible stalls.
6. Repeat 1 under each theme.

## Implementation plan

## Review log
