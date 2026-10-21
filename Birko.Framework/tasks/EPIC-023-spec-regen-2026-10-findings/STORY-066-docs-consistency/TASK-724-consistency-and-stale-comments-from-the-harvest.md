---
id: TASK-724
parent: STORY-066
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-19]
pr: null
github-issue: null
jira-key: null
---

# Consistency gaps and stale comments the harvest found

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Smaller items, each to confirm or close: async `CreateTableAsync` lacks the composite primary key, auto-increment refusal and `RecordTableCreated`; `SchemaDriftHealthCheck` can report Unhealthy though documented Degraded-only; `InsertAsync`/`SelectCountAsync` ignore `ct` for lock and open; async `SelectCountAsync(tables)` keeps null tables; sync and async inserts on one async connector use different locks; RavenDB settings never use user/password/secure; InfluxDB judges a failure transient by message substring; nullable aggregate view columns; a persistent view without a name counts on-the-fly; the instrumented store wrappers break aggregation above them; stale comments in `FieldBuilder` (HasPrecision 'NOT applied'), `ColumnDrift.CaseMismatch`, `SqLiteSettings` (cache id), `SqlOutboxStore` (lease).

Evidence: see the TASK-696 harvest reports; files named in the spec diffs of 22a2bb77.

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

_Populated by `/tasks plan TASK-724` — leave empty until then._
