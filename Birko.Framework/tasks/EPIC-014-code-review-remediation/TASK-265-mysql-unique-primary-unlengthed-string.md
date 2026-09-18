---
id: TASK-265
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-245, TASK-248, TASK-257]
findings: []
pr: 72eecf2 (Birko.Data.SQL.MySQL)
github-issue: null
jira-key: null
---

# On MySQL a `[UniqueField]` or `[PrimaryField]` unlengthed string emitted `LONGTEXT`, so the table could not be created

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

- [x] Measured on a live MySQL 8.4: what `CREATE TABLE` does today for `[UniqueField]` and
      `[PrimaryField]` on an unlengthed string (expected ERROR 1170), recorded with the error number.
- [x] After the fix, both declarations produce a creatable table with a genuine UNIQUE / PRIMARY KEY
      constraint — asserted against `information_schema`, not against the absence of an exception.
- [x] The over-long write is refused rather than silently truncated. **Check MySQL's `sql_mode`**: with
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

---

## Closed 2026-08-23

`MySQLConnector.ConvertType` now reads `AbstractField.IsInIndexKey` instead of `IsIndexed` — the one-word
change this file predicted, made only after the live measurement it insisted on.

### Re-scoped before being worked, because a sibling had moved underneath it

TASK-275 closed hours earlier and moved every **nullable** `[UniqueField]` column onto a synthesised index,
which sets `IsIndexed` and bounded it through the existing branch. So this task's description of its own scope
was a day stale. Measuring the four shapes separately on 8.4.11 showed what was left:

| Emitted DDL | Result |
|---|---|
| `LONGTEXT UNIQUE` — a `[RequiredField]` unique column | **ERROR 1170** |
| `LONGTEXT PRIMARY KEY` — a `[PrimaryField]` string | **ERROR 1170** |
| `VARCHAR(255) UNIQUE` | OK |
| `VARCHAR(255) PRIMARY KEY` | OK |
| 300 characters into `VARCHAR(255)` | **ERROR 1406**, 0 rows stored |

Both remaining shapes are the ones that keep an **inline** constraint, which is exactly what `IsIndexed`
cannot see and `IsInIndexKey` can.

### The sql_mode criterion, answered

`sql_mode` on the measured server is
`ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION`
— the 8.x default, which this framework never changes. With `STRICT_TRANS_TABLES` the over-long write is
`ERROR 1406` and nothing is stored, so the constraint is not quietly weakened. Without it MySQL truncates
with a warning, and two genuinely different values sharing a 255-character prefix would collide — the outcome
TASK-248 rejected prefix indexes to avoid. Pinned by its own test rather than left as a note.

### The pin was inverted, not deleted

`IndexKeyPredicateScopeTests.A_unique_or_primary_unlengthed_string_is_NOT_yet_bounded_here` asserted today's
`LONGTEXT` and carried, in its own failure message, the instruction not to "fix" it by switching the
connector from symmetry with MSSql. It is now
`A_unique_or_primary_unlengthed_string_is_bounded_here_too`, asserting `VARCHAR(255)` — and still asserting
that `IsIndexed` is false for these shapes, which is precisely the difference between the narrow flag and the
wide one.

### Verification

**1,433 tests, 0 failed, 0 skipped** across eighteen suites with `BIRKO_REQUIRE_LIVE` set throughout — 3 new
(`UnlengthedConstraintColumnLiveTests`), plus the inverted theory. The constraints are asserted from
`information_schema` and then proven by a duplicate insert, not from the absence of an exception —
`CreateTable` records index failures rather than raising them, so "it did not throw" would have passed
against a table with no constraint at all.

**Mutation:** reverting to `field.IsIndexed` fails **5 of 97** in the MySQL suite — the two inverted theory
cases and all three new live tests — while the other three providers stay green, because none of them reads
this branch.

### Deliberately not done

- **No change to the other three providers.** MSSql already read `IsInIndexKey` (TASK-257); SQLite and
  PostgreSQL index an unbounded string happily and genuinely ignore the flag.
- **No prefix index.** Rejected in the original filing and still rejected: it makes the constraint weaker
  than declared. A bounded column refuses the over-long write instead, which is measured above.
- **`IndexedStringColumnLength` stays 255** — chosen in TASK-257 to match across providers, not because it is
  MySQL's ceiling (3072 bytes for the whole key).
