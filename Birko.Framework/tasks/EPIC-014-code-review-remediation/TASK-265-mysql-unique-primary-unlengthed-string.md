---
id: TASK-265
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-245, TASK-248, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# On MySQL a `[UniqueField]` or `[PrimaryField]` unlengthed string still emits `LONGTEXT`, so the table cannot be created

## Context — spawned by TASK-257, deliberately left open there

TASK-248 taught `MySQLConnector.ConvertType` to bound an **indexed** unlengthed string, because MySQL
cannot index a BLOB/TEXT column without a key length (measured on 8.4 as ERROR 1170). Its branch reads:

```csharp
else if (field.IsIndexed) return string.Format("VARCHAR({0})", IndexedStringColumnLength);
else                      return "LONGTEXT";
```

`IsIndexed` is set only by `DataBase.LoadIndexes`, which resolves `[IndexedField]` and `[CompositeIndex]`
**only**. But `MySQLConnector.FieldDefinition:289,293` emits `PRIMARY KEY` and `UNIQUE` as **inline column
constraints**, and those are index keys too — so:

```csharp
[UniqueField] public string Code { get; set; }   // -> LONGTEXT UNIQUE -> ERROR 1170 at CREATE TABLE
```

TASK-257 hit the identical hole on MSSql (`TEXT`, then `NVARCHAR(MAX)`, both refused as a key with
Msg 1919) and closed it by adding **`AbstractField.IsInIndexKey`** = `IsIndexed || IsUnique || IsPrimary`,
which `MSSqlConnector` now consults. **MySQL was deliberately left reading the narrow `IsIndexed`**, because
switching it changes emitted DDL on a provider TASK-257 did not stand up and measure — and this epic's
recurring defect is exactly a change believed correct on a provider nobody ran.

So the fix is very likely the one-word change `field.IsIndexed` → `field.IsInIndexKey`, but it is **not**
to be made from symmetry with MSSql. Measure it on a live 8.4 first.

The asymmetry is currently pinned by
`Birko.Data.SQL.MySQL.Tests/IndexKeyPredicateScopeTests.A_unique_or_primary_unlengthed_string_is_NOT_yet_bounded_here`,
which asserts today's `LONGTEXT` and says in its message not to "fix" the test by flipping the connector.
**That test is what should fail when this task lands** — update it together with the connector.

## Acceptance criteria

- [ ] Measured on a live MySQL 8.4: what `CREATE TABLE` does today for `[UniqueField]` and
      `[PrimaryField]` on an unlengthed string (expected ERROR 1170), recorded with the error number.
- [ ] After the fix, both declarations produce a creatable table with a genuine UNIQUE / PRIMARY KEY
      constraint — asserted against `information_schema`, not against the absence of an exception.
- [ ] The over-long write is refused rather than silently truncated. **Check MySQL's `sql_mode`**: with
      `STRICT_TRANS_TABLES` (the 8.x default) an over-length value errors; without it MySQL *truncates
      with a warning*, which would make the UNIQUE constraint quietly weaker than declared — the same
      trap TASK-257 measured for SQL Server's `ANSI_WARNINGS`. Record which applies.
- [ ] `IndexKeyPredicateScopeTests` updated in the same change, so the pin tracks the new truth.
- [ ] Proven able to fail — revert to `IsIndexed` and name the failing tests with counts.

## Out of scope

- MSSql's half — TASK-257, done.
- Binary columns as index keys (`LONGBLOB`, `VARBINARY(MAX)`) — TASK-266.

## Human test plan

- [ ] N/A — mechanical; the proof is a `CREATE TABLE` succeeding against a real MySQL 8.4 with the
      constraint present in `information_schema`.
