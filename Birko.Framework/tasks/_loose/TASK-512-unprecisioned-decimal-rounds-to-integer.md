---
id: TASK-512
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

# An unprecisioned `decimal` is stored as an integer on MySQL and SQL Server — fractions are rounded away silently

## Context

Spawned from TASK-511, which fixed only the drift *report*. `ConvertType` emits a bare `DECIMAL` for a `decimal`
property with no `[PrecisionField]` + `[ScaleField]` (or `HasPrecision(..).HasScale(..)`), and the server fills in
its own default — **scale 0** on both:

| Provider | Stored as | `INSERT 7.5` reads back (measured 2026-10-03) |
|---|---|---|
| MySQL 8.4 | `decimal(10,0)` | `8` |
| SQL Server 2022 | `DECIMAL(18,0)` | `8` |
| PostgreSQL 16 | `numeric` (unbounded) | lossless |
| SQLite | `REAL` | float drift (TASK-498: `10.10 + 0.20` → `10.299999999999999`) |

No error, no warning: a price of `9.99` is stored as `10`. A `decimal` declared with precision but **no scale**
also takes this path — `ConvertType` requires both before it renders `DECIMAL(p,s)`.

**Framework blast radius, measured 2026-10-03:** none of the framework's own SQL tables. Every `decimal` that
`Birko.Models.*.SQL` maps carries `HasPrecision(22).HasScale(6)` (the canonical pair,
`ValueData.StoreDecimalPrecision` / `StoreDecimalPlaces`); the unmapped ones (`PriceListEntry`, `CurrencyRate`,
`Discount`) have no SQL mapping at all. **Consumer exposure is unmeasured** — any consumer entity with a plain
`public decimal X` on MySQL or SQL Server.

⚠ The comments in `StockBalanceMapping` and `StockMovementMapping` say an unmapped decimal "takes the provider's
default — 18,2 on several". Measured, it is **scale 0** on both servers that default at all. The comments
understate the loss and should be corrected with this task.

## Acceptance criteria

- [x] Decide the declaration for an unprecisioned decimal, on measurement — the two candidates:
      (a) emit the canonical `DECIMAL(22,6)` on every provider that needs a precision; or
      (b) refuse an unprecisioned decimal at table load, naming the property (rule 41: a mapper that cannot
      express something refuses) — breaks every consumer that has one, loudly
  — **(a), chosen by the owner 2026-10-03.** `AbstractConnectorBase.BoundedDecimalType` (MySQL, SQL Server):
  the missing half takes 22 / 6, so a lone `[PrecisionField]`/`[ScaleField]` is now honoured instead of ignored
  (rule 55); scale > precision after defaulting is a `FieldAttributeException` naming the property. Measured
  first: precision-only decimals exist once across all consumers (Birko.Sandbox). PostgreSQL / SQLite unchanged
  — lossless / `REAL` either way, and bounding them would add a ceiling and a drift report for nothing.
  Consumer exposure measured: Symbio ~240 unprecisioned decimals in `[Table]` entities (money, tax, GPS,
  quantities) — default SQLite, exposed on modules configured for MySQL/SQL Server; Symbio is in dev, so its
  tables can be recreated. FisData is being retired into Symbio.
- [x] Existing tables are not altered silently; after the change `DetectDrift` reports their bare columns as
      drift (correct: they are lossy), and the CHANGELOG says how to migrate one
  — `A_table_created_before_TASK512_reports_its_integer_decimal_as_drift` (both providers); the migration
  statements in CHANGELOG.md are proven by `The_documented_migration_widens_an_old_column` (drift clears, a
  fraction survives, the old row stays `8`).
- [x] TASK-511's `DeclaredAsStored` overrides are then unreachable for a bare `DECIMAL` — delete them with their
      tests inverted, not left as a second implementation (rule 53)
  — hook, both overrides and the call site deleted; TASK-511's "bare column reports clean" test inverted into the
  drift test above, its guard test replaced by it.
- [x] Correct the `18,2` comments in the two Inventory mappings
- [x] Live tests: `7.5` round-trips on MySQL and SQL Server for an unprecisioned property
  — `An_unprecisioned_decimal_keeps_its_fraction` (`7.5` and `1234.567891`), plus offline
  `UnprecisionedDecimalColumnTypeTests` (6 each) and `DefaultDecimalPrecisionTests` pinning 22/6 to `ValueData`.
  Mutation: emitting the bare `DECIMAL` again fails the round trip, the inverted drift test and the migration
  test on both providers.
- [x] (found by the regression run) The migrations factory required BOTH halves before building a decimal
  field, so `Precision = 18` alone produced `DECIMAL(22,6)` — the declaration dropped. `SchemaField.For` now
  builds one for either half; TASK-264's two tests pinning the bare result were inverted (rule 56).

## Out of scope

- The drift-report false positive — fixed in TASK-511
