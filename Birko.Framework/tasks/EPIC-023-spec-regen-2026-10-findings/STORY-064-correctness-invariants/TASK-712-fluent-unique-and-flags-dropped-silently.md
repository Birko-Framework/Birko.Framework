---
id: TASK-712
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-07]
pr: null
github-issue: null
jira-key: null
---

# Fluent model maps: a unique flag on a nullable column enforces nothing, and some declarations are dropped

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `ModelMapRegistry` sets `IsUnique` after `LoadIndexes`, so a fluent `IsUnique` on a nullable column creates no `ux_` index and no inline UNIQUE. Flags on a property with no mapped field are dropped silently (rule 55). The duplicate-index refusal works per descriptor while `ModelMap.Property()` always adds a new one, so it can be bypassed. `RegisterPrecision(null, null)` cannot unregister.

Evidence: Birko.Models.SQL/Mapping/ModelMapRegistry.cs:130,142; DataBase_Table.cs:552; ModelMap.cs:42-48; FieldBuilder.cs:103; DataBase_Field.cs:74-77.

## Acceptance criteria

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-712` — leave empty until then._
