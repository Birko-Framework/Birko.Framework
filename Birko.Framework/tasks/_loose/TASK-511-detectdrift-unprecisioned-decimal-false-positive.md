---
id: TASK-511
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-03
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `DetectDrift` reports an unprecisioned `decimal` as drifted on MySQL and SQL Server — on a table the framework created

## Context

Found while proving TASK-510 against live servers. A `decimal` property with no `[PrecisionField]`/`[ScaleField]`
makes `ConvertType` emit a bare `DECIMAL`; the server stores its own default precision, and the catalogue reports
that back. `SameColumnType` compares text, so a table `CREATE TABLE` just made is reported as drifted:

- MySQL 8.4: `Amount: declared DECIMAL, stored decimal(10,0)`
- SQL Server 2022: `Amount: declared DECIMAL, stored DECIMAL(18,0)`
- PostgreSQL 16: clean (`numeric` without a modifier has no precision)

Measured by `EnsureColumnsLiveTests.An_ensured_table_reports_exactly_what_a_table_created_whole_reports` in both
suites, which asserts "same report as a table created whole" instead of "clean" for exactly this reason.

This is the false-positive direction TASK-269 warned about: noise on every entity with an unprecisioned `decimal`,
which is how a drift report gets ignored (`Birko.Health.Data.SQL`'s check surfaces it to operators).

## Acceptance criteria

- [x] A bare `DECIMAL` declaration compares equal to the provider's default precision on MySQL (`(10,0)`) and SQL
      Server (`(18,0)`) — and only that one: `DECIMAL(18,2)` against stored `(18,0)` must still be reported (TASK-264)
  — `AbstractConnector.DeclaredAsStored(declared)`, applied to the comparison only (the report's `Declared` stays
  the DDL's text); overridden in `MySQLConnector` and `MSSqlConnector` with the server's documented default,
  never read back from the table being judged. Live: `A_bare_DECIMAL_the_framework_created_reports_clean` and
  `A_bare_DECIMAL_declaration_against_a_stored_scale_is_still_reported` in both suites.
- [x] The two live tests above assert `IsClean` again
  — and keep the "same report as a table created whole" comparison. Mutation: comparing without
  `DeclaredAsStored` fails exactly those four tests (2 per provider), 6 of 8 stay green.
- [x] Decide and record whether the real fix is the declaration (an unprecisioned decimal rounds away fractions on both
      servers — measured 2026-10-03: `INSERT 7.5` into a bare `DECIMAL` reads back `8` on MySQL 8.4 and SQL Server 2022), which would be a separate, behaviour-changing task — **silent data loss on every unprecisioned `decimal`**, so likely the more important half
  — **Decided: yes, the declaration is the real defect** → [[TASK-512]], P2. This task only stops the report crying
  wolf; it makes the lossy column look *healthy*, which is correct about the schema (it is what the DDL made) and
  says nothing about the data. Framework blast radius measured as zero (every SQL-mapped framework `decimal` has
  `HasPrecision(22).HasScale(6)`); consumer exposure unmeasured. TASK-512 must delete the overrides added here
  once the declaration stops being bare (rule 53).

## Out of scope

- Changing what `ConvertType` emits for an unprecisioned decimal (see the third criterion — record, don't do)
