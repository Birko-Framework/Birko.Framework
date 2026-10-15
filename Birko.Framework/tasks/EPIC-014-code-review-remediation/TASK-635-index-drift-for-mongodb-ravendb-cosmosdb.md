---
id: TASK-635
parent: EPIC-014
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-15
depends-on: [TASK-628]
blocks: []
related: [TASK-622, TASK-634]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Index drift is detected for SQL only: MongoDB, RavenDB and CosmosDB have none

## Context

Filed 2026-10-15 with the user. [[TASK-622]] made SQL schema drift report a declared index the table lacks (`Missing`,
`ColumnsDiffer`, `UniquenessDiffers`), measured on all four providers. The document stores have no equivalent: an index a
model declares can be absent, or differ, on MongoDB, RavenDB and CosmosDB, and nothing says so. On a document store a
missing index is mostly a performance failure, but a missing **unique** index is a correctness one: duplicates are
accepted silently (the storage-level enforcement [[TASK-632]] may rely on).

CosmosDB cannot answer "does index X exist" by name at all until [[TASK-628]] gives its index manager a name registry,
hence the dependency.

## Acceptance criteria

- [ ] Each provider reports declared-vs-stored index drift with the same kinds as TASK-622, read through its index
      manager, measured live (MongoDB, RavenDB; CosmosDB offline or on an emulator if one becomes available).
- [ ] The ElasticSearch / SQL health checks' pattern is followed, so a host can surface it at startup.
- [ ] Detection only: creating or changing an index stays the operator's (or a migration's).

## Out of scope

- Repair statements (a later task, if detection shows the need).

## Human test plan

N/A: asserted against live servers.
