---
id: TASK-513
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

# A `decimal` on SQLite is stored as a binary float — money columns drift on the framework's default provider

## Context

Spawned from TASK-512, which fixed the MySQL / SQL Server half (an unprecisioned `decimal` was an integer column
there) and deliberately left SQLite alone. On SQLite a `decimal` property maps to `REAL` when unprecisioned and
`NUMERIC(p,s)` when declared, and **both store a binary float**:

- **Measured at TASK-498 (2026-09-26):** `10.10m + 0.20m` reads back `10.299999999999999m` under both `REAL` and
  `NUMERIC(18,2)`. SQLite keeps both as an 8-byte IEEE float; `NUMERIC(p,s)` is only a type *affinity*, and the
  precision and scale are ignored.
- A plain Set of `10.30m` round-tripped exactly in the same measurement, so the loss shows up in arithmetic
  (increments, SQL-side sums) and in values with more significant digits than a double holds (~15–17), not in
  every write.

**Why it matters now.** SQLite is the framework's default provider and **Symbio's default** (`DataProviders:Default`
= `SQLite`). Measured at TASK-512: Symbio has ~240 unprecisioned `decimal` properties in `[Table]` entities, the
majority of them money (invoice/order/cart/credit-note lines and totals, tax, salary, exchange rate). Symbio is in
dev, so a storage change can recreate its tables rather than migrate them.

## Questions to answer by measurement first (none measured yet)

- How does `Microsoft.Data.Sqlite` bind a `decimal` parameter (TEXT is the documented default), and what does each
  column affinity do with it — `REAL`, `NUMERIC`, `TEXT`, none?
- Which of these break if the column becomes TEXT: `ORDER BY` (lexicographic `"10" < "9"`), range predicates,
  `SUM`/`AVG` aggregates, `PropertyUpdate.Increment` (`col = col + @p`, TASK-498)?
- Does `DecimalField.Read` (`reader.GetDecimal`) read every candidate representation back exactly?

## Measurements (2026-10-03, Microsoft.Data.Sqlite 10.x, SQLite 3.53.3)

Harness: a throwaway file-based app binding through `AddWithValue`, exactly as `SqLiteConnector.AddParameter` does.

**Binding.** A `decimal` parameter is bound as **TEXT** (`SqliteType.Text`), normalised: `10.10m` → `'10.1'`,
`10m` → `'10.0'`, `decimal.MaxValue` in full. The column's affinity then decides what is stored.

| | `REAL` (unprecisioned today) | `NUMERIC(22,6)` (declared today) | `TEXT` / no affinity | `TEXT COLLATE BIRKO_DECIMAL` + `birko_decimal_add` | scaled `INTEGER` ×10⁶ |
|---|---|---|---|---|---|
| stored as | real | integer if whole, else real | text | text | integer |
| ≤15-digit values read back | exact | exact | exact | exact | exact |
| `1234567890123456.123456m` (22,6) | **→ `1234567890123456.0`** | **→ `1234567890123456`** | exact | exact | **out of range** |
| `decimal.MaxValue` | write OK, **read THROWS `OverflowException`** | same | exact | exact | out of range |
| `Increment` 10.10 + 0.20 | **10.299999999999999** | **10.299999999999999** | **10.299999999999999** (stays text) | **10.3** | 10.3 |
| …then `WHERE v = 10.30m` | no match | no match | no match | — | — |
| `ORDER BY` (−5.5, 0.2, 2.5, 9, 10, 100) | correct | correct | **lexical: 10, 100, 2.5, 9** | correct | correct |
| `v > 9.5` | correct | correct | **`[]`** | correct (index used) | correct |
| `SUM` of ten 0.1 | 1.0 | 1.0 | 1.0 (as real) | needs a custom aggregate | exact |
| `MIN`/`MAX` | correct | correct | lexical | correct | correct |
| connection without the registration | — | — | — | **`ORDER BY` and `INSERT` into an indexed column THROW** `no such collation sequence` | — |

Findings beyond the table:

- **Today's storage already loses data past ~15 significant digits**, not only in arithmetic: a `DECIMAL(22,6)` value
  at the ceiling is truncated to 16 digits, and `decimal.MaxValue` is a write that succeeds and a read that throws.
- **Scaled INTEGER at scale 6 has a ceiling of 9 223 372 036 854.775807**: 13 integer digits against `DECIMAL(22,6)`'s
  16. Option (b) cannot hold the canonical type; it would need a smaller canonical precision on SQLite or a refusal.
- **Plain TEXT (a) breaks ordering, ranges and Increment.** Every comparison path would need rewriting.
- **(d) TEXT + collation + functions** (the EF Core approach) fixes ordering, ranges, `MIN`/`MAX`, equality across
  trailing zeros (`10.00m` matches `10.0`) and Increment, and keeps index use. Its costs: every connection must
  register them (one funnel in `SqLiteConnector`, but consumers' raw connections and external tools such as the
  `sqlite3` CLI or DB Browser cannot `ORDER BY` or write to an indexed decimal column); `SUM`/`AVG` in views need
  custom aggregates; and SQL-side arithmetic other than Increment would still go through floats.

## Candidate fixes (decide after measuring)

- **(a) TEXT column, decimal-as-text** — exact storage, but ordering, comparison and arithmetic are wrong unless
  every one of those paths is rewritten; likely the most invasive.
- **(b) Scaled INTEGER** — store `value × 10^scale` as a 64-bit integer: exact, sorts and compares correctly, and
  sums correctly; bounded to ~19 significant digits, which `DECIMAL(22,6)` exceeds — needs a stated ceiling and a
  refusal past it.
- **(c) Document and refuse nothing** — accept float storage on SQLite as a known limit, and say so where the
  default provider is chosen. Only if (a) and (b) both measure as unworkable.

## Acceptance criteria

- [x] The questions above answered with measurements against Microsoft.Data.Sqlite, recorded here
- [x] A choice among (a)/(b)/(c), made by the owner on those measurements — the owner chose **(d)**, a TEXT variant
      the measurements added (TEXT + `BIRKO_DECIMAL` collation + registered functions), on 2026-10-03/04
- [x] If (a) or (b): `10.10m + 0.20m` reads back `10.30m` through `Increment`, ordering and range predicates are
      correct, and a value past the representable range is refused rather than rounded — applied to (d), the TEXT
      variant: `SqLiteDecimalStorageEndToEndTests` + the inverted TASK-498 test
- [x] `DetectDrift` on SQLite stays clean for a table the framework creates, and reports an old `REAL`/`NUMERIC`
      decimal column if the declaration changes
- [x] CHANGELOG entry (storage change on the default provider) with the migration, or the documented limit for (c)

## Human test plan

N/A — fully covered by automated tests: storage, ordering, ranges, equality, increments (single, concurrent,
overflow), drift, back-fill and the CHANGELOG migration all run against a real on-disk SQLite file, and nothing
here has a rendered surface a person would judge.

## Progress log

- 2026-10-03 — measurements recorded above; the owner asked for the connections (d) needs to be checked and fixed.
- 2026-10-03 — **connection survey for (d).** Framework: every production `SqliteConnection` is created in
  `SqLiteConnector` (`CreateConnection`'s two branches + the journal-mode PRAGMA connection); unit of work,
  migrations, job locks and index management all go through `CreateConnection`. Measured: a registration on an
  unopened connection survives `Open`, `Close`/`Open` of the same object, and pooling on and off. Consumers:
  Presenter `PresentationStore` goes through `AsyncSqLiteModelRepository`, so the connector covers it (it only uses
  `SqliteConnectionStringBuilder` to parse a path; a first grep miscounted it as a raw connection). Symbio
  `TelemetryBuffer` is a genuine raw connection on a private table, so unaffected; its double-averaging compaction and
  undocumented bypass are filed as Symbio TASK-874 (FIELD-019). **Symbio `tools/sqlite-cli` runs ad-hoc SQL on `symbio-dev.db` and will need `SqLiteDecimal.Register`**
  once columns change; ~15 Symbio unit-test files open raw connections against framework tables likewise.
- 2026-10-03 — **connection layer done.** `SqLiteDecimal` (collation `BIRKO_DECIMAL`, `birko_decimal_add`,
  `birko_decimal_sum` / `_avg` aggregates; refuses overflow and unparseable values) registered by one producer,
  `SqLiteConnector.NewConnection`. `SqLiteDecimalConnectionTests` 12/12; mutation (registration removed) → 10 fail;
  SQLite suite 408/408. **Not yet done:** `ConvertType` still emits `REAL`/`NUMERIC`, Increment still emits
  `col = col + @p`, views still emit `SUM`/`AVG` — nothing uses the registrations until those change.
- 2026-10-04 — **storage switched.** `ConvertType`: decimal → `TEXT COLLATE BIRKO_DECIMAL`, double stays `REAL`.
  New `AbstractConnectorBase.IncrementExpression` hook (SQLite: `birko_decimal_add`); translator takes the connector.
  Drift's stored side reads the collation from `sqlite_master.sql` (measured: `pragma_table_info` reports `TEXT`).
  Decimal default literal `'0.0'`. `SqLiteDecimalStorageEndToEndTests` 23/23 incl. the CHANGELOG migration (measured:
  an already-drifted REAL migrates as `10.299999999999999`, not repaired). Mutations: column back to REAL → 9 fail;
  increment via native `+` → 4 fail (incl. overflow no longer refused); collation not read → 3 fail; bare default →
  1 fails. Inverted per rule 56: TASK-498 drift pin, migrations' SQLite expectations (4), Health drift (2) + a new
  provider-free scale-only comparison test. Views → TASK-514. Suites: SQLite 431, SQL 700, Migrations 127, Health 20,
  and the 9 other SQLite-importing suites green.
- 2026-10-04 — **close gate** (conventions, intent, correctness, security, comments; run as parallel passes).
  Fixed from it: **the CHANGELOG migration silently dropped every index and unique constraint and left child FKs
  pointing at `T_old`** (correctness pass, High) — measured both, recipe now adds `legacy_alter_table` + `DROP INDEX`,
  proven by a new test with each step mutated out (index gone / FK rewritten); collation parse narrowed (`'1,000'`
  collated equal to `1000`); stored collation read anywhere in the column's definition; one producer for the
  `type COLLATE x` text (rule 16); false/stale comments and docs (`health.md`, `BoundedDecimalType`, three tests);
  rule 64 registered (connection-registered DDL). Spawned TASK-515 (declared scale never enforced on SQLite).
  Accepted as-is: TEMP / attached-schema tables read no `CREATE` statement (the framework creates neither).

## Out of scope

- MySQL / SQL Server / PostgreSQL decimals — exact since TASK-512 (PostgreSQL always was)
- `double` / `float` properties — binary floats by declaration, correctly stored as such
- View `SUM` / `AVG` over a decimal column — TASK-514
- SQLite enforcing a declared precision / scale (it never has) — TASK-515, found by this close's conventions pass
- TEMP / attached-schema tables in `DetectDrift`'s collation read — decided not to do: the framework creates neither,
  and `StoredColumnsSql` already reads only the main schema's `pragma_table_info`
- Symbio adopting the change — `SqLiteDecimal.Register` in `tools/sqlite-cli` and in its raw-connection tests,
  recreating `symbio-dev.db` — owned by the Symbio session (prompt handed over 2026-10-04; it files the task itself,
  Symbio id to be recorded here once filed)
