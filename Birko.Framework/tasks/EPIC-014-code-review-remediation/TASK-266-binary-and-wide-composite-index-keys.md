---
id: TASK-266
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-248, TASK-257, TASK-265]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Index keys that are still wrong after TASK-257: a `byte[]` column, and a composite too wide for the key limit

## Context — both found by TASK-257's close-gate code review, both measured

TASK-257 fixed the `DbType.String` arm of `MSSqlConnector.ConvertType`. Two neighbouring cases have the
same shape and were deliberately **not** folded in, because every acceptance criterion of that task said
*string* and a `byte[]` column is not one.

### 1. A binary column cannot be an index key either

`MSSqlConnector.ConvertType` maps `DbType.Object` / `DbType.Binary` to **`VARBINARY(MAX)`** (CR-M137) and
does **not** consult `IsInIndexKey`. `BinaryField` is constructed with `primary`/`unique`
(`AbstractField.cs:359-367`), so:

```csharp
[UniqueField] public byte[] Hash { get; set; }   // -> VARBINARY(MAX) UNIQUE
```

Measured on live SQL Server 2022 (16.0.4265.3):

| Statement | Result |
|---|---|
| `CREATE TABLE t (Col VARBINARY(MAX) UNIQUE)` | **Msg 1919** — the whole `CREATE TABLE` fails |
| `CREATE INDEX ix ON t (Col)` over `VARBINARY(MAX)` | **Msg 1919** |
| `CREATE TABLE t (Col VARBINARY(255) UNIQUE)` | OK |

So a `[UniqueField] byte[]` entity has no table at all on MSSql, and an `[IndexedField]` one loses its
index silently (recorded on `IndexCreationFailures`, which nothing subscribes to — TASK-204).
**MySQL has the same shape** via `LONGBLOB` (ERROR 1170) and needs its own live measurement, exactly as
TASK-265 does; do not fix one provider from symmetry with the other.

### 2. A wide composite overflows the key limit and fails at INSERT, not at DDL

`IndexedStringColumnLength` is 255 characters = **510 bytes** under `NVARCHAR`, which fits a single-column
key and a composite of up to three such columns. It does **not** cap the per-index total. Measured:

| Composite | DDL | Write |
|---|---|---|
| 3 × `NVARCHAR(255)` = 1530 B | created | OK |
| 4 × `NVARCHAR(255)` = 2040 B | **created**, warning 1708 | **Msg 1946** — *"The index entry of length 2040 bytes … exceeds the maximum length of 1700 bytes"* |

This is the quiet class TASK-257 existed to remove, reappearing one level up: a loud DDL failure has been
traded for a deferred, data-dependent write failure. A `[CompositeIndex]` over four or more unlengthed
strings is currently creatable and unusable.

## What to decide

- For binary: bound it (mirroring the string branch) or **refuse** the declaration? Unlike the string case
  there is no "7 live consumer entities already do this" argument to check — survey first. A hash or
  fingerprint column is a plausible real unique key and has a natural fixed width, which argues for
  requiring an explicit length rather than inventing 255 bytes.
- For the composite: cap the per-index budget (needs a whole-index view, which `ConvertType` does not
  have — it sees one field), warn at DDL time, or document only. Note `ConvertType`'s signature is
  `(DbType, AbstractField)`, so a per-index budget cannot be computed there; it belongs wherever the index
  column list is assembled.

## Acceptance criteria

- [ ] A decision recorded per case, with its reason.
- [ ] Measured on live SQL Server **and** live MySQL 8.4 before the remedy is chosen — including whether
      any framework or consumer model actually declares an indexed/unique `byte[]` today.
- [ ] Against a real server: a `[UniqueField] byte[]` entity's table is created and its constraint is
      present in `sys.indexes` (or the declaration is refused loudly at load time, if that is the choice).
- [ ] The wide-composite case either cannot be created, or is refused at DDL, or is documented with a test
      that pins the current deferred-failure behaviour so it cannot regress unnoticed.
- [ ] Proven able to fail.

## Out of scope

- The string arm — TASK-257, done.
- MySQL's `IsUnique`/`IsPrimary` string hole — TASK-265. This task is the *binary* and *width* half; if
  both land together, say so in both files rather than silently widening one.

## Human test plan

- [ ] N/A — mechanical; the proof is DDL and a write against real servers.
