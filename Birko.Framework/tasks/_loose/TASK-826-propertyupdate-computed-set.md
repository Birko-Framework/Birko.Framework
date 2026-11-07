---
id: TASK-826
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `PropertyUpdate<T>` can only set a constant or a delta: add a computed set from the same row

Requested by consumer Symbio (symbio-32, on the user's word) for Symbio TASK-1063. The ordering against
TASK-1063 (wait for this, or ship an interim) is the user's call and is not decided.

## Need

```csharp
update.Set(x => x.PriceWithoutTax, x => Math.Round(x.PriceWithTax / k, 2, MidpointRounding.AwayFromZero));
```

A column written from other columns **of the same row**, in **one** native statement, through the decorator
chain (tenant, soft-delete, `UpdatedAt` / `UpdatedBy`), exactly as `Set` / `Increment` reach the store today via
`UpdateAsync(filter, PropertyUpdate<T>)`.

First consumer: when a tax rate's percentage changes, every `BasePrice` with that `TaxRateGuid` gets
`TaxRatePercent = r` (a constant) and one price column recomputed from the other, per the tenant's price mode:
`net = round(gross / (1 + r), 2)` or `gross = round(net * (1 + r), 2)`. A read-modify-write of whole rows is not
acceptable (it loses concurrent edits), which is the same reason TASK-498 added `Increment`.

## Context (surveyed by Symbio, cited lines confirmed 2026-11-07)

- `Birko.Data.Stores/PropertyUpdate.cs`: only `Set` (constant, :53), `Increment` (:90), `Decrement` (:127),
  a closed hierarchy reached through `Match` (TASK-498: a new kind is a new
  case, and every translator must handle it or refuse it). TASK-498 left multiply etc. out of scope "until someone
  needs one"; this is that someone.
- The connector's expression `Update` (`Birko.Data.SQL/SQL/Connectors/AbstractConnector_Update.cs:48-94`;
  `ParseExpression` handles `+ - * / %` at `DataBase.cs:201-215`) bypasses every decorator and has no `Math.Round`.
- SQLite stores decimals as `TEXT COLLATE BIRKO_DECIMAL`; only add / sum / avg have exact functions
  (`Birko.Data.SQL.SqLite/Database/Connectors/SqLiteDecimal.cs`), so a plain `/` or `*` there computes in floating
  point. Exact multiply, divide and round functions are needed.
- Translators that must take the new case: SQL (`AsyncDataBaseBulkStore`, `DataBaseBulkStore` → connectors),
  MongoDB (`MongoPropertyUpdateTranslator`, needs an update pipeline, not `$set`), Elasticsearch
  (`ElasticSearchStoreHelper`, painless), the reflection fallback `PropertyUpdate.ApplyTo` (JSON, XML, InMemory,
  Cosmos, RavenDB, InfluxDB), and the decorators that inspect assignments (`Birko.Data.Patterns/Decorators/*`,
  `Birko.Data.Localization` `LocalizedPropertyUpdateHelper`, `Birko.Data.EventSourcing` wrappers).

## The trap: rounding mode

C# `Math.Round(x, 2)` defaults to **half to even**. SQL Server, PostgreSQL (`numeric`) and MySQL (exact values)
`ROUND` are **half away from zero**. MongoDB `$round` is **half to even**. So one expression gives different cents
on different backends at a `.xx5` midpoint. The value a native computed set writes must equal what C# computes on a
normal save, to the cent, on every provider.

Proposed: the expression must name the rounding explicitly (`Math.Round(v, d, MidpointRounding)`); a `Math.Round`
without it is refused, not defaulted. Each provider translates the mode it was given, emulating the one it lacks
natively (half-to-even on the SQL dialects, away-from-zero on MongoDB). **Every provider supports both modes;
none may refuse them** (the user's decision, 2026-11-07: the request is that all providers have it).

## Acceptance criteria

- [x] A new `PropertyUpdate<T>` case: set a property from an expression over the same row's properties, constants
      and captured values. Supported nodes are decided at implementation and kept to what every native provider can
      express exactly: `+ - * /`, a decimal constant, `Math.Round(v, d, MidpointRounding)`. Anything else is refused
      at `Set` time, never dropped or evaluated client-side quietly (rules 41/42).
- [x] **Every provider supports it** (the user's decision, 2026-11-07): SQLite, PostgreSQL, SQL Server, MySQL,
      TimescaleDB, MongoDB, Elasticsearch, Cosmos DB, RavenDB, InfluxDB, InMemory, JSON, XML. No provider refuses the
      case. Native providers write in one statement; providers on the `ApplyTo` fallback evaluate the same expression
      in C# and say in their docs whether that path is atomic (a concurrency gap found there is filed, not hidden).
- [x] Decorators see the new case: tenant and soft-delete scoping, `UpdatedAt` / `UpdatedBy` stamping, the
      localization and event-sourcing wrappers (an event-sourced update records the computed value or refuses).
- [x] SQLite computes exactly: decimal multiply, divide and round functions beside the existing add / sum / avg.
- [x] A cross-provider contract at a `.xx5` midpoint for **both** rounding modes: the stored value equals C#'s
      `Math.Round` on the same inputs, on every provider (SQLite, InMemory, JSON, XML, and live PostgreSQL, SQL
      Server, MySQL, TimescaleDB, MongoDB, Cosmos DB, RavenDB, Elasticsearch, InfluxDB; every one of them, none skipped). A division whose exact
      result is not representable (e.g. `/ 1.2`) is in the contract too.
- [x] Atomicity: one statement per native provider (asserted the way TASK-498 asserted it for `Increment`).

## Human test plan

N/A — a library API with no UI; every criterion is asserted by automated tests, including the live provider
twins on CI's `live-tests` job. Symbio TASK-1063 is the in-app check.

## Size

Large: one new case across about a dozen translators and the decorators, new SQLite decimal functions, and a
live cross-provider contract. Comparable to TASK-498 plus the expression translator and the rounding emulation.

## Related

TASK-498 (the `Match` hierarchy this extends), TASK-515 (SQLite ignores declared decimal precision/scale, which
bears on what "to the cent" means there), TASK-217 (the partial-update SET list).

## Outcome (2026-11-07)

`PropertyUpdate<T>.Compute(target, formula)` for a `decimal` / `decimal?` target. The formula becomes a closed
hierarchy, `ComputedValue` (column, constant, `+ - * /`, round), reached through `Match`. `PropertyAssignment.Match`
takes a third `computed` branch. **Breaking** for any caller of the two-branch form; none exists outside the
framework (Consumers grepped).

- **Accepted:** non-nullable `decimal` properties of the row, `decimal` constants and captured values (evaluated once,
  when added), unary minus, and `Math.Round` / `decimal.Round` with an explicit `ToEven` or `AwayFromZero`.
- **Refused when added:** a round without a mode, directed modes, row-dependent digits, nullable or non-decimal
  operands, nested members, `%` and any other call. Also refused: a formula reading a column the same update assigns,
  in either order of the calls, because MySQL evaluates a SET list left to right and the others against the old row.

Per provider:

- **SQL:** connector hooks `ComputedArithmetic`, `ComputedDivide`, `ComputedRound` and `ComputedTruncate`.
  - Half-to-even is emulated around `ROUND`.
  - Each dialect widens the quotient's scale: PostgreSQL `NUMERIC(60,28)`, MySQL `DECIMAL(65,26)`, SQL Server
    `DECIMAL(38,20) / DECIMAL(28,8)`, with the value narrowed to `DECIMAL(32,12)` before the midpoint test.
  - SQLite gets exact `birko_decimal_sub/mul/div/round` functions in C#.
- **MongoDB:** one pipeline update. Away-from-zero is emulated around `$round`, which rounds half to even.
- **Elasticsearch:** Painless `BigDecimal` with `DECIMAL128` division and `setScale(HALF_EVEN | HALF_UP)`.
- **Others:** `ApplyTo` evaluates the formula in C#, read-modify-save. On InfluxDB a decimal is stored as a double, as
  for every write there.
- **Localization:** the wrapper refuses a computed set on its read-modify-save replay, as it refuses an increment.
- **Event sourcing:** records the computed value in the Updated event.

### Measured

- **Cross-provider contract** (`tests/Shared/StoreContract/ComputedSetContract.cs`), sync and async, 9 tests each:
  - Covers a divide and a multiply landing on half-cent midpoints (one negative), an inexact `100 / 1.2`, both modes,
    a constant set beside the formula, and filter scoping.
  - Green on all 13 providers, the live ones against local containers: SQLite, PostgreSQL, TimescaleDB, SQL Server,
    MySQL, MongoDB, Elasticsearch, Cosmos DB, RavenDB, InMemory, JSON and XML (×3 stores). InfluxDB runs against
    stand-in query and write APIs.
- **Red verification**, each mutation restored:
  - SQL half-to-even emulation off: 5 of 9 red on PostgreSQL, SQL Server, MySQL and TimescaleDB.
  - SQLite on its native operators: 3 red, including the 20-digit exactness test.
  - MongoDB away-from-zero via `$round`: 4 red.
  - Elasticsearch `HALF_UP` swapped to `HALF_EVEN`: 4 red.
  - Fallback ignoring the mode: red on InMemory (4), core (1) and InfluxDB (2).
- **Symbio's shape** (`Task826ComputedSetChainTests`): tenant → soft-delete → timestamp over SQLite. One `UPDATE`;
  only the current tenant's live rows change; `UpdatedAt` is stamped.
- **Regression:** 124 importer suites, live. 122 green. The other two:
  - RavenDB failed 1 contract test under load: index staleness. The contract now waits for indexes, and the full
    suite passed 3 of 3. The store gap is filed as [[TASK-828]].
  - PostgreSQL `DeadlockRetryLiveTests.With_retry_off…` fails on a clean HEAD as well, against the local container.
    It is pre-existing, and CI passed it on `64433632`.

### Not covered

- Atomicity was asserted by statement count on SQLite only. The other SQL dialects share the translator, and
  MongoDB and Elasticsearch send one request by construction.
- `UpdatedBy` (audit) was not tested. Its `Set` is added the same way as the timestamp's.
- The dialects' division scale casts are a precaution: no test fails without them (by arithmetic, not measured: two-decimal money
  over a divisor of up to four digits stays clear of a midpoint at MySQL's default scale).

RavenDB server-side patch: [[TASK-827]].
