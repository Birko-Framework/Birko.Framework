---
id: TASK-707
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
findings: [SH-T696-02]
pr: null
github-issue: null
jira-key: null
---

# Redis and MongoDB connection strings interpolate credentials unescaped

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. TASK-653 moved the SQL providers onto their drivers' connection-string builders. Redis still appends option values unescaped, so `Password = "x,ssl=False"` becomes `password=x,ssl=False` and adds an option; MongoDB does not URL-encode user name, password or database, so `@`, `/` or `?` break or redirect the URI. Same class of injection TASK-653 fixed.

Evidence: Birko.Redis/RedisSettings.cs:53-81; Birko.Data.MongoDB/Stores/Settings.cs:36-48.

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

_Populated by `/tasks plan TASK-707` — leave empty until then._
