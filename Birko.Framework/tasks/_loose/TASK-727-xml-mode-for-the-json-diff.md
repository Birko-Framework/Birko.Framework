---
id: TASK-727
parent: null
feature: null
status: verify  # verified headlessly 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: [TASK-725]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# XML mode for the JSON diff: `xmlToTree` in Birko.Web.Core, `format="xml"` in `b-json-diff`

## Context

Requested by the ClientApi.CSharp session, with its user's approval. The FinStat web tester compares API responses
across environments (TASK-725), and some responses are XML. The tester had a ~50-line `xml-tree.ts` of its own; this
moves it into Birko so the tester can drop it. Nothing FinStat-specific: the .NET conventions (XmlSerializer's array
item names) are options with those values as defaults.

## Acceptance criteria

- [x] `xmlToTree(text, options?)`: the root element's content, `null` when not XML; text-only element → string,
      empty → `''`, `xsi:nil` → null, attributes → `@name`, xmlns / xsi attributes dropped; list detection and
      repeated names as in the tester's port; options `simpleItemNames`, `nilAsNull`, `attributePrefix`
- [x] The tester's FinStat-shaped sample gives the output it verified in Chromium; `<!DOCTYPE html>…` gives null
- [x] `b-json-diff format="xml"`: strings read with `xmlToTree`, diffed as before; the tree view unchanged; the
      side-by-side view shows the ORIGINAL XML pretty-printed with the changed lines tinted; a non-XML string shows that
      side's error and raw text
- [x] Documented limitation: every value is a string, so JSON vs XML of the same data does not compare equal

## Out of scope

- A general text / line diff (TASK-728, needs a decision), anything FinStat-specific

## Human test plan

- [ ] In the FinStat web tester, compare one XML endpoint across two environments in both views, light and dark

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed or tagged (same as TASK-725).

- **`xmlToTree(text, options?)`** and **`xmlToLines(text, options?)`** in `birko-web-core` (`diff/xml-tree.ts`).
  - Both functions read an element through one `shapeOf` (list / object / text / nil), so every path a line shows
    exists in the tree.
  - `xmlToLines` returns `{ text, paths }[]`: one element per line, a text-only element on one line, two spaces per
    level. The XML declaration and comments are kept, and attributes and namespace declarations shown as written.
- **One deviation from the tester's port.** Text beside attributes or child elements is kept under `textKey`
  (default `'#text'`). The port dropped it, so `<Price currency="EUR">10</Price>` lost its `10`.
- **`b-json-diff`**:
  - `format` attribute (`json` default | `xml`); changing it re-reads the bodies.
  - `setOptions` takes `JsonDiffViewOptions` = the diff options plus `xml: XmlTreeOptions`.
  - The split view marks XML lines by path, and a line inside a marked element takes that element's mark. The default
    title follows the format ("XML diff").
  - Fixed on the way: the two panes' heading ids were chosen by comparing labels, so equal labels collided.
- **Gate.** Playground `xml-diff-smoke` 40/40, all other suites green, entry-points 409/409, `tsc` clean on Core and
  Components.
  - Mutations: dropping the kept text fails 3 checks, and marking exact paths only (no inheritance) fails the
    "value replaced by an element" check.
  - Screenshot of the XML side-by-side view checked.
  - Unrelated and not touched: the Playground's `src/sync-interrupt-smoke.ts(171,15)` has a `tsc` error (TS2554)
    that predates this task.
