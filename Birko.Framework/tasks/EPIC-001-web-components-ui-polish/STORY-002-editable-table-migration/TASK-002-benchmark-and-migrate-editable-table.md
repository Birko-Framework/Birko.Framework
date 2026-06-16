---
id: TASK-002
parent: STORY-002
status: todo
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

- [ ] Reproducible 500-row grid benchmark harness exists
- [ ] Baseline measured (current raw-input implementation)
- [ ] Bare-component variant measured
- [ ] Decision documented (migrate / don't migrate / partial) with the numbers
- [ ] If migrate: cells use `<b-input bare size="sm">`, event handling redesigned (bubble `change`, no parent `update()` on edit), caret + selection survive

## Out of scope

- Other table component changes
- Generic Shadow-DOM-per-cell optimization

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] Run the 500-row benchmark harness by hand and confirm the recorded numbers are reproducible across runs (note machine + browser)
- [ ] **Caret survival** — type continuously in a cell and confirm the caret position does not jump or reset on each keystroke (the core risk of the migration)
- [ ] **Selection survival** — select a text range inside a cell, trigger a sibling re-render, and confirm the selection is preserved
- [ ] **IME / composition** — verify a multi-keystroke composition (e.g. accented input) commits correctly without losing characters
- [ ] No visible flicker or layout shift while editing a cell; row height matches `--b-control-min-height-sm` density
- [ ] If the decision is "migrate" — tab/arrow navigation between editable cells still works; if "don't migrate" — confirm the documented numbers justify it
- [ ] Verify in Chromium + Firefox
