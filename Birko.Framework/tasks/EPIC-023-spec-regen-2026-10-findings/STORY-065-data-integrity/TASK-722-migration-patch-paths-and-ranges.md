---
id: TASK-722
parent: STORY-065
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
findings: [SH-T696-17]
pr: null
github-issue: null
jira-key: null
---

# Migrations: unescaped Cosmos patch paths, a delete range that stops at now, null-client NRE

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Cosmos `RenameField` and `UpdateDocuments` build patch paths `/{name}` without JSON-pointer escaping (`DropField` escapes). The InfluxDB 'delete everything' range stops at `UtcNow`, so future-dated points survive. `MongoMigrationRunner` reads `mongoClient.Database` in its base call, so a null client is an NRE, not `ArgumentNullException`. TimescaleDB introspection commands never set `command.Transaction`.

Evidence: CosmosDBSchemaBuilder.cs:176; CosmosDBDataMigrator.cs:87; InfluxDBDataMigrator.cs:40; MongoMigrationRunner.cs:34; TimescaleDBMigration.cs:612-631.

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

_Populated by `/tasks plan TASK-722` — leave empty until then._
