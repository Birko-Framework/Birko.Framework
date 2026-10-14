---
id: TASK-619
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-14
depends-on: [TASK-618]
blocks: []
related: [TASK-618]
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB `DeleteDocuments` discards the delete's Task: failures vanish and the delete races the next step

## Context

Found 2026-10-14 fixing TASK-618. `InfluxDBDataMigrator.DeleteDocuments` (`Context/InfluxDBDataMigrator.cs:44`) calls
`deleteApi.Delete(start, stop, predicate, bucket.Name, bucket.OrgID)` without waiting on the `Task` it returns. A
migration's delete step therefore reports success before the server has answered. A refused predicate, such as an
unquoted keyword key (TASK-618 measured `name` as one), is never observed, and the migration's next step can run
before the delete lands.

## Acceptance criteria

- [ ] Live: a predicate the server refuses throws from `DeleteDocuments`. Red today.
- [ ] Live: after `DeleteDocuments` returns, the deleted points are gone (count read straight after).

## Out of scope

- Quoting a caller's predicate: it is the caller's Flux, passed through verbatim by design (CR-M111).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. Wait on the call (`.GetAwaiter().GetResult()`), as the data store's sync deletes already do.
2. Two live tests, both proven red first.
