---
id: TASK-826
parent: null
feature: null
status: todo
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
natively (half-to-even on the SQL dialects, away-from-zero on MongoDB), and a provider that cannot express one
refuses it.

## Acceptance criteria

- [ ] A new `PropertyUpdate<T>` case: set a property from an expression over the same row's properties, constants
      and captured values. Supported nodes are decided at implementation and kept to what every native provider can
      express exactly: `+ - * /`, a decimal constant, `Math.Round(v, d, MidpointRounding)`. Anything else is refused
      at `Set` time, never dropped or evaluated client-side quietly (rules 41/42).
- [ ] Every translator handles it or refuses it explicitly. None of them silently does a read-modify-write
      unless that is already its documented fallback (`ApplyTo`), and that fallback evaluates the same expression in C#.
- [ ] Decorators see the new case: tenant and soft-delete scoping, `UpdatedAt` / `UpdatedBy` stamping, the
      localization and event-sourcing wrappers (an event-sourced update records the computed value or refuses).
- [ ] SQLite computes exactly: decimal multiply, divide and round functions beside the existing add / sum / avg.
- [ ] A cross-provider contract at a `.xx5` midpoint for **both** rounding modes: the stored value equals C#'s
      `Math.Round` on the same inputs, on every provider (SQLite, InMemory, JSON, XML, and live PostgreSQL, SQL
      Server, MySQL, MongoDB; Cosmos, RavenDB, Elasticsearch, InfluxDB as far as they run). A division whose exact
      result is not representable (e.g. `/ 1.2`) is in the contract too.
- [ ] Atomicity: one statement per native provider (asserted the way TASK-498 asserted it for `Increment`).

## Human test plan

N/A — a library API with no UI; every criterion is asserted by automated tests, including the live provider
twins on CI's `live-tests` job. Symbio TASK-1063 is the in-app check.

## Size

Large: one new case across about a dozen translators and the decorators, new SQLite decimal functions, and a
live cross-provider contract. Comparable to TASK-498 plus the expression translator and the rounding emulation.

## Related

TASK-498 (the `Match` hierarchy this extends), TASK-515 (SQLite ignores declared decimal precision/scale, which
bears on what "to the cent" means there), TASK-217 (the partial-update SET list).
