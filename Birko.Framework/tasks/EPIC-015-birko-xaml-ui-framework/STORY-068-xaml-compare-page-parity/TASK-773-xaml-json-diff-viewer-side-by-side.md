---
id: TASK-773
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
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

FEATURE-020 D7 (changed 2026-10-26) + D12. The second view of `b-json-diff`, as the web draws it: two panes, labelled
side A and side B, each showing its whole body line by line — JSON pretty-printed (keys in the engine's order), XML as
the original document via `XmlTree.ToLines` (TASK-771) — with added / removed / changed / ignored lines tinted. A line
inside a marked element takes its nearest marked ancestor's tint. The sides are **not** lined up. "Only changes" and
expand / collapse all stay tree-view switches, as on the web. **One desktop addition (D7):** the panes scroll together.
Adds `View` (Tree | SideBySide) to TASK-772's control, with Tree / Side by side toggles in its toolbar.

Open concern carried from FEATURE-020's fog: large responses. Measure a realistic tester response (hundreds of KB) and
record it; the lines are virtualized so only the visible ones are built.

## Acceptance criteria

- [x] `View` property and toolbar toggles switch between the tree and side-by-side; the tree-only actions hide in
      side-by-side.
- [x] Each pane shows its label and its whole body; lines are tinted by the diff (removed on A, added on B, changed on
      both, ignored dimmed), a line inside a marked element taking that element's tint — as the web's
      `_prettyLines` / `_xmlLines`.
- [x] XML mode shows the original XML text with the tints mapped from the tree diff.
- [x] A side that does not parse shows its error and its raw text in its pane.
- [x] Scrolling one pane scrolls the other (vertical and horizontal).
- [x] Headless tests: marks on both sides for JSON and XML, a moved keyed item marked at its own side-A position,
      ignored lines, parse error pane, view switch, scroll sync.
- [x] Measured render time for a large sample recorded in the Review log.
- [x] Gallery page gains the view switch; docs and Recent Updates updated.

## Out of scope

- Lining up the two sides (not what the web does; D7).

## Human test plan

1. Gallery diff viewer page, Side by side, JSON sample: on the left, `"gone": true` is tinted red and the `"v": 1`
   inside the item whose Id is 1 amber; on the right, `"extra": 1` green and `"v": 5` amber; `Url` dimmed on both.
2. Scroll one pane: the other follows.
3. XML sample: the original XML (with its attributes) is shown; `<Count>` lines amber, `<New>` green, the removed
   `<CzNace>` (Code 30) red on the left.
4. Break one side's JSON: that pane shows the error and the raw text; the other pane still shows its body.
5. Switch back to Tree: "only changes", Expand and Collapse are back.
6. Repeat 1 under each theme.

## Implementation plan

## Review log

- 2026-10-26 — Started by correcting the task: its first draft asked for lined-up rows with blank partners, synced
  "only changes" and collapsing, which the web view does not do. Put to the user; D7 changed to strict web parity
  **plus** panes that scroll together (decided by the user). Context, criteria and Human test plan rewritten.
- Built; **verify** (Human test plan needs a person at the Gallery). `View` (Tree | SideBySide) with Tree / Side by side
  toolbar toggles (the current view stays pressed); tree-only actions hide. Two panes of `DiffLine`s: JSON via public
  `JsonLines` (key order from `JsonDiff.OwnKeys`, made public), XML from `XmlTree.ToLines`; marks as the web's
  `_renderSplit`, a line inheriting its nearest marked container's mark. A side that does not parse shows its error and
  raw text in its own pane. Panes virtualized; `SyncScroll` keeps their offsets equal.
- **Measured** (the fog item): a 373 KB body a side, 3 000 records, 31 changes — side by side 454–649 ms headless. The
  same body as a **tree took 4.1 s**: TASK-772 built every row of an open container up front. Fixed here: the tree now
  flattens its visible rows into a virtualized list — 166 ms, and a test asserts at most 200 rows are built. TASK-772's
  log records the change.
- Tests: `JsonDiffViewerSplitTests` 9 + 3 screenshots. **Proved they can fail:** no scroll sync, no marks, and a
  non-virtualized tree failed 5 of 9. `Birko.Xaml.Avalonia.Tests` 354/354. Screenshots looked at (Light, Dark, XML):
  tints on the right lines on each side, the key-paired item's `"v": 1` marked at its side-A position.
- Gallery: no change needed — the demo's viewer carries its own Tree / Side by side toggles.
