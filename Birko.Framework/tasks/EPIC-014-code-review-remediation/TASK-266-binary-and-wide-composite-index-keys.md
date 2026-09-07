---
id: TASK-266
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-248, TASK-257, TASK-265]
findings: []
pr: "SQL 43fb29a, MSSql 12e29ea, MySQL 0e49ae6; tests cbfeaab/dbd458e/f00e447/64652f0"
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

- [x] A decision recorded per case, with its reason.
- [x] Measured on live SQL Server **and** live MySQL 8.4 before the remedy is chosen — including whether
      any framework or consumer model actually declares an indexed/unique `byte[]` today.
- [x] Against a real server: a `[UniqueField] byte[]` entity's table is created and its constraint is
      present in `sys.indexes` (or the declaration is refused loudly at load time, if that is the choice).
- [x] The wide-composite case either cannot be created, or is refused at DDL, or is documented with a test
      that pins the current deferred-failure behaviour so it cannot regress unnoticed.
- [x] Proven able to fail.

## Out of scope

- The string arm — TASK-257, done.
- MySQL's `IsUnique`/`IsPrimary` string hole — TASK-265. This task is the *binary* and *width* half; if
  both land together, say so in both files rather than silently widening one.

## Implementation plan

### Step 0 — measured 2026-09-07 on live SQL Server **16.0.4265.3** (the build this task's own numbers came from) and live MySQL **8.4.11**, before a line changed

**The survey criterion 2 demands: 0 declarations.** Across the framework and all 16 consumer repos there
are **5** `byte[]` properties in total, and **none** carries `[UniqueField]`/`[IndexedField]`/
`[PrimaryField]`, nor is named by any class-level `[CompositeIndex]`. All five are in **non-SQL** projects
(`Birko.Communication.Camera`, `.Modbus`, `.NFC`) and two are `{ get; }`-only, so not mappable at all.
The grep was validated against a known fully-qualified `[Birko.Data.SQL.Attributes.CompositeIndex]`
instance first, per § TASK-248's warning that an unqualified grep misses those entirely. **So the binary
half is wholly latent** — both remedies are affordable, and neither can break a live entity.

**Binary, measured:**

| statement | SQL Server 16.0.4265.3 | MySQL 8.4.11 |
|---|---|---|
| unbounded binary + inline `UNIQUE` | **Msg 1919 + Msg 1750**, and *not catchable by `TRY/CATCH`* — the batch aborts, so the whole `CREATE TABLE` fails | **ERROR 1170** |
| `CREATE INDEX` over unbounded binary | **Msg 1919** | **ERROR 1170** |
| `VARBINARY(255)` + inline `UNIQUE` | OK | OK |
| the real ceiling | `VARBINARY(901) UNIQUE` also OK — the limit is the 1700-byte nonclustered key, not 900 | `VARBINARY(3072)` OK, **`VARBINARY(3073)` ERROR 1071** |

**Wide composite, measured — and the two providers behave oppositely:**

| | SQL Server | MySQL (utf8mb4, `dynamic` row format) |
|---|---|---|
| 3 × 255 | 1530 B — DDL OK, silent | 3060 B — DDL OK, **12 bytes under the limit** |
| 4 × 255 | 2040 B — **DDL created, with a warning** | 4080 B — **DDL REFUSED, ERROR 1071** |
| then a *max-width* INSERT | **Msg 1946**, row rejected | n/a, no index exists |
| then a *short* INSERT | **succeeds — 1 row landed** | n/a |

⚠ **That last row is the measurement this task did not have, and it settles the composite question.** The
wide index is not broken, it is **data-dependent**: a table whose values stay short works perfectly
today. So refusing at DDL would break working code — `PredicateScope`'s recorded rule that *a false
refusal breaks working code and is worse than the hole*. And the quiet form is **MSSql-only**: MySQL
already refuses loudly, so a framework guard would duplicate the server on one provider and regress the
other. (Both happen to break at exactly four 255-columns, by coincidence of different limits.)

⚠ **A third finding that changes the binary remedy: `BinaryField` cannot carry a length at all.** It has
no length parameter and `CreateAbstractField` never passes `maxLength` for a `byte[]` — so
`[MaxLengthField(32)] byte[] Hash` is **silently dropped** today. This task's own "What to decide" argues
for *"requiring an explicit length rather than inventing 255 bytes"*, and that option is **not
expressible** as the code stands: refusing and telling the author to declare a length would be
§ TASK-263's *"named an escape hatch that did not open"*.

### Decisions

**Binary — bound at the provider, and make an explicit length possible.** Not a load-time refusal:
unbounded binary as a unique key is perfectly legal on PostgreSQL (`BYTEA`) and SQLite (`BLOB`), so
refusing framework-wide would convert a working declaration into a start-up failure on two providers to
fix two others — § TASK-248's exact veto, which is why that task absorbed MySQL's limit at MySQL. So:

1. **`BinaryField` gains a length** (`MaxLength`), and `CreateAbstractField` passes `maxLength` through
   for `byte[]`. Spelled `MaxLength`, deliberately **not** `Lenght` like `CharField`'s — that typo is
   shipped public API which cannot be renamed, but a new member should not inherit it, and `MaxLength`
   matches `MaxLengthField` / `FieldDescriptor.MaxLength`. Recorded so it reads as a choice.
2. **MSSql and MySQL `ConvertType`** binary arms become: a declared length → `VARBINARY(n)`; else an
   index key (`IsInIndexKey`) → `VARBINARY(IndexedBinaryColumnLength)` = 255; else unchanged
   (`VARBINARY(MAX)` / `LONGBLOB`). The gate is `field is BinaryField`, because **`DbType.Object` shares
   this arm on all four connectors** and a serialized object has no business taking a length.
3. **PostgreSQL and SQLite unchanged** — `BYTEA` and `BLOB` have no declarable length and index fine.
   Asserted per provider, or the change is indistinguishable from a blanket one (§ TASK-278).
4. **255 for the same reason TASK-257 chose it**: the same model runs on both servers, so a value that
   indexes on one must index on the other. The real ceilings differ (1700 B vs 3072 B) and 255 is far
   under both; overridable via `IndexedBinaryColumnLength`, with a test for the override, because "the
   real ceiling is the key limit, not this number" is otherwise unenforced prose.

**Wide composite — pinned, not refused,** which is criterion 4's third arm and now rests on the
short-INSERT measurement rather than on taste. A per-index byte budget is also not computable where the
type is chosen: `ConvertType`'s signature is `(DbType, AbstractField)` — one field, no index context.
Capping is worse still: it would silently shrink a declared column because of what an index elsewhere
does with it. A *warning* channel is the one defensible middle, and it is deliberately not built here:
`IndexCreationFailures` is named and documented for failures, this is not a failure on either provider,
and inventing a second channel for a latent case is speculative API. Recorded as a decision with its
reason, and pinned by tests per provider so the divergence reads as measured rather than accidental.

### Proven able to fail

One revert per claim: the MSSql declared-length branch, the MSSql indexed-default branch, the MySQL pair,
the `CreateAbstractField` pass-through, and the `field is BinaryField` gate (which a `DbType.Object`
field must not trip).

### Out of scope

- The string arm — TASK-257 (MSSql) and TASK-265 (MySQL), both done.
- A per-index key-budget calculator and a DDL warning channel. Both are features, both are argued
  against above rather than merely deferred; if a real consumer ever declares a wide composite on SQL
  Server, that is the trigger to revisit.

## Human test plan

- [x] N/A — mechanical; the proof is DDL and a write against real servers.

---

## Closed 2026-09-07 — one half fixed, one half pinned, and the measurement decided which

### Measured before a line changed (criterion 2)

Live **SQL Server 16.0.4265.3** — the exact build this task's own numbers came from — and live
**MySQL 8.4.11**.

**The survey.** Across the framework and all 16 consumer repos there are **5** `byte[]` properties in
total and **none** carries `[UniqueField]`/`[IndexedField]`/`[PrimaryField]`, nor is named by any
class-level `[CompositeIndex]`. All five are in non-SQL projects (`Communication.Camera`, `.Modbus`,
`.NFC`); two are `{ get; }`-only and not mappable at all. The grep was validated against a known
fully-qualified `[Birko.Data.SQL.Attributes.CompositeIndex]` first, per § TASK-248's warning. **Wholly
latent**, so both remedies were affordable.

| | SQL Server | MySQL |
|---|---|---|
| unbounded binary + inline `UNIQUE` | **Msg 1919 + Msg 1750**, *not catchable by `TRY/CATCH`* — the batch aborts | **ERROR 1170** |
| `CREATE INDEX` over unbounded binary | **Msg 1919** | **ERROR 1170** |
| `VARBINARY(255)` + `UNIQUE` | OK | OK |
| the real ceiling | `VARBINARY(901) UNIQUE` OK — the limit is the 1700-byte nonclustered key, not 900 | `VARBINARY(3072)` OK, **`(3073)` ERROR 1071** |
| 3 × 255 composite | 1530 B, clean | 3060 B, clean — **12 bytes** inside the limit |
| 4 × 255 composite | **created, with a warning** | **REFUSED, ERROR 1071** |
| then a max-width INSERT | **Msg 1946** | n/a |
| then a **short** INSERT | **succeeds — 1 row lands** | n/a |

### The third finding, which changed the binary remedy

**`BinaryField` could not carry a length at all** — no constructor parameter, and
`CreateAbstractField` never passed `maxLength` for a `byte[]`, so `[MaxLengthField(32)]` on one was
silently dropped. This task's own *"What to decide"* argued for *"requiring an explicit length rather
than inventing 255 bytes"*, and that option **was not expressible**: refusing and telling the author to
declare a width would have been § TASK-263's *"named an escape hatch that did not open"*. Opening it is
half the fix.

### Decisions

**Binary — bound at the provider, and make an explicit width possible.** Not a load-time refusal: an
unbounded binary unique key is legal on PostgreSQL (`BYTEA`) and SQLite (`BLOB`), so refusing
framework-wide would convert a working declaration into a start-up failure on two providers to fix two
others — § TASK-248's exact veto. `BinaryField` gains `MaxLength` (spelled correctly, **not** `Lenght`
like `CharField`'s shipped typo, with a test pinning that); `ConvertType` on MSSql and MySQL becomes the
same three-way choice the string arm makes, gated on `field is BinaryField` because **`DbType.Object`
shares that `case` on all four connectors**.

**Wide composite — pinned, not refused**, which is criterion 4's third arm and rests on the
short-INSERT measurement rather than on taste. Three reasons, in order of weight:

1. **A false refusal would break working code.** The over-wide index is *data-dependent*: a table whose
   values stay short works today. `PredicateScope`'s recorded rule.
2. **The quiet form is SQL Server's alone** — MySQL already refuses at DDL, so a framework guard would
   duplicate one server and regress the other.
3. **It is not computable where the type is chosen.** `ConvertType`'s signature is
   `(DbType, AbstractField)` — one field, no index context. Capping would be worse still: silently
   shrinking a declared column because of what some index elsewhere does with it.

A *warning* channel is the one defensible middle and is deliberately not built: `IndexCreationFailures`
is named and documented for failures, this is not a failure on either provider, and inventing a second
channel for a latent case is speculative API.

### Measurements

`BIRKO_REQUIRE_LIVE` set throughout, against live **SQL Server 2022 (16.0.4265.3)**, **MySQL 8.4.11**,
**PostgreSQL 16.15**, **TimescaleDB 2/PG16** and on-disk **SQLite**: **1,599 tests, 0 failed,
0 skipped** across nine suites — MSSql **129** (111 → 129), MySQL **116** (101 → 116),
`Birko.Data.SQL` **672**, PostgreSQL **103**, SQLite 341, Migrations.SQL 87, Migrations.TimescaleDB 81,
TimescaleDB 56, SQL.View.Migrations 14. **43 new tests.** Builds clean, no new nullable warning.

**Mutations, six, one per claim:**

| mutation | red |
|---|---|
| MSSql: drop the declared-width branch | **3** |
| MSSql: drop the indexed branch | **9** |
| MSSql: swap their order (indexed would beat declared) | **2** |
| MySQL: drop the declared-width branch | **3** |
| MySQL: drop the indexed branch | **8** |
| drop the shared `CreateAbstractField` pass-through | **3** MSSql + **3** MySQL + **2** field contract, **PostgreSQL 0** |

The last row is the one worth keeping: the shared change reds both providers *and* the field contract
while leaving PostgreSQL at zero, which is the false-side control doing its job. Provider mutations
leave the field contract at 0, so the two layers are cleanly separated.

### Acceptance

- [x] A decision recorded per case, with its reason — bound the binary, pin the composite.
- [x] Measured on live SQL Server **and** live MySQL 8.4 before the remedy was chosen, including the
      survey: **0** indexed/unique `byte[]` declarations anywhere.
- [x] Against a real server: the `[UniqueField] byte[]` table is created, the constraint is in
      `sys.indexes` / `information_schema.statistics`, and a duplicate is rejected.
- [x] The wide composite is documented **with tests that pin the current deferred-failure behaviour**
      per provider, including the short-row control that justifies not guarding it.
- [x] Proven able to fail — six mutations, above.

### ⚠ Deliberately not done

- **No per-index key-budget calculator and no DDL warning channel.** Argued against above rather than
  merely deferred; the trigger to revisit is a real consumer declaring a wide composite on SQL Server.
- **No load-time refusal of an unbounded binary index key** — it is legal on two of the four providers,
  and both have tests asserting they are untouched.
- **SQLite has no binary test of its own.** `BLOB` takes no length and the SQLite suite ran clean at
  341; PostgreSQL carries the false-side assertions for the whole unaffected pair, since the two behave
  identically here. Said rather than implied.
- **⚠ Two of my own measurement faults, both recorded because each looked like a code failure.**
  (1) I set `BIRKO_REQUIRE_LIVE` globally across suites whose servers were not running, and read
  PostgreSQL's 63 and MySQL's 65 failures as signal — the skip-as-failure trap § TASK-259 records
  falling into "one task after documenting it". Re-run per provider with its own server up.
  (2) My new MySQL live class defaulted `BIRKO_MYSQL_PASSWORD` to `Birko!Passw0rd` while **all nine**
  existing classes in that project default to `root`, producing 65 `Access denied` failures unrelated to
  the change — the same fixture trap TASK-273 recorded **in this very suite**. Aligned; the full suite
  is now 116/116 on `BIRKO_MYSQL_HOST` alone.
