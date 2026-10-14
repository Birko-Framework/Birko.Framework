---
id: TASK-618
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-617]
blocks: [TASK-619]
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

- [x] Live: record a migration, remove it, and `GetAppliedVersions()` no longer contains it. Red today (measured).
- [x] Removing a migration that is not recorded does not throw. Measure what the server answers; it is the case the
      swallow existed for.
      → The server answers 204 to a predicate that matches nothing, so "already deleted" never raised, and the swallow
      only ever hid the 400.
- [x] Any other delete failure propagates (rule 22: a write that cannot be applied must never report success).
      → The first live test stayed green after the catch was removed. Root cause: `DeleteApi.Delete` returns a `Task`,
      and this call discarded it, so the delete raced the next read and its errors went unobserved. It is now waited
      on. `InfluxDBDataMigrator.DeleteDocuments` has the same un-awaited call: TASK-619.

## Out of scope

- `InfluxDBDataMigrator.DeleteDocuments` passes a caller's Flux predicate verbatim; quoting it is the caller's job.
- The data store's `Guid="…"` predicates (`Guid` is not a keyword).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. Quote the tag key (`"name"="…"`), wait on the delete, and remove the catch.
2. Live tests: record → remove → not applied, a second remove does not throw, and a delete against a vanished bucket
   throws.

## Progress log

- 2026-10-14 — `influxdb:2`. Red: the remove left the version applied, and the vanished-bucket delete did not throw.
  Both were red on the old store when re-proved by reverting only that file. Green: Migrations.InfluxDB 34/34 live.
