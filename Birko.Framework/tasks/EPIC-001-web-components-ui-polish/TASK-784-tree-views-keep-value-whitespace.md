---
id: TASK-784
parent: EPIC-001
status: verify
priority: P2
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Tree views keep a value's spaces (b-object-tree, b-xml-viewer, b-json-diff)

## Context

Asked by the user (2026-10-29): "in web framework the code blocks etc truncate spaces into one (common html
behaviour) when a pre tag is used the spaces are preserved can it be done in the code/diff related components?"

Measured before the fix:
- `b-code-block`, `b-pre` and `b-json-diff`'s side-by-side view already render in `<pre>` / `white-space: pre`, so they
  keep spaces.
- The tree views render values in plain spans, so the browser folds `"a     b"` into `"a b"`: `b-object-tree` string
  values (so `b-json-viewer` too), `b-xml-viewer` text / attribute values / CDATA / comments / PIs, and `b-json-diff`'s
  tree values (`ins`, `del`, the equal and ignored value).

Birko.Xaml was checked as well and has no such defect: an Avalonia `TextBlock` does not fold whitespace, so
`XmlViewer` and `JsonDiffViewer` show inner spaces already. Both platforms trim the ends of XML text on purpose
(`xml-tree.ts`, `XmlTree.cs`, the shared diff vectors); that stays.

## Acceptance criteria

- [x] `white-space: pre-wrap` on the value spans of the three components (keeps every space, a long value still
      wraps). The equal and ignored value spans in `b-json-diff` get a `val` class to carry it
- [x] `b-xml-viewer` trims the ends of an element's only text, as it already did for mixed content, so pretty-printed
      XML keeps one row under `pre-wrap`
- [x] Playground `whitespace-smoke` (9 checks, by rendered width): green with the fix, 8/9 red without it
- [x] CHANGELOG v0.1.21, package versions 0.1.21

## Human test plan

1. Playground → JSON viewer: set data `{"v": "a     b"}`. The value shows five spaces between `a` and `b`.
2. Playground → XML viewer: load `<r k="a     b"><x>a     b</x></r>`. Both the attribute value and the text show five
   spaces. Load a pretty-printed document: each element with only text still sits on one row.
3. Playground → JSON diff, tree view: compare `{"c": "x"}` with `{"c": "a     b"}`. The new value shows five spaces.
   A long string value still wraps inside the panel instead of running out of it.
4. Same JSON diff, side-by-side view: unchanged from before.

## Outcome (2026-10-29)

- Birko.Web `c51276b`, tag `v0.1.21` (not pushed yet): CSS in `b-object-tree.ts`, `b-xml-viewer.ts`, `b-json-diff.ts`;
  the inline-text trim in `b-xml-viewer.ts`.
- Playground `7f313bf`: `src/whitespace-smoke.ts`, wired in `app.ts` and `verify.mjs`.
- Gate: whitespace-smoke 9/9 (1/9 without the component changes), verify.mjs 0 failing (json-diff-smoke 76/76,
  xml-diff-smoke 40/40), entry-points 409/409. `tsc -p .` in the Playground reports one error in
  `sync-interrupt-smoke.ts`, a file this task does not touch.
- Stays at `verify` until the human test plan is run.
