---
id: TASK-002
feature: FEATURE-001
parent: STORY-002
status: review
priority: P2
assignee: ai
created: 2026-05-28
depends-on: [TASK-001]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Benchmark + migrate b-editable-table to bare components

## Context

`b-editable-table` currently renders raw `<input>` / `<select>` / `<checkbox>` inside its own shadow root to (a) use a single delegated event listener on `<tbody>`, (b) avoid re-render-on-keystroke so caret + selection survive, and (c) render cells at `--b-control-min-height-sm` density without each cell paying for its own Shadow DOM. Migrating to `<b-input bare size="sm">` etc. would unify chrome with the rest of the form family but costs Shadow-DOM-per-cell render + per-cell event listeners. Decision is benchmark-gated.

## Acceptance criteria

- [x] Reproducible 500-row grid benchmark harness exists — `Birko.Web.Playground/src/grid-bench.ts`, run
      headlessly by `verify.mjs` via `grid-bench-smoke.ts` and by hand from the **Grid benchmark** card in
      the gallery's Data section. Parameterised by row count; medians over 3 passes.
- [x] Baseline measured (current raw-input implementation)
- [x] Bare-component variant measured
- [x] Decision documented (migrate / don't migrate / partial) with the numbers — **DON'T MIGRATE the
      unpaged grid**; see below.
- [ ] ~~If migrate: cells use `<b-input bare size="sm">`, event handling redesigned~~ — **not applicable**
      under the decision. Note the benchmark found the caret risk this criterion was written to guard
      against does **not** materialise (see "The caret risk was the wrong worry").

## Decision: keep the raw cells

Measured in Chromium, 6 columns (2 text / number / date / select / checkbox — the table's own mix), median
of 3 passes:

**500 rows (3000 cells) — the unpaged case**

| | raw | bare | ratio |
|---|---|---|---|
| build | 383 ms | 696 ms | 1.8× |
| re-render | 268 ms | 834 ms | 3.1× |
| edit (50 keystrokes) | 27 ms | 44 ms | 1.6× |
| elements | 8 001 | 11 501 | 1.4× |
| shadow roots | 0 | **3 000** | — |

**30 rows (180 cells) — what a paged or virtualised grid actually renders**

| | raw | bare | ratio |
|---|---|---|---|
| build | 25 ms | 57 ms | 2.3× |
| re-render | 33 ms | 54 ms | 1.6× |
| edit (50 keystrokes) | 34 ms | 27 ms | 0.8× |

**Why don't-migrate:** at 500 rows the bare variant re-renders in ~830 ms against ~270 ms — the difference
between a perceptible pause and a freeze, on the operation the table performs most (any data change rebuilds
`tbody`). 3 000 shadow roots is the cost driver, and it is structural, not tunable.

**Why this is not a blanket "bare components are too slow":** at 30 rows both variants are under 60 ms and
edit latency is a wash (bare was marginally *faster* in that pass — at that scale the numbers are inside the
noise). So the constraint is the **unpaged grid**, not the components. If `b-editable-table` ever gains
virtualisation or hard paging, re-run the harness at the real visible-row count and the decision may flip;
the harness takes a row count precisely so that is a re-measure, not a rewrite.

## The caret risk was the wrong worry

This task's context named caret/selection survival as "the core risk of the migration". It does not
materialise: **caret was kept in both variants**, including across the control's own re-render. `b-input`'s
`onUpdated` restores `input.value = this._value`, and assigning an *unchanged* value does not move the
caret — so the per-keystroke re-render that the criterion feared is already harmless by construction.

Worth recording how that was established, because the first measurement said the opposite: the initial
check assigned `.value` *after* placing the caret, and assignment moves the caret to the end on its own — so
it reported `caret=LOST` for **both** variants, including raw, where nothing re-renders at all. That
impossible result is what exposed the bad test. A benchmark that "confirms" the thing you expected is worth
re-reading.

## Verification

- Harness: `grid-bench` in the Playground, logged under `[playground]` so `verify.mjs` captures it; also a
  button-driven gallery card (it allocates 3 000 cells, so it is not built with the gallery).
- Runs both 500-row and 30-row configurations each time; `tsc --noEmit` clean.
- Machine/browser for the numbers above: headless Chromium 1228 via Playwright, Windows 11. Absolute
  figures are machine-dependent — the **ratios** are the durable result.

## Out of scope

- Other table component changes
- Generic Shadow-DOM-per-cell optimization

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] Run the benchmark by hand from the gallery (**Data → Grid benchmark → Run benchmark**) on your own
      machine and browser, and confirm the ratios hold. Absolute ms will differ; if the 500-row re-render
      ratio comes out near 1× rather than ~3×, the decision deserves revisiting.
- [ ] **Caret survival by actual typing** — the harness dispatches synthetic `input` events, which is not
      the same as a real IME/keyboard. Type continuously in a `b-input bare` cell and confirm the caret does
      not jump. (Automated result: kept, in both variants.)
- [ ] **Selection survival** — select a text range inside a cell, trigger a sibling re-render, confirm the
      selection is preserved. Not covered automatically: `setSelectionRange` + synthetic events do not
      reproduce a user drag-selection.
- [ ] **IME / composition** — verify a multi-keystroke composition (e.g. accented input) commits correctly without losing characters
- [ ] No visible flicker or layout shift while editing a cell; row height matches `--b-control-min-height-sm` density
- [ ] If the decision is "migrate" — tab/arrow navigation between editable cells still works; if "don't migrate" — confirm the documented numbers justify it
- [ ] Verify in Chromium + Firefox
