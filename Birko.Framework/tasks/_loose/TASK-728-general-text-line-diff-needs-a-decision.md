---
id: TASK-728
parent: null
feature: null
status: todo  # needs a decision before any work — see "Decision needed"
priority: P3
assignee: null
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A general text / line diff (`diffText` + `b-text-diff`) — decide before building

## Context

Filed at the ClientApi.CSharp session's request, after TASK-725 (JSON diff) and TASK-727 (XML mode). Origin: the
ClientApi.CSharp web tester's environment comparison (Outline doc e9938a9b-92c0-4cf4-9f43-766d72136161, section
"Porovnanie prostredí").

**What.** `diffText(a, b, options?)` in Birko.Web.Core: line-based (Myers / LCS), with options to ignore whitespace,
ignore case, and set context lines. Plus a `b-text-diff` component in Birko.Web.Components: unified and side-by-side
views, line numbers, the same look and i18n conventions as `b-json-diff`.

**Why it may be needed.** The tester compares responses across environments. A body that is neither JSON nor XML can
only be compared as one text value today. Candidate uses: HTML error pages, CSV exports, the SK daily diff files, curl
output, and other Birko apps comparing plain-text documents or configs.

**Why not now.** No current consumer needs it. The tester's compare page deliberately does not diff failed calls, and
JSON / XML cover the real responses.

## Decision needed

- [ ] Has a real consumer appeared? Name it and the bodies it compares
- [ ] Should `b-json-diff`'s split view and `b-text-diff` share the line-rendering code? (`b-json-diff` already renders
      marked lines for JSON and, via `xmlToLines`, for XML)
- [ ] A library or Birko's own implementation? Birko.Web.Core has **no dependencies by design**, so a diff library is a
      dependency-rule decision, not a convenience

## Acceptance criteria

To be written once the decision is recorded.

## Human test plan

To be written once the decision is recorded.
