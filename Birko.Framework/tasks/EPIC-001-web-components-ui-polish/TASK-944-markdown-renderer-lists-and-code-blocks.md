---
id: TASK-944
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
related: [TASK-943]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `renderMarkdown` lists and code blocks render as written

## Context

Reported by DraCode with [[TASK-943]], 2026-11-21. Three rendering bugs in `BMarkdownEditor.renderMarkdown`; DraCode
worked around (b) and (c) with a `tidy()` step and not (a).

- (a) A list item continued on an indented next line ended the list: the continuation became its own paragraph.
- (b) A list directly after a paragraph line ("Components:\n- a\n- b") formed one block that did not start with `<ul`,
  so the paragraph step turned its newlines into `<br>` between the `<li>`s.
- (c) A fence with no language kept its opening newline (`firstNewline` is 0 and the check was `> 0`), so the block
  started with a blank line.

## Change (Birko.Web v0.4.20, with TASK-943)

- (a) After escaping, an indented line right after a list item that is not itself an item is joined to that item.
  A nested item (indented marker) is left alone.
- (b) A `<ul>` / `<ol>` is split into its own block before paragraphs are made.
- (c) `firstNewline >= 0`.
- CHANGELOG.

## Evidence

backport-smoke: the 4 TASK-944 checks fail on Birko.Web main and pass with the change; 397/397 in 10 of 11 verify runs.
One run reported 1 failing check outside backport-smoke that was not captured; the next 8 runs reported 0. Recorded,
not attributed (rule 54). keyboard-check 225/225, rerender-leak PASS, a11y-name OK (43), entry-points 433/433,
locale-switch 47/47, Components `tsc` clean.

## Acceptance criteria

- [x] An indented continuation stays in its list item, ordered and unordered.
- [x] A list right after a paragraph line has no `<br>` between items, and the paragraph line is kept.
- [x] A fence with no language starts on its first line.

## Human test plan

1. In a `b-markdown-editor` preview, type `1. one\n   still one\n2. two`: one numbered list of two items.
2. Type `Components:\n- a\n- b`: the line, then a tight list with no gaps between items.
3. Type a fence with no language around two lines: the code block starts on the first line.
