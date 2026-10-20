---
id: TASK-693
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-691, TASK-695]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants.SQL's indexes over unbounded string columns — unmeasured on MySQL

## Context

Found while planning [[TASK-684]] (2026-10-20), not measured. `Birko.Security.Grants.SQL` declares index-manager
indexes over string columns such as `ResourceType` and `Email`, which have no declared length. Rule 37 renders an
index-key string as `VARCHAR(255)` on MySQL. A composite key of several such columns can exceed InnoDB's 3 072 bytes
under utf8mb4, as the translation key did (TASK-680: 3 204 bytes, ERROR 1071). Schema-ensure would then report the
index as failed and leave it absent.

With [[TASK-691]] a fluent `HasMaxLength` can bound those columns.

## Acceptance criteria

- [x] Measure on live MySQL 8.4 which Grants.SQL indexes are created and which are reported in
      `IndexCreationFailures` (rule 54: measure before acting).
- [x] For any index that fails, bound its columns (`HasMaxLength`) so it fits, and assert it from the catalogue
      (rule 63). If none fails, record the measurement and close.

## Out of scope

- The translation table: [[TASK-684]].

## Human test plan

N/A — asserted by the live MySQL suite.

## Implementation plan

_Planned 2026-10-20 after measuring. Decided by the user: declare the indexes with the fluent `HasIndex`, so the schema
knows the key columns (rule 37); extend `FieldBuilder` to put one column in several indexes, split as [[TASK-695]]._

## Outcome

**Measured first (live, 2026-10-20).** Worse than guessed: the columns were not `VARCHAR(255)` but unbounded. The
indexes came only from the index manager, so the schema never learned these columns were keys.

| Provider | `ResourceType`, `Email` | `GrantSqlIndexes.EnsureAsync` |
|---|---|---|
| MySQL 8.4 | `LONGTEXT` | throws: ERROR 1170, BLOB/TEXT column used in key specification without a key length |
| SQL Server 2022 | `NVARCHAR(MAX)` | throws: Msg 1919, a type that is invalid for use as a key column |
| PostgreSQL 16 | `TEXT` | creates all three |
| SQLite | `TEXT` | creates all three (existing suite) |

**Fix.** `ResourceGrantMapping` declares `GrantsByUser (UserGuid, ResourceType)` and
`GrantsByResource (ResourceType, ResourceId)`, and `InvitationMapping` declares `InvitationsByEmail (Email)`, with
`HasIndex` under the existing names. `ResourceType` sits in both, through TASK-695. Schema-ensure creates the three and
bounds the key strings at 255 on MySQL and SQL Server. `EnsureAsync` stays as the startup check. The README gives the
`ALTER` remedy for a table created on MySQL or SQL Server before this, checked against the live servers.

**Tests.** `ProviderIndexLiveTests` runs on PostgreSQL, MySQL and SQL Server:
- schema-ensure alone creates the three indexes, and no failure is reported;
- the catalogue reports `ResourceType` and `Email` at 255 on MySQL and SQL Server (rule 63);
- `EnsureAsync` stays quiet, and so does a restart with fresh stores.

`SchemaTests` exposes the once-per-process mapping gate (`EnsureMapped`). The test project imports the three server
providers.

**Mutations, all red:**
- `GrantsByUser` undeclared: 3 live red;
- the `Email` index undeclared: 3 live red;
- `ResourceType` left out of `GrantsByResource`: the SQLite column check red.

**Regression:** Grants.SQL 27/27, live. Every test project importing Birko.Models.SQL was run at TASK-695. The Sandbox
consumer builds.
