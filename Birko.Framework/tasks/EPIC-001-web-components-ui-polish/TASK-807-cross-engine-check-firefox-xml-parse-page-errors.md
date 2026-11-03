---
id: TASK-807
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-03
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# cross-engine-check fails in Firefox on 8 "XML Parsing Error" page errors

## Context

Playground `cross-engine-check.mjs` (bare-smoke in Chrome and Firefox) reports `chrome 113/113`, `firefox 113/113`,
then `FAIL - firefox: 8 page error(s)`, every one an `XML Parsing Error` at `/?smoke=1`: "no root element found"
(line 1, col 29) ×3, "syntax error" ×2, "mismatched tag. Expected: </b>." ×2, "not well-formed" ×1. Measured
2026-11-03 on Playground main and Birko.Web main (v0.4.1), with and without the Playground's new `launch-browser.mjs`
(Firefox is launched as before), so it is not that change. Found while fixing the flaky Playground checks.

The texts look like deliberately malformed XML that a page parses with `DOMParser` (`application/xml`): Firefox
reports a parse error on the console, and puppeteer's Firefox path surfaces it as a `pageerror`; Chrome does not. The
two parsers in Birko.Web are `Birko.Web.Core/src/diff/xml-tree.ts:60` (TASK-771's `XmlTree`, which treats a parse error
as a result) and `Birko.Web.Components/src/data/b-xml-viewer.ts:204`. Which suite or card feeds them under `?smoke=1`,
and when it started (TASK-771 landed 2026-10-26; the check was last green when?), is not measured.

The check excuses an engine difference only by exact message, filed (`KNOWN_ENGINE_DIFFERENCES`, TASK-466), never
by "ignore page errors in Firefox".

## Acceptance criteria

- [ ] The source of each of the 8 errors named (suite or card, input, which parser).
- [ ] Decided, with the reason recorded: a framework defect (the component should not surface a parse error as a page
      error in Firefox) is fixed in Birko.Web with a check that fails without it; or a reporting difference of
      Firefox for an input that is *meant* to be malformed is excused by exact message in `KNOWN_ENGINE_DIFFERENCES`
      with this task named.
- [ ] `node cross-engine-check.mjs` passes on both engines.

## Out of scope

- TASK-466 (the invalid form control `steps` not focusable), already excused.

## Human test plan

N/A — fully covered by automated tests (cross-engine-check is the test).

## Implementation plan

_Populated by `/tasks plan TASK-807` — leave empty until then._
