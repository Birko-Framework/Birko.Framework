---
id: TASK-720
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
findings: [SH-T696-15]
pr: null
github-issue: null
jira-key: null
---

# InfluxDB store: reads skip init, page before filtering, and return dates in local time

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `Read(Guid)` skips the lazy-init and cancellation gate and wraps cancellation in `InvalidOperationException`. The bulk read pages before it filters, so a filtered `limit` returns fewer rows; sorting uses only the first field's direction. `DateTime` is stored as a string and read back with `DateTime.Parse`, so a UTC value probably comes back local. A duplicate create is never refused (store contract).

Evidence: Birko.Data.InfluxDB/Stores/AsyncInfluxDBStore.cs:99,126,218-243,404-447,692,781; InfluxDBStore.cs:125.

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

_Populated by `/tasks plan TASK-720` — leave empty until then._
