---
id: TASK-989
parent: EPIC-001
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-545, TASK-978]
findings: []
pr: null
github-issue: null
jira-key: null
---

# After a keyboard Undo, focus drops to the page body

## Context

Seen twice at TASK-545's keyboard test: on the Playground's undo-mode page (2026-11-24) and on Symbio HR → Positions
(2026-11-25, Symbio's run). Deleting a row with the keyboard moves focus to the toast's Undo, as designed; pressing
Enter restores the row, but the toast holding focus is replaced by the "restored" toast, so focus falls to `<body>`
and a keyboard user loses their place. TASK-545 did not say where focus should go.

## Acceptance criteria

- [x] After Undo, focus goes to the restored row in the table (via TASK-978's roving tab stop) when the page shows one,
      otherwise back to where the delete was started; never to `<body>`.
- [x] Decide and document what a page without a table gets.
- [x] Playground check (the `?kb=1` undo-mode page): after Enter on Undo, the focused element is the restored row; red
      before the change.

## Evidence

Birko.Web `task/TASK-989` 6800e5a (starts v0.5.5), Playground b4047f3. All from builds that printed
"✓ Built". `base-crud-page` reads at the Undo whether it was pressed from the keyboard (`:focus-visible`, before the
toast that holds focus is replaced) and, after `_afterRestore` reloads, calls `_focusRestored(id)`: the restored row
(`b-data-table.focusRow` → `b-table.focusRow`), else the table's tab-stop row (`focusTabStop`) when the restored row is
not rendered (another page, filtered out), else — rows not focusable (no `clickable-rows`) or no table — the page
itself (`tabindex="-1"`, no outline), so Tab carries on from the top of the page. A pointer Undo leaves focus alone.
**Decided for a page without a table** (criterion 2): the page itself. Documented in the Shell README (undo mode) and
the Components README (`focusRow`).
- `keyboard-check` 249/249: on the undo page whose rows are not focusable, focus after Undo is the page, not `<body>`;
  on a clickable-rows variant, Tab and Down reach Bravo, the keyboard delete moves focus to Undo, Enter on Undo puts
  focus on the restored Bravo row, and Up then moves to Alpha. **Red** 3 of the 5 new on Birko.Web main (focus on a
  stray button / `<body>`).
- verify 0 failing; table-rows-keyboard-check 17/17; toast-announce-check 7/7; Components and Shell `tsc` clean.

## Out of scope

- Focus after a delete that is not undone (already goes to Undo).

## Human test plan

- [x] On Symbio HR → Positions, keyboard only: delete a position, Enter on Undo; focus is on the restored row and the
      arrow keys keep working from there.

**Merged 2026-11-25** on the user's word ("also merge 989") into Birko.Web main 9a87f73, tagged v0.5.5. On main:
Components and Shell `tsc` clean, keyboard-check 249/249, table-rows-keyboard-check 17/17, verify 0 failing. Stays at
verify until the Symbio step above is run (it needs Symbio rebuilt from v0.5.5).
  - _Run 2026-11-25 by the Symbio session_ (UI rebuilt from Birko.Web 9a87f73 / v0.5.5, tenant acme, 4 positions),
    keyboard only, focus read from the deep active element after each key: Tab to a row, ArrowDown, Enter opens the
    detail, Tab ×5 to Delete, Enter deletes with focus on the toast's Undo, Enter on Undo puts focus on the restored row
    "Keyboard test B" (outlined); ArrowDown then reaches A and ArrowUp ×2 reaches C. Never `<body>`. PASS. Noted, not a
    failure: the detail panel's buttons come after the table's pager in Tab order (5 Tabs from a row to Delete).

**Closed 2026-11-25.**
