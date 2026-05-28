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
