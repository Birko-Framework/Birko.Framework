---
id: TASK-620
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-269, TASK-610, TASK-611]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Schema drift compares column types only: nullability, defaults and key constraints that disagree read clean

## Context

Found 2026-10-15 reviewing what `DetectDrift` covers, after TASK-610/611 (asked: "all schema detection errors are
handled too?"). Read in source, **not measured**.

`AbstractConnector.DetectDrift` (`AbstractConnector_SchemaDrift.cs`) reads each stored column as a `StoredColumn` with
only `Name`, `TypeName`, `Size`, `Precision` and `Scale`, and compares `ConvertType(field)` against the rendered stored
type. So none of these is reported:

- **Nullability.** A column the model declares `NOT NULL` (`[RequiredField]`, a non-nullable value type) stored as
  `NULL`, or the reverse. The first lets rows in that the model says cannot exist. The second makes every INSERT that
  omits the value fail on a table the health check calls clean.
- **Defaults** the DDL emits (`AddColumnDefinition`'s back-fill default, a declared default) and the stored column
  lacks.
- **Primary key / unique.** A `[UniqueField]` or key column whose constraint is absent (TASK-567 is the "no declared
  key" sibling).

`SchemaDriftHealthCheck` then reports Healthy over all of them.

## Acceptance criteria

- [ ] Measured first, per provider (PostgreSQL, MSSQL, MySQL, SQLite): a table whose column differs from the model
      only in nullability reports clean today. Record it.
- [ ] Nullability drift is reported, on every provider, with a kind of its own or as `TypeMismatch` carrying both
      sides. Decide and record which. The declared side comes from the method that emits the DDL (rule 63), never
      restated.
- [ ] Defaults and unique / key constraints are each either reported, with a live test, or recorded here as
      deliberately not compared, with the reason.
- [ ] `EnsureColumns` stays additive: none of these is acted on.

## Out of scope

- Index drift (TASK-622). Tables outside the default schema (TASK-272).

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

_Drafted when picked._
