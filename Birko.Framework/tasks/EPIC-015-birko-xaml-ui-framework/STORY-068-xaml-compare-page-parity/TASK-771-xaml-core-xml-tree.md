---
id: TASK-771
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `XmlTree.ToTree(string xml, XmlTreeOptions? options = null) → JsonNode?` and
      `XmlTree.ToLines(string xml, XmlTreeOptions? options = null) → IReadOnlyList<XmlLine>?`, mirroring the web types.
- [ ] Every XML case in TASK-769's vectors passes, through the same vector reader as TASK-770.
- [ ] Malformed-input and empty-input tests.
- [ ] Docs as TASK-770 (shared Recent Updates entry if both close together).

## Out of scope

- The viewer.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

## Review log
