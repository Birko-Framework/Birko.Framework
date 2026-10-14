---
id: TASK-618
parent: EPIC-014
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-617]
blocks: []
related: [TASK-617]
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB `RemoveMigration` never removes anything: `name` is a keyword in the delete predicate

## Context

Measured 2026-10-14 on `influxdb:2` while proving TASK-617's round trip. `InfluxMigrationStore.RemoveMigration` deletes
with the predicate `_measurement="migrations" AND name="<migration>"`. The server refuses it:

```
400 invalid request; error parsing predicate: found NAME, expected identifier, string, number, bool at line 1, char 31
```

`name` is a keyword in the delete-predicate grammar. The quoted key `"name"="Sample"` is accepted (204, point gone;
measured with curl). The 400 is an `InfluxException`, which the method swallows (CR-L146's "already deleted" guess). So
every downgrade reported success and left the migration recorded as applied, and the next `Migrate()` will not
re-apply it after its `Down` ran: the database and the bookkeeping disagree.

## Acceptance criteria

- [ ] Live: record a migration, remove it, and `GetAppliedVersions()` no longer contains it. Red today (measured).
- [ ] Removing a migration that is not recorded does not throw. Measure what the server answers; it is the case the
      swallow existed for.
- [ ] Any other delete failure propagates (rule 22: a write that cannot be applied must never report success).

## Out of scope

- `InfluxDBDataMigrator.DeleteDocuments` passes a caller's Flux predicate verbatim; quoting it is the caller's job.
- The data store's `Guid="…"` predicates (`Guid` is not a keyword).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
