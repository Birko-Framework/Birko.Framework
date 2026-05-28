---
id: STORY-002
parent: EPIC-001
status: planned
created: 2026-05-28
---

# b-editable-table migration to bare components

## User story

As a maintainer, I want `b-editable-table` cells to reuse Birko form components (via the `bare` attribute) so chrome is unified across the form family — but only if the Shadow-DOM-per-cell perf cost is acceptable.

## Behaviour

- Decision is benchmark-gated on a 500-row grid (before vs after)
- If migrated: each cell becomes `<b-input bare size="sm">` (or the equivalent select/checkbox)
- Event handling redesigned to bubble `change` from custom elements; no parent `update()` on edit
- Caret + selection must survive keystrokes (current invariant)
