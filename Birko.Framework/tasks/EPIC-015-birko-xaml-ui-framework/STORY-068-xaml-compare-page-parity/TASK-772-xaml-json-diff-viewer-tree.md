---
id: TASK-772
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Properties: `RawA`, `RawB` (strings), `Format` (Json | Xml), `LabelA`, `LabelB`, `OnlyChanges`, `Options`
      (`JsonDiffOptions`), `Result` (read-only `JsonDiffResult`), and a `SetData(rawA, rawB)` method; all bindable
      where the web one is an attribute. Unparseable input shows an error state, as on the web.
- [ ] XML mode diffs the converted trees (TASK-771).
- [ ] "Only changes" hides `equal` (and `ignored`) branches; containers with changes stay expanded.
- [ ] Status colours come from the theme tokens, readable under Light, Dark, Neon and Finstat.
- [ ] Headless tests in `tests/Birko.Xaml.Avalonia.Tests`: status per node, only-changes filtering, format switch,
      parse error state, re-diff on `Options` change.
- [ ] Gallery page in `Birko\Consumers\Birko.Xaml.Gallery` with JSON and XML samples.
- [ ] Recent Updates entry; `Birko.Xaml.Avalonia/README.md` + `CLAUDE.md` list the control.

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
