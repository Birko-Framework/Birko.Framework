---
id: TASK-263
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-19
depends-on: [TASK-256]
blocks: []
related: [TASK-256, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# There is no way to persist an instant with its offset — the timezone-aware column type is mapped but unreachable

## Context — found while planning TASK-256 (PostgreSQL UTC `DateTime` binding)

TASK-256 settled what a Birko `DateTime` column *means* on PostgreSQL: `timestamp without time zone`
holding the **wall-clock components as supplied**, with `DateTimeKind` not persisted and every read
returning `Kind=Unspecified`. That rule needs an escape hatch for the case it deliberately does not
serve — a value whose **offset must survive** — and while writing it I found the escape hatch is
mapped, documented by its own type map, and **unreachable**.

The connectors already answer `DbType.DateTimeOffset`, and two providers answer it with a genuinely
timezone-aware type:

| Provider | `DbType.DateTimeOffset` → | tz-aware? |
|---|---|---|
| PostgreSQL | `TIMESTAMPTZ` (`PostgreSQLConnector.cs:217-218`) | **yes** |
| MSSql | `DATETIMEOFFSET` (`MSSqlConnector.cs:159-160`) | **yes** |
| MySQL | `DATETIME` (`MySQLConnector.cs:216-217`) | no — falls back |
| SQLite | numeric group (`SqLiteConnector.cs:118-129`) | no — falls back |

`PostgreSQLConnector.DbTypeToNpgsqlDbType` (`:361`) likewise maps it to `NpgsqlDbType.TimestampTz`, so
even the binary `COPY` path is ready for it.

**Nothing can reach any of it.** Two independent walls:

- `AbstractField.CreateAbstractField` (`AbstractField.cs:203-265`) has **no `DateTimeOffset` arm**. Since
  SH-H037 (TASK-197) an unmapped property type no longer produces a silent null field — it throws
  `FieldAttributeException` at table load, taking out every route on the owning entity. So a
  `DateTimeOffset` property does not degrade; it refuses to start.
- **No attribute can override a field's `DbType`.** `Attributes/Field.cs` offers
  `IgnoreField` / `NamedField` / `UniqueField` / `PrimaryField` / `IncrementField` / `RequiredField` /
  `MaxLengthField` / `PrecisionField` / `ScaleField` / `IndexedField` / `CompositeIndex` — name, nullability,
  width, precision, indexing. Nothing selects a type. And `DateTimeField` hardcodes `DbType.DateTime`
  (`DateTimeField.cs:11-14`).

So `DbType.Date`, `DbType.Time` and `DbType.DateTimeOffset` are all **unreachable from a model** —
`ConvertType` arms that no field class produces.

## Why it matters

- **TASK-256's rule points at a door that does not open yet.** Its recorded decision says a value whose
  offset must survive needs this opt-in. Until this lands, the honest statement is "the framework cannot
  represent that at all", which is a § SH-H037 violation in the quietest form the CLAUDE.md describes: a
  refusal whose named opt-out does not exist. TASK-256 ships with the reference; this closes it.
- **It is load-bearing for TASK-256's implementation.** That task normalises **every** bound `DateTime`
  parameter on PostgreSQL, and its correctness rests on the premise *no bound `DateTime` can be targeting a
  `timestamptz` column* — true only because of the two walls above. Landing a tz-aware arm **falsifies that
  premise**, so this task must revisit `PostgreSQLConnector.NormalizeTimestampValue` and its call sites.
- Two providers already have the right column type and cannot be asked for it.

## What to decide

1. **A `DateTimeOffset` CLR arm** in `CreateAbstractField` (+ `DateTimeOffsetField` /
   `NullableDateTimeOffsetField`). The property *type* is the opt-in — no new attribute, no ambiguity, and it
   reads correctly on the two providers that have a matching type. Needs a `Read` implementation per provider
   for the two that fall back (a `DATETIME` / numeric column cannot return an offset, so the read must either
   reconstruct UTC or refuse).
2. **An attribute on a `DateTime` property** (e.g. `[TimeZonedField]`) selecting `DbType.DateTimeOffset` while
   keeping the CLR type `DateTime`. Smaller model churn for a consumer, but it needs a new `DbType`-override
   path that deliberately does not exist today, and it leaves the CLR type unable to carry the offset it just
   asked the database to preserve.
3. **Both** — the attribute as sugar over the arm.

Whichever is chosen, state what the **fallback** providers store and what they return on read: a silent
degrade to a timezone-less column is the failure mode this whole family of findings is about, so "MySQL and
SQLite fall back" must be a written, tested contract rather than an omission.

## Acceptance criteria

- [ ] A decision recorded with its reason, stating how the opt-in is spelled.
- [ ] An opt-in temporal property round-trips its **instant** on PostgreSQL and MSSql against live servers —
      value **and** offset asserted after read-back, not merely absence of an exception.
- [ ] The MySQL and SQLite fallback is a **stated, tested** contract: what column type, what is stored, and
      what a read returns. If information is lost, that is asserted, not implied.
- [ ] `DbType.Date` and `DbType.Time` resolved: either made reachable, or the dead `ConvertType` arms
      removed/documented as unreachable, so the next reader can tell a decision from an oversight.
- [ ] TASK-256's `NormalizeTimestampValue` premise re-verified — every call site audited against the new
      arm, so a tz-aware value is not silently stripped of its `Kind` by TASK-256's normaliser.
- [ ] An entity mixing a plain `DateTime` and an opt-in property on one table works, with both columns
      asserted.
- [ ] **The `DateTimeKind` asymmetry on MySQL / MSSql / SQLite measured** — delegated here by TASK-256, which
      fixed it on PostgreSQL only and deliberately did not wire the other three blind. On PostgreSQL a
      `Kind=Utc` value bound with no `DbType` inferred `timestamptz` and the server cast it through the
      session `TimeZone`, silently shifting it. Whether each of the other three re-infers a bound `DateTime`
      the same way is **unmeasured** — probably benign, since none has a tz-aware type in play for
      `DbType.DateTime`, but that is a guess and TASK-256's whole lesson is that the silent cell is only found
      by measuring. Measure each against a live server before adding or declining a normalisation.
- [ ] Proven able to fail.

## Out of scope

- The `Kind=Utc` binding defect on the plain `DateTime` path — TASK-256 owns it, and this task depends on it
  rather than duplicating it.
- Changing what a plain `DateTime` column means. TASK-256 recorded that rule after measuring the
  alternatives; this task adds a door beside it, it does not reopen it.
- MSSql's `TEXT` mapping — TASK-257.

## Human test plan

- [ ] N/A — mechanical; the proof is a live round-trip on PostgreSQL and MSSql with the offset asserted, plus
      the stated fallback assertions on MySQL and SQLite.
