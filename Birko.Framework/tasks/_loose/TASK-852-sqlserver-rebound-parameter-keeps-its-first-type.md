---
id: TASK-852
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-836]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A re-bound SQL parameter keeps the type it was created with

## Context

Flagged by the bulk-filter-operations respec (2026-11-07). The behaviour predates TASK-836. The user chose to file it.
- `AbstractConnectorBase.AddParameter` overwrites an existing name's value, which the multi-row insert relies on (it
  re-binds the same names per row).
- The provider's `CreateParameter` runs only when the parameter is first created. That is where SQL Server sets
  `DateTime2` (TASK-656).
- A name first bound to one CLR type and re-bound to another keeps the first type's `DbType` / `SqlDbType`. For
  example: null, then a `DateTime`; or an `int`, then a `decimal`.

## Acceptance criteria

- [x] Reproduced on SQL Server live, and on the other providers where type inference applies: a re-bound parameter
      whose value type changes is sent with the old type. Record what the server receives (truncation, conversion
      error, or a silently wrong value). If nothing observable goes wrong, close with the measurement.
      → **Reproduced, silently wrong (2026-11-07):**
        - Only the connector's public multi-row `Insert(table, models)` / `InsertAsync` re-binds names; the store's
          bulk create inserts one row at a time.
        - On SQL Server, a nullable `DateTime` null in row 1 and `…56.1234567` in row 2 stored `…56.1233333`. The
          re-bound parameter was re-inferred as `datetime`, not `DateTime2`. A raw re-bind showed `SqlDbType=DateTime`
          for null-then-DateTime, and `DateTime2` kept for DateTime-then-decimal.
      → PostgreSQL, MySQL and SQLite: nothing wrong. They set no explicit type, so each driver re-infers on every
        value; null then DateTime round-tripped exactly on all three.
- [x] Re-binding applies the same typing as creation (one producer, rule 28), or the value-type change is refused.
      → A re-bound name gets a newly created parameter in the same position:
        `command.Parameters[index] = CreateParameter(...)`, found with one `IndexOf`. Creation's typing is the only
        typing.
      → Refusing a type change was not an option: null followed by a value is the normal case for a nullable column.
- [x] Regression test red before the fix.
      → `ReboundParameterTypeLiveTests` (SQL Server, live sync and async multi-row tests, plus offline tests of the
        re-bound parameter's type): 4 of 6 red before the fix.
      → The lookup counter in `TestDbParameterCollection` is renamed `NameLookups` and counts `IndexOf` too.
      → All 35 Birko.Data.SQL importer suites green live.
      → **Found, filed:** `MSSqlConnector.BulkUpdate` / `BulkDelete` bypass `CreateParameter`. Measured: the same
        precision loss, and a composite-key `BulkDelete` matched nothing → TASK-854.

## Out of scope

- `AddParameter`'s lookup cost (TASK-836, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
