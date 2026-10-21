---
id: TASK-717
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
findings: [SH-T696-12]
pr: null
github-issue: null
jira-key: null
---

# SQL view DDL the providers refuse

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `ViewSqlGenerator` always emits `CREATE OR REPLACE VIEW`, refused by SQLite and SQL Server. SQLite's own view DDL uses `CREATE VIEW IF NOT EXISTS`, which never replaces. The SQL Server indexed-view clustered index names table-qualified source columns a view does not have. A bool join constant renders `TRUE`/`FALSE`, refused by SQL Server. The base async `ViewExists` swallows every exception and the async read funnels call the sync one.

Evidence: Birko.Data.Migrations.SQL/.../ViewSqlGenerator.cs:61; SqLiteConnector_View.cs:17; MSSqlConnector_View.cs:100,164; ViewSelectSqlBuilder.cs:204; AbstractConnector_CreateView.cs:128-142.

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

_Populated by `/tasks plan TASK-717` — leave empty until then._
