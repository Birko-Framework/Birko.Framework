---
id: TASK-552
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-022]
pr: null
github-issue: null
jira-key: null
---

# b-editable-table shows stale typed values after setData or a row removal

## Context

Found by Symbio TASK-900 (Gallery block editor, `presentation-gallery-editor.spec.ts`), 2026-10-10.

`BEditableTable.setData()` (and `_removeRow()`) re-render through `update()`, and `BaseComponent.update()` uses
the DOM morph, which **keeps existing `<input>` nodes** so that child state survives. An `<input>` the user has
typed in has a dirty `.value`, and a changed `value` **attribute** does not move it. So after the rows change, the
table's `_data` is right and the screen is wrong.

Measured in Symbio: two typed rows, swapped through `getData()` → swap → `setData()`. Row 0's input still showed
`/media/tulips.png` while its `value` attribute and `_data[0].url` were `/media/roses.png`. The next keystroke in
that cell writes into the row the user is not looking at.

The same mechanism should apply to the built-in remove button: removing a typed middle row shifts `_data`, and the
inputs that remain keep the old typed values. Not yet reproduced; verify it as step one.

Symbio works around it in `src/Frontend/Symbio.UI/src/modules/presentation/pages/gallery-images.ts`
(`syncCellValues`, which sets each input's `.value` from the data after `setData`). The workaround should be removed
once this lands. `pricing/promotions/promotions-page.ts` also uses the table and has no workaround.

## Acceptance criteria

- [x] After `setData(rows)`, every cell control shows `rows`, including cells the user had typed in.
- [x] After removing a row, the remaining rows show their own values (reproduce first: type in rows 0-2,
      remove row 1, and check that row 1 shows the old row 2).
- [x] A playground or unit-level check fails without the fix (typed value → `setData` → displayed value).
- [ ] Symbio's `syncCellValues` workaround is removed in a Symbio follow-up once the framework version carrying
      this is consumed.

## Out of scope

- The morph's general behaviour in `BaseComponent`. Other components rely on it keeping state. The fix belongs in
  how this component re-renders after a **data** change (for example `forceRender()` or assigning `.value` after the
  morph), not in the morph itself.

## Human test plan

N/A — fully covered by automated tests (the defect is a value read off a DOM property).

## Progress

**2026-10-14 — released in Birko.Web v0.1.9** with [[TASK-580]] (Web `76ff38f`, Playground `30d0701`). In `verify`:
the last criterion waits on Symbio removing `syncCellValues` once it consumes v0.1.9.

- **Removal reproduced** (criterion 2's step one): type rows 0–2, remove row 1, and row 1 still showed `second`.
  Selects and checkboxes are affected the same way as text inputs: a changed `selected` / `checked` attribute does
  not move a control the user has touched.
- **Fix:** `BEditableTable._syncControls()` writes `_data` into each control's live `.value` / `.checked` after
  `setData()`, `setConfig()` and a row removal. The morph still keeps the nodes, so the focused remove button stays
  focused (`forceRender()` would have dropped keyboard focus to the body). It is deliberately not run after
  `validate()` / `clearErrors()`: the rows did not move, and rewriting a typed `1.50` as `1.5` would edit the input.
- **Gate:** `editable-table-sync-smoke` in Birko.Web.Playground (10 checks: setData swap, a keystroke after setData,
  fresh setData clearing a value, row removal; text, number, select and checkbox cells). **Proven red: 2/10 without
  the fix**, including the keystroke check (`tulips.pngX` written over the roses row). 10/10 with it. Full
  `verify.mjs` green; one `ribbon-scaling-smoke` flyout flake (3 checks) passed 44/44 on two reruns and with this
  suite removed.

