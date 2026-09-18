---
id: TASK-263
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-19
depends-on: [TASK-256]
blocks: []
related: [TASK-256, TASK-257]
findings: []
pr: f2fda25
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

## Decision — `[UtcField]`, and the promise it deliberately does not make

Recorded 2026-08-19, verified against live PostgreSQL 16, SQL Server 2022, MySQL 8.4 and on-disk SQLite.

> **A plain Birko `DateTime` column is a wall clock (TASK-256). A `DateTime` property marked `[UtcField]` is an
> instant: normalised to UTC on write, stored in the provider's timezone-aware column type where one exists,
> and read back as `DateTimeKind.Utc` on every provider. Both meanings coexist per property on one entity. A
> caller's original offset is normalised away on every provider — the promise is the instant, not the offset.**

### Two structural facts that narrowed the deliverable

1. **A field cannot behave differently per provider.** `Tables.Table` holds no connector reference, and
   `AbstractField.Read` is reached through the static, provider-blind `DataBase.Read`. There is no per-provider
   read hook, and adding one is far larger than this task.
2. **The fallback columns cannot carry an offset.** `TIMESTAMPTZ` and `DATETIMEOFFSET` can; MySQL's `DATETIME`
   and SQLite's numeric affinity cannot.

So this opt-in **cannot** promise a caller's offset round-trips. It promises the instant is exact and reads back
as UTC — still a real gain over a plain `DateTime` column, which cannot say *which* instant it names.

### The measured binding matrix — the step that could have invalidated the design

Bound `DateTimeOffset(2026-03-15 10:30:00Z, +00:00)` into each provider's own
`ConvertType(DbType.DateTimeOffset)` column, under a non-UTC session on the two providers that have one:

| Provider | Column | Stored | `GetFieldValue<DateTimeOffset>` | `GetDateTime` |
|---|---|---|---|---|
| PostgreSQL 16 | `TIMESTAMPTZ` | `11:30+01` (= `10:30Z`) | instant exact | `10:30Z`, `Kind=Utc` |
| SQL Server 2022 | `DATETIMEOFFSET` | `10:30:00Z` | instant exact | **throws** `InvalidCastException` |
| MySQL 8.4 | `DATETIME` | `10:30` (offset dropped) | instant exact | `10:30`, `Unspecified` |
| SQLite | declares `INTEGER`, stores **text** | `10:30:00+00:00` | instant exact | `11:30+01`, `Kind=Local` |

**All four accept a `DateTimeOffset` parameter**, so the design holds. And `GetFieldValue<DateTimeOffset>` is the
**only** uniform read: `GetDateTime` is wrong or fatal on three of four, so the obvious implementation would
have passed on PostgreSQL and failed outright on MSSql.

### Why an attribute rather than a `DateTimeOffset` CLR property

A `DateTimeOffset` property would *advertise* an offset that half the supported providers cannot honour. An API
that over-promises is worse than one that states its limit — and the attribute is cheaper for consumers, since
an existing `DateTime` model opts in per property with no type change. Rejected alongside it: refusing on
MySQL/SQLite (makes a model non-portable; TASK-248 already measured that instinct as wrong when a declaration
works on some providers and not others), and ISO-8601 text storage everywhere (preserves the offset but breaks
ordering, range queries and comparisons — silently degrading every query on the column).

### How this composes with TASK-256, which it falsified

TASK-256's `NormalizeTimestampValue` strips `Kind` from **every** bound `DateTime` on PostgreSQL, on the stated
premise that no bound `DateTime` could target a `timestamptz` column. `[UtcField]` makes that false. Since
`AddParameter(command, name, value)` cannot know the target column, the resolution is the bound value's **CLR
type**: `UtcDateTimeField.Write` returns a **`DateTimeOffset`**, which that helper's `is DateTime` test does not
match. No signature change, no field context, no per-provider plumbing, and TASK-256's helper stays as narrow as
it was.

**Measured cost of getting that wrong:** reverting `Write` to return a bare `DateTime` stores the instant
**an hour out, silently** (`09:30` for a `10:30Z` value on a UTC+1 server) — and only a non-UTC server can see
it. That is why the invariant is asserted directly (`UtcFieldMappingTests`) *and* through the stored instant
(`UtcFieldInstantLiveTests`).

### The delegated survey — measured, and the answer is "no change"

Bound a plain `Kind=Utc` `DateTime` to each provider's plain `DateTime` column, under a non-UTC session where
one exists:

| Provider | Result |
|---|---|
| **PostgreSQL 16** | **`11:30` — shifted** (the pre-TASK-256 defect, reproduced via raw ADO, which validates the probe) |
| SQL Server 2022 | `10:30` — no shift |
| MySQL 8.4 | `10:30` — no shift |
| SQLite | `10:30` — no shift |

The asymmetry is **PostgreSQL's alone**, so TASK-256's normalisation correctly stays on `PostgreSQLConnector`
rather than moving to `AbstractConnectorBase`. Pinned for MySQL — the only fallback provider with a settable
session zone — by `A_plain_utc_kinded_datetime_is_not_shifted_by_a_non_utc_session`. MSSql and SQLite are not
pinned because neither converts its datetime type on storage at all, so there is no session zone for a shift to
come from.

### Recorded divergences, deliberately not "fixed"

- **SQLite declares `INTEGER` and stores ISO-8601 text.** Misleading, and left alone because plain
  `DbType.DateTime` declares `INTEGER` and stores text too — changing only the new one would make it diverge
  from its neighbour. Pinned by a test asserting both the declaration and `typeof()`.
- **`DbType.Date`, `DbType.Time` and a CLR `DateTimeOffset` property remain unreachable from a model**, by
  design, documented at `CreateAbstractField` because that dispatch is the only producer of fields.
  `ConvertType` keeps answering them because it is public surface a consumer may call directly.

### Proven able to fail

| Revert | Result |
|---|---|
| `Write` returns a bare `DateTime` (the silent one) | **1 of 82** PostgreSQL — exactly `A_utc_field_stores_the_same_instant_on_a_non_utc_server`, reading `09:30` for a `10:30Z` value |
| `CreateAbstractField` ignores the attribute | **3 of 82** PostgreSQL + **9 of 565** core |
| The non-`DateTime` guard removed | **2 of 565** core |

1,011 tests green across five suites (565 · 82 · 61 · 74 · 229), 31 new.

## Acceptance criteria

- [x] A decision recorded with its reason, stating how the opt-in is spelled. — `## Decision` above:
      `[UtcField]` on a `DateTime`, with the `DateTimeOffset`-CLR-property alternative rejected because it
      would advertise an offset half the providers cannot honour.
- [x] An opt-in temporal property round-trips its **instant** on PostgreSQL and MSSql against live servers —
      value **and** offset asserted after read-back, not merely absence of an exception. — `UtcFieldInstantLiveTests`
      in both suites: the instant asserted exactly, `Kind=Utc` asserted, and the **column type read from the
      catalogue** (`timestamp with time zone` / `datetimeoffset`) rather than inferred from a call that did not
      throw. ⚠ *Criterion narrowed by the spelling decision*: it was written assuming a `DateTimeOffset` CLR
      property, which would have had an offset to assert. With `[UtcField]` the read-back type is `DateTime`, so
      "offset" is expressed as `Kind=Utc` — and the offset **normalisation** is asserted separately in the two
      fallback suites. Narrowed before the work, on the recorded decision, not afterwards to fit the result.
- [x] The MySQL and SQLite fallback is a **stated, tested** contract: what column type, what is stored, and
      what a read returns. If information is lost, that is asserted, not implied. — `UtcFieldFallbackLiveTests`
      (MySQL) and `UtcFieldFallbackTests` (SQLite): column type asserted from the catalogue / `pragma_table_info`,
      the stored text asserted, the instant asserted exact, and `The_original_offset_is_normalised_away_not_preserved`
      asserting the **loss** explicitly. SQLite's declared-`INTEGER`/stored-text mismatch is pinned rather than
      hidden.
- [x] `DbType.Date` and `DbType.Time` resolved: either made reachable, or the dead `ConvertType` arms
      removed/documented as unreachable, so the next reader can tell a decision from an oversight. — Documented
      as deliberately unreachable at `CreateAbstractField`, which is the **only** producer of fields, with the
      reason for each (`Date` truncates a timestamp — the CR-H086 bug; `Time` because `TimeOnly` maps to
      `DbType.String`; a CLR `DateTimeOffset` property because the offset cannot survive on two providers). The
      arms stay because `ConvertType` is public surface a consumer may call directly.
- [x] TASK-256's `NormalizeTimestampValue` premise re-verified — every call site audited against the new
      arm, so a tz-aware value is not silently stripped of its `Kind` by TASK-256's normaliser. — The premise
      **is** falsified, and the resolution is `Write` returning a `DateTimeOffset`, which the helper's
      `is DateTime` test does not match. All three call sites plus the six prepared bulk-update/delete binding
      sites audited; the helper's doc rewritten from "TASK-263 will falsify this" to the actual resolution, and
      TASK-256's own premise test re-pointed at the narrower rule it now asserts.
- [x] An entity mixing a plain `DateTime` and an opt-in property on one table works, with both columns
      asserted. — every live suite's probe entity carries both `ObservedAt` (`[UtcField]`) and `NoticeDate`
      (plain), with both column types and both read-back `Kind`s asserted; the offline
      `An_unmarked_datetime_on_the_same_entity_is_still_a_wall_clock` pins the mapping half.
- [x] **The `DateTimeKind` asymmetry on MySQL / MSSql / SQLite measured** — delegated here by TASK-256, which
      fixed it on PostgreSQL only and deliberately did not wire the other three blind. On PostgreSQL a
      `Kind=Utc` value bound with no `DbType` inferred `timestamptz` and the server cast it through the
      session `TimeZone`, silently shifting it. Whether each of the other three re-infers a bound `DateTime`
      the same way is **unmeasured** — probably benign, since none has a tz-aware type in play for
      `DbType.DateTime`, but that is a guess and TASK-256's whole lesson is that the silent cell is only found
      by measuring. Measure each against a live server before adding or declining a normalisation.
      — **Measured: the asymmetry is PostgreSQL's alone.** A plain `Kind=Utc` `DateTime` is stored
      unshifted on SQL Server 2022, MySQL 8.4 and SQLite, and shifted on PostgreSQL 16 (which reproduces
      the pre-TASK-256 defect and so validates the probe). Normalisation therefore **declined** on the
      other three, on evidence rather than symmetry, and TASK-256's helper correctly stays on
      `PostgreSQLConnector`. Pinned for MySQL, the only fallback provider with a settable session zone.
- [x] Proven able to fail. — three independent reverts, table in `## Decision`. The first is the one that
      matters: it fails **1 of 82**, silently storing an instant an hour out, and only the non-UTC database
      can see it.

## Out of scope

- The `Kind=Utc` binding defect on the plain `DateTime` path — TASK-256 owns it, and this task depends on it
  rather than duplicating it.
- Changing what a plain `DateTime` column means. TASK-256 recorded that rule after measuring the
  alternatives; this task adds a door beside it, it does not reopen it.
- MSSql's `TEXT` mapping — TASK-257.

## Implementation plan

### Two structural findings that shaped the decision

1. **A field cannot behave differently per provider.** `Tables.Table` holds no connector reference, and
   `AbstractField.Read` is reached through the static, provider-blind `DataBase.Read(fieldSets, reader, data,
   index)`. There is no per-provider read hook and adding one would be a much larger change than this task.
2. **The fallback columns cannot carry an offset.** `TIMESTAMPTZ` (PostgreSQL) and `DATETIMEOFFSET` (MSSql) do;
   MySQL's `DATETIME` and SQLite's numeric affinity do not.

Together those mean **this opt-in cannot promise that a caller's offset round-trips.** A single `Read` cannot
recover an offset from a MySQL `DATETIME`. What it *can* promise on all four providers is that the **instant is
exact and reads back as UTC** — which is a real gain over today, where `Kind` is discarded and the stored wall
clock is ambiguous about which instant it names.

### Decision

- **Spelling: `[UtcField]` on a `DateTime` property.** The CLR type stays `DateTime`, so existing models opt in
  per property with no type change — which is what makes this reachable for Symbio, whose entities are all
  `DateTime` and whose `UtcDateTimeJsonConverter` already treats storage values as UTC by convention. A
  `DateTimeOffset` CLR arm was rejected: its type would *advertise* an offset that cannot survive on two of the
  four providers, and an API that over-promises is worse than one that states its limit.
- **Semantics: instant-preserving, offset-normalised, on every provider.** Write the UTC instant; read back
  `Kind=Utc`. Uniform across all four, which is what TASK-256 prioritised when it rejected `TIMESTAMPTZ` for
  making PostgreSQL diverge from the SQLite the product tests on. The original offset is lost **everywhere**,
  by design, and that is asserted rather than implied.
- Rejected: refusing on MySQL/SQLite (makes a model non-portable, and TASK-248 measured that instinct as wrong
  when a declaration works on some providers and not others), and ISO-8601 text storage (preserves the offset
  but breaks ordering, range queries and comparison on the column — silently degrading every query).

> **The recorded rule this adds beside TASK-256's:** a `DateTime` column is a wall clock; a **`[UtcField]`
> `DateTime` column is an instant**, stored in the provider's timezone-aware type where one exists, and read
> back as `Kind=Utc` on every provider. Neither preserves a caller's original offset.

### The interaction with TASK-256 — this is the risky part, and it drives the design

TASK-256's `PostgreSQLConnector.NormalizeTimestampValue` strips `Kind` from **every** bound `DateTime`, on the
documented premise that no bound `DateTime` can be targeting a `timestamptz` column. **This task falsifies that
premise**, and the failure would be silent and wrong: a `Kind=Utc` value stripped to `Unspecified` is inferred
by Npgsql as `timestamp`, and inserting a `timestamp` into a `timestamptz` column makes the server interpret
the wall clock **in the session's time zone** — storing a different instant, no error. Exactly the defect
TASK-256 existed to remove, re-entering through its own fix.

`AddParameter(command, name, value)` has no field context, so it cannot special-case the column. The chosen
resolution avoids needing one:

**`UtcDateTimeField.Write` returns a `DateTimeOffset`, not a `DateTime`.** The CLR property stays `DateTime`;
only the *bound value* changes type. Consequences, all of them wanted:

- `NormalizeTimestampValue` matches `is DateTime` and therefore **ignores it** — no signature change, no field
  context, no per-provider plumbing, and TASK-256's helper stays exactly as narrow as it was.
- Npgsql binds a `DateTimeOffset` to `timestamptz` and SqlClient to `datetimeoffset` — the correct type on both
  tz-aware providers, with the instant unambiguous.
- The premise test TASK-256 left behind (`A_datetime_property_maps_only_to_DbType_DateTime`) **will fail**, by
  design. That is the alarm working: it must be rewritten to assert the *new* rule (a plain `DateTime` still
  maps to `DbType.DateTime`; a `[UtcField]` one maps to `DbType.DateTimeOffset`), not deleted.

**What is unmeasured and must be measured first:** whether MySqlConnector and Microsoft.Data.Sqlite accept a
`DateTimeOffset` parameter against their fallback columns at all, and what they store. Step 1 settles it before
any production code is written, because if either refuses, `Write` needs a per-provider answer and the design
above does not hold.

### Steps

1. **Measure the binding matrix first — four providers, live.** For each of PostgreSQL 16, MSSql 2022, MySQL
   8.4 and SQLite: bind a `DateTimeOffset` (UTC instant, zero offset) into that provider's
   `ConvertType(DbType.DateTimeOffset)` column; record what is stored and what
   `reader.GetDateTime` / `GetFieldValue<DateTimeOffset>` returns. Run it under a **non-UTC session** on the two
   servers that have a session time zone, since that is where an ambiguous write shows up (TASK-256's lesson).
   Record the matrix in this file — it is the evidence for criterion 3, and it decides whether step 4 needs a
   fallback branch.

2. **`[UtcField]` attribute** in `Birko.Data.SQL/Attributes/Field.cs`, beside the existing `Field` descendants.
   Doc comment states the semantics and that it is an *instant*, not a wall clock.

3. **`UtcDateTimeField` / `NullableUtcDateTimeField`** in `SQL/Fields/`, `DbType.DateTimeOffset`:
   - `Write` — normalise to a definite UTC instant, then return `DateTimeOffset`:
     `Kind=Utc` as-is · `Kind=Local` → `ToUniversalTime()` · `Kind=Unspecified` → **treat as UTC**
     (`SpecifyKind(v, Utc)`), matching the convention Symbio's converter already applies. Document why
     `Unspecified` is read as UTC rather than local: the attribute *declares* the property holds UTC.
   - `Read` — return a `DateTime` with `Kind=Utc`. Must work identically on all four providers; step 1 says
     whether that is `reader.GetFieldValue<DateTimeOffset>(index).UtcDateTime` uniformly or needs a
     `GetDateTime` path for the fallback providers.
   - Nullable twin follows `NullableGuidField`/`NullableDateTimeField`: `IsDBNull` → null, else delegate.

4. **Wire it into `CreateAbstractField`** — set a `utc` flag in the existing attribute loop (beside
   `NamedField`/`PrimaryField`/…), then branch the `DateTime`/`DateTime?` arm on it. `[UtcField]` on a
   non-`DateTime` property **throws `FieldAttributeException`** naming the type: an attribute that silently does
   nothing is the § SH-H037 shape, and this one would silently store a wall clock while the model claims an
   instant.

5. **Revisit TASK-256's premise (criterion 5).** Audit both `NormalizeTimestampValue` call sites plus the six
   prepared bulk-update/delete binding sites against the new arm; update the helper's doc comment, which
   currently says the `TIMESTAMPTZ` arm is unreachable from a model. Rewrite the premise test to assert the new
   two-way rule rather than the old one-way one.

6. **`DbType.Date` and `DbType.Time` (criterion 4)** — still unreachable: no field class produces them, and
   this task adds no arm for them. Resolve by *documenting them as deliberately unreachable* on `ConvertType`
   (a `DateTime` maps to `DbType.DateTime`, a `TimeOnly` to `DbType.String` per `TimeOnlyField`'s own reasoning),
   so a later reader can tell a decision from an oversight. Removing the arms is out of scope — they are public
   `ConvertType` surface a consumer may pass directly.

7. **The delegated survey (criterion 8)** — measure whether MySQL, MSSql and SQLite share the `Kind=Utc`
   inference asymmetry TASK-256 fixed on PostgreSQL: bind a `Kind=Utc` `DateTime` to a plain `DbType.DateTime`
   column on each, under a non-UTC session where applicable, and check for a shift. Then **add or decline a
   normalisation per provider on the measurement**, not on symmetry. Expected benign — none has a tz-aware type
   in play for `DbType.DateTime` — but that is the guess TASK-256's whole lesson forbids acting on.

8. **Tests** (`Birko.Data.SQL.Tests` offline; the four provider suites live):
   - Round-trip a `[UtcField]` property on **live PostgreSQL and MSSql**: instant exact, read-back `Kind=Utc`,
     and the **column type** asserted from the catalogue as `timestamp with time zone` / `datetimeoffset`
     (criterion 2). Assert against the catalogue, not "it did not throw" — this layer swallows.
   - The MySQL and SQLite fallback as a **stated, tested contract** (criterion 3): column type asserted, the
     instant asserted exact, and the offset loss asserted explicitly rather than left implied.
   - A mixed entity — one plain `DateTime` and one `[UtcField]` on the same table (criterion 6) — with both
     columns asserted, proving the two rules coexist per property.
   - **The non-UTC session test, which is the one that discriminates:** on a non-UTC server, a `[UtcField]`
     write must store the same instant as on a UTC server. Uses the dedicated-database + `ClearAllPools()`
     mechanism TASK-256 established, for the reason recorded there.
   - `[UtcField]` on a non-`DateTime` property throws, and the message names the property and its type.
   - The rewritten premise test from step 5.

9. **Prove able to fail (criterion 7)** — revert independently and record each split: the `Write`
   `DateTimeOffset` conversion (expect the non-UTC instant tests to fail, *not* a throw — this is the silent
   one), the `CreateAbstractField` wiring (expect the column type to fall back to `TIMESTAMP`), and the
   non-`DateTime` guard.

10. **Document** — `§ Conventions` entry in the aggregator `CLAUDE.md` stating both rules together (wall clock
    vs instant) since they are only comprehensible as a pair; the four provider `CLAUDE.md` files' Data Types
    sections, whose `DbType.DateTimeOffset` rows currently describe a mapping nothing could reach; and a
    `Recent Updates` entry.

### Files

- `Birko.Data.SQL/Attributes/Field.cs` — `UtcField`
- `Birko.Data.SQL/SQL/Fields/UtcDateTimeField.cs` — new
- `Birko.Data.SQL/SQL/Fields/AbstractField.cs` — attribute flag + `DateTime` arm branch + the non-`DateTime` throw
- `Birko.Data.SQL.PostgreSQL/Database/Connectors/PostgreSQLConnector.cs` — helper doc only, unless step 1 says otherwise
- Per-provider `CLAUDE.md` Data Types sections; aggregator `CLAUDE.md`
- Tests: `Birko.Data.SQL.Tests` (offline mapping + guard), and the PostgreSQL / MSSql / MySQL / SqLite suites

Five repos → five commits, production first (§ Task tracking › Integration model).

### Risks

- **Step 1 can invalidate the design.** If MySqlConnector or Microsoft.Data.Sqlite refuses a `DateTimeOffset`
  parameter, `Write` cannot return one uniformly and the fix needs a per-provider answer with no hook to hang it
  on. Measure before writing production code; if it refuses, stop and re-decide rather than inventing plumbing.
- **The silent failure mode is a wrong instant, not an exception.** Every assertion is a stored/read value, and
  the discriminating ones run under a non-UTC session — a UTC-only run cannot tell a correct write from an
  ambiguous one.
- **SQLite has no real type affinity for this.** Its `ConvertType` groups `DbType.DateTimeOffset` with the
  numeric types, so what is actually stored depends on the ADO provider's conversion. Step 1 measures it; the
  contract records it.

### Out-of-scope boundaries (not work — owners named)

- The wall-clock rule for a plain `DateTime` — TASK-256 owns it; this task adds a door beside it.
- MSSql's `TEXT` mapping — TASK-257.

## Human test plan

- [ ] N/A — mechanical; the proof is a live round-trip on PostgreSQL and MSSql with the offset asserted, plus
      the stated fallback assertions on MySQL and SQLite.
