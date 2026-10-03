---
id: TASK-513
parent: null
feature: null
status: todo
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

## Candidate fixes (decide after measuring)

- **(a) TEXT column, decimal-as-text** — exact storage, but ordering, comparison and arithmetic are wrong unless
  every one of those paths is rewritten; likely the most invasive.
- **(b) Scaled INTEGER** — store `value × 10^scale` as a 64-bit integer: exact, sorts and compares correctly, and
  sums correctly; bounded to ~19 significant digits, which `DECIMAL(22,6)` exceeds — needs a stated ceiling and a
  refusal past it.
- **(c) Document and refuse nothing** — accept float storage on SQLite as a known limit, and say so where the
  default provider is chosen. Only if (a) and (b) both measure as unworkable.

## Acceptance criteria

- [ ] The questions above answered with measurements against Microsoft.Data.Sqlite, recorded here
- [ ] A choice among (a)/(b)/(c), made by the owner on those measurements
- [ ] If (a) or (b): `10.10m + 0.20m` reads back `10.30m` through `Increment`, ordering and range predicates are
      correct, and a value past the representable range is refused rather than rounded
- [ ] `DetectDrift` on SQLite stays clean for a table the framework creates, and reports an old `REAL`/`NUMERIC`
      decimal column if the declaration changes
- [ ] CHANGELOG entry (storage change on the default provider) with the migration, or the documented limit for (c)

## Out of scope

- MySQL / SQL Server / PostgreSQL decimals — exact since TASK-512 (PostgreSQL always was)
- `double` / `float` properties — binary floats by declaration, correctly stored as such
