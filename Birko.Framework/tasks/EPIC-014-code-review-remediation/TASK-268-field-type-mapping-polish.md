---
id: TASK-268
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-257, TASK-263]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Two small SQL field-mapping gaps found while typing MSSql's string columns

## Context — grouped spawn from TASK-257

Two unrelated-but-adjacent items surfaced while auditing `CreateAbstractField` and the provider
`ConvertType` arms. Grouped because each is a few lines, both live in the same two files, and doing them
together costs one review instead of two — per § Conventions, *several small ones from the same thread →
one grouped task, not six.*

### 1. `TimeOnlyField` declares the widest string type on every provider despite being fixed-width

`Birko.Data.SQL/SQL/Fields/TimeOnlyField.cs:46-54` is an `AbstractField` with `DbType.String` — **not** a
`CharField` — so it falls into every provider's unbounded-string branch: `NVARCHAR(MAX)` on MSSql (as of
TASK-257; it was `TEXT` before), `TEXT` on PostgreSQL and SQLite, `LONGTEXT` on MySQL.

Its own doc (`:27-33`) argues the stored shape is a fixed-width culture-independent `HH:mm:ss` and that this
"compares correctly with `<`, `>` and `BETWEEN`". That claim was **false on MSSql** for the whole life of the
framework, because `TEXT` cannot be used with `ORDER BY` or a comparison at all (Msg 306 / 402) — TASK-257
fixed it incidentally by making the column `NVARCHAR(MAX)`.

The column is 8 characters by construction (`TimeOnlyField.Format`). Declaring it as a bounded 8-character
field would make the documented ordering guarantee true by construction on every provider, allow it to be
indexed (an unbounded string cannot be an index key on MSSql or MySQL), and needs no per-provider branch.

### 2. `CharField.Lenght` is `int?`, so `NVARCHAR()` / `VARCHAR()` is a reachable syntax error

`CharField.cs:11` declares `public int? Lenght = 1`. Three connectors interpolate it directly —
`MSSqlConnector` (`NVARCHAR({0})`), `MySQLConnector` and `PostgreSQLConnector` (`VARCHAR({0})`) — so a null
length renders `NVARCHAR()`, a syntax error on every one of them.

**Not reachable from a model**: `AbstractField.CreateAbstractField` constructs `CharField` only at `:332`
(literal `1`, for a CLR `char`) and `:343` (guarded by `length != null && length > 0`). It *is* reachable
from public surface — `new CharField(property, name, lenght: null)` — and `ConvertType`/`FieldDefinition`
are public.

A decision rather than a defect: either give the constructor a non-null default and refuse null, or have the
three connectors fall back. Refusing at construction is the § SH-H037 shape (a mapper that cannot express
something refuses rather than emitting something broken) and keeps the connectors free of a guard each.

## Acceptance criteria

- [ ] `TimeOnly` columns declare a bounded 8-character type on all four providers, and a test asserts the
      declared type per provider (SQLite will still report `TEXT`/affinity — pin what it actually does
      rather than what it ought to, as TASK-263 did for its `INTEGER`/ISO-text mismatch).
- [ ] `TimeOnly` ordering asserted against a **real** server for at least one provider, since the ordering
      guarantee is the reason the change is worth making.
- [ ] `CharField` with a null length either cannot be constructed, or produces valid DDL — decided, with
      the reason recorded, and covered by a test on the chosen behaviour.
- [ ] Confirm no existing entity's column type changes in a way that breaks stored data. A `TimeOnly`
      column narrowing from an unbounded type to 8 characters only affects **new** tables (nothing
      reconciles an existing one — see TASK-257), so state that explicitly.
- [ ] Proven able to fail.

## Out of scope

- The string arm on MSSql — TASK-257, done.
- Binary index keys and wide composites — TASK-266.

## Human test plan

- [ ] N/A — mechanical; the proof is the declared column type and a correctly ordered result set.
