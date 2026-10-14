---
id: TASK-612
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
related: [TASK-610]
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB migrations match bucket names case-insensitively; DropCollection may delete a different bucket

## Context

Found 2026-10-14 by the cross-provider audit spawned from TASK-610 (same class: a name read back from the backend
matched case-insensitively against the declared one). Read in source, **not measured**. The premise that InfluxDB 2
bucket names are case-sensitive (`Foo` and `foo` coexist, Flux `from(bucket:)` matches exactly) is unconfirmed, so
the first step is to measure it on a live server (rule 54: falsify or record).

- `Birko.Data.Migrations.InfluxDB/Context/InfluxDBSchemaBuilder.cs:79-85` `FindBucket`: `OrdinalIgnoreCase` on name
  and id. Used by:
  - `DropCollection` — `DropCollection("Metrics")` would find and **delete `metrics`**. Data loss.
  - `CreateCollection` — with only `metrics` present, creates nothing and returns a builder for `Metrics`.
  - `CollectionExists` — "exists" on a case-only mismatch.
- `Context/InfluxDBDataMigrator.cs:35-50` `DeleteDocuments`: matches the bucket case-insensitively, then deletes
  using the **declared** name rather than `bucket.Name`.
- `InfluxMigrationStore.cs:41-42`: the `_migrations` bucket is matched case-insensitively but queried exactly at :102.

The same lookups have two more suspected defects (unmeasured): `FindBucketsAsync()` is called without a limit, and the
server may page at 20, so "missing" would be a false negative past 20 buckets. Nothing filters by organization either.
They come from the same lookups, and one fix (an exact `FindBucketByNameAsync` scoped to the org, as the InfluxDB
*store* already does) would cover all three.

## Acceptance criteria

- [ ] Measured on a live InfluxDB 2: are bucket names case-sensitive? Record the answer here. If they are not,
      cancel this task with that measurement.
- [ ] If they are: `DropCollection`, `CreateCollection`, `CollectionExists`, `DeleteDocuments` and the migration
      store's bucket check match the bucket name exactly. A live test where `metrics` exists and `Metrics` is dropped
      leaves `metrics` intact. Red before.
- [ ] The lookups are scoped to the configured organization and do not depend on paging. If the same measurement
      shows paging or cross-org matching is real, cover each with a test.

## Out of scope

- The InfluxDB data store (`FindBucketByNameAsync`), which the audit read as already exact.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
