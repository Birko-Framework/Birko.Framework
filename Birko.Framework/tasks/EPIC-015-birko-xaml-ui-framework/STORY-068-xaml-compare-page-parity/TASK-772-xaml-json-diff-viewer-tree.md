---
id: TASK-772
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-10-26
depends-on: [TASK-770, TASK-771]
blocks: [TASK-773]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `JsonDiffViewer` control (Birko.Xaml.Avalonia) — tree view

## Context

FEATURE-020 D6 + D12. Port the tree view of `b-json-diff` (`Birko.Web.Components/src/data/b-json-diff.ts`, 584 lines):
the engine's merged tree rendered as an expandable tree, each node coloured by status (added / removed / changed /
modified / ignored / equal) with old → new values, and the summary counts. The control is built so TASK-773 adds the
side-by-side view as a second mode of the same control, not a second control.

## Acceptance criteria

- [x] Properties: `RawA`, `RawB` (strings), `Format` (Json | Xml), `LabelA`, `LabelB`, `OnlyChanges`, `Options`
      (`JsonDiffOptions`), `Result` (read-only `JsonDiffResult`), and a `SetData(rawA, rawB)` method; all bindable
      where the web one is an attribute. Unparseable input shows an error state, as on the web.
- [x] XML mode diffs the converted trees (TASK-771).
- [x] "Only changes" hides `equal` (and `ignored`) branches; containers with changes stay expanded.
- [x] Status colours come from the theme tokens, readable under Light, Dark, Neon and Finstat.
- [x] Headless tests in `tests/Birko.Xaml.Avalonia.Tests`: status per node, only-changes filtering, format switch,
      parse error state, re-diff on `Options` change.
- [x] Gallery page in `Birko\Consumers\Birko.Xaml.Gallery` with JSON and XML samples.
- [x] Recent Updates entry; `Birko.Xaml.Avalonia/README.md` + `CLAUDE.md` list the control.

## Out of scope

- The side-by-side view (TASK-773).

## Human test plan

1. Open the Gallery's diff viewer page. Load the JSON sample: added, removed and changed values show in their colours,
   modified containers are marked, and the counts match what you can see.
2. Toggle "only changes": unchanged branches disappear; toggle back: they return.
3. Switch to the XML sample: a repeated element shows as a list; a nil element shows as empty.
4. Paste invalid JSON into one side: an error message shows instead of a crash.
5. Repeat 1 under each theme (Light, Dark, Neon, Finstat): every status colour stays readable.

## Implementation plan

## Review log

- 2026-10-26 — Built; **verify**: code and headless tests done, the Human test plan above needs a person at the Gallery.
  `Birko.Xaml.Avalonia/Controls/JsonDiffViewer.cs` (+ `JsonDiffFormat`): `RawA`/`RawB`/`SetData`, `Format`,
  `LabelA`/`LabelB`/`Title`, `OnlyChanges`, `Options`, `XmlOptions`, read-only `Result`, `DiffComputed`,
  `ExpandAll`/`CollapseAll`, public `Inline` (the web's value rendering, non-ASCII kept, cut at 120). Strings under
  `bxaml.jsonDiff.*` with the web's English. A code-built row tree rather than a `TreeView`, expansion keyed like the
  web's `nodeKey`. New `Controls/DataViewers.axaml`: `BToolbarButton`, `BToolbarToggle`, `BTreeNodeToggle` — Birko had
  only the primary `Button` theme, which made the toolbar three large blue buttons and an open chevron a filled
  checked box (seen on the first screenshot, fixed before close).
- Tests: `JsonDiffViewerTests` 15 + 4 screenshots (one per theme, `BIRKO_SHOTS`). **Proved they can fail:** no seeded
  expansion and no only-changes filter failed 3. `Birko.Xaml.Avalonia.Tests` 341 + new, all green.
- Looked at: headless screenshots in Light, Dark, Neon and Finstat — every status colour readable, rows aligned after
  the fix. Headless text is proportional (the mono fonts are not installed there); a test asserts the rows carry
  `BFontMono`. The real Gallery app starts with the new page (launched, alive after 12 s, closed) — **not** looked at
  by a person yet.
- Gallery (Birko\Consumers\Birko.Xaml.Gallery): Viewers tab, "JSON / XML diff": two editable sides, JSON / XML
  samples, a format picker; Url ignored and Items paired by Id, as the web demo.
- 2026-10-26 — Changed while in verify, by TASK-773's measurement: an open container of 3 000 records took 4.1 s to
  show because every row was built up front. The tree now flattens its visible rows into a virtualized `ItemsControl`
  (166 ms for the same body); rows, classes, toggles and the Human test plan are unchanged. This task's 15 tests pass
  unchanged.
