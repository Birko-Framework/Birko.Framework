---
id: TASK-617
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-612]
blocks: []
related: [TASK-612]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The InfluxDB migration store's `_migrations` bucket is a name InfluxDB 2 refuses

## Context

Measured 2026-10-14 on `influxdb:2` while fixing TASK-612. `InfluxMigrationStore.MigrationsBucketName` is
`"_migrations"`. Creating it is refused:

```
BadRequestException: bucket name _migrations is invalid. Buckets may not start with underscore
```

InfluxDB 2 reserves leading-underscore names for its system buckets (`_monitoring`, `_tasks`). So `Initialize()` can
never create the bookkeeping bucket on a server that does not already have one, and every migration run on InfluxDB 2
fails at its first step. Until TASK-612 this was hidden behind an earlier failure: the organization name was passed
where an organization ID is required, a 400 of its own.

A live test is written and held for this task (it was in TASK-612's working tree): `InfluxMigrationStore(client,
"<fresh org>").Initialize()` creates the bucket, and a second `Initialize()` adopts it.

## Acceptance criteria

- [x] The bookkeeping bucket has a name InfluxDB 2 accepts. Decide the name, and whether it is configurable.
      → `birko_migrations` (the framework's prefix, no leading underscore), configurable through `InfluxMigrationStore(…,
      bucketName)` and `InfluxMigrationRunner(…, migrationsBucketName)`, and validated up front.
- [x] A database that already has a bucket under the old name (only possible if someone created it by hand, e.g. on
      1.x compatibility) is either adopted or reported, never silently replaced by an empty one. That would replay
      every migration (SH-H033's failure mode). Record which, and why.
      → Neither, because the case cannot occur. The API refuses to create `_migrations` (measured), and `influxd upgrade`
      names 1.x buckets `database/retention-policy`. I wrote an adoption fallback and then removed it, because a branch
      nothing can reach only drifts (rule 53). The reasoning is recorded on `DefaultMigrationsBucketName`.
- [x] Live: on a fresh organization, `Initialize()` creates the bucket with infinite retention, and a second call
      adopts it. Red today (measured).

## Out of scope

- Bucket lookup and organization scoping (TASK-612).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. `DefaultMigrationsBucketName = "birko_migrations"`, an optional bucket-name constructor argument on the store and
   runner, validated (non-blank, no leading `_`). The name is escaped where it enters Flux.
2. Tests: offline (default, custom, refusal) and live (fresh org: created with infinite retention, record and read
   back, a second store adopts the same single bucket).

## Progress log

- 2026-10-14 — `influxdb:2`. Red, measured during TASK-612: `Initialize()` → 400 "bucket name _migrations is invalid";
  the held test cannot compile against the old store (the constant is new API). Green: Migrations.InfluxDB 32/32 live.
  The round trip found that `RemoveMigration` never removes anything: `name` is a keyword in the delete-predicate
  grammar, and the 400 was swallowed. Filed as TASK-618, not fixed here.
