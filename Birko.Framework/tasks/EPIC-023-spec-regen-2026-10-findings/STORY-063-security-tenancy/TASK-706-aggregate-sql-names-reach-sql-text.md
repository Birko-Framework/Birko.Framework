---
id: TASK-706
parent: STORY-063
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-01]
pr: null
github-issue: null
jira-key: null
---

# Aggregate queries put caller-supplied names and intervals into SQL text

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `AbstractConnectorBase.ResolveSqlName` falls back to the raw name when it cannot resolve a property, so a caller's group-by, time-column or source string reaches the SQL text unquoted; `ToSqlInterval` puts unparseable interval text inside `time_bucket('…')` without escaping. Rules 15, 33 and 45: an identifier is resolved against metadata or refused, never interpolated. `time_bucket` also exists only on TimescaleDB, so a time-bucketed aggregate fails on every other provider.

Evidence: Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs:1730-1801 (SelectAggregate path).

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

_Populated by `/tasks plan TASK-706` — leave empty until then._
