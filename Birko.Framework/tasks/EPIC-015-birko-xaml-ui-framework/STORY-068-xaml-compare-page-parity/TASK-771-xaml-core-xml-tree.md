---
id: TASK-771
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-26
depends-on: [TASK-769]
blocks: [TASK-772, TASK-773]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `xmlToTree` / `xmlToLines` in Birko.Xaml.Core

## Context

FEATURE-020 D1 + D2. Port `Birko.Web.Core/src/diff/xml-tree.ts` (186 lines) to `Birko.Xaml.Core.Diff`, on
`System.Xml.Linq`. `xmlToTree` turns XML into the JSON shape `JsonDiff` compares: a repeated element becomes a list,
`xsi:nil="true"` becomes null, attributes and text follow the web mapping exactly. `xmlToLines` gives the original XML's
lines with their paths, which the side-by-side viewer shows in XML mode (TASK-773). Malformed XML returns what the web
one returns (`null` for lines), never throws past the caller.

## Acceptance criteria

- [x] `XmlTree.ToTree(string xml, XmlTreeOptions? options = null) → JsonNode?` and
      `XmlTree.ToLines(string xml, XmlTreeOptions? options = null) → IReadOnlyList<XmlLine>?`, mirroring the web types.
- [x] Every XML case in TASK-769's vectors passes, through the same vector reader as TASK-770.
- [x] Malformed-input and empty-input tests.
- [x] Docs as TASK-770 (shared Recent Updates entry if both close together).

## Out of scope

- The viewer.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

## Review log

- 2026-10-26 — Built. `Birko.Xaml.Core/Diff/XmlTree.cs`: `ToTree`, `TryToTree` (added: `ToTree` returns null for both
  "not XML" and a nil root, as the web does, and the viewer needs to tell them apart), `ToLines`, `XmlTreeOptions`,
  `XmlLine`, `DefaultSimpleItemNames`. On `System.Xml.Linq` with `PreserveWhitespace` (a text-only element keeps its
  whitespace, as `textContent` does). **One deliberate difference from the web:** a DTD is ignored and never resolved
  (`DtdProcessing.Ignore`, no resolver), so no external entity is fetched; a document relying on DTD entities reads as
  not XML. Recorded in the project CLAUDE.md and README.
- Tests: `XmlTreeVectorTests` — 27/27 `xmlTree`, 10/10 `xmlLines` on the first run; **proved they can fail:** a list
  rule needing three items and a one-space indent gave 26/27 and 1/10. `XmlTreeTests` (10): non-XML input never
  throws, `TryToTree` on a nil root, the XXE document refused, an XML diff marking its lines, values stay text.
  `Birko.Xaml.Core.Tests` 88/88. `diff-parity.yml` filter extended to the XML suites (28 pass locally under
  `BIRKO_REQUIRE_LIVE`).
- Recent Updates: folded into the same-day TASK-770 entry rather than a new one (keeps the section under 10 KB).
