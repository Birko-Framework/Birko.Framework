---
id: TASK-499
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: []
findings: [FIELD-015]
pr: null
github-issue: null
jira-key: null
---

# StoreLocator hands out ElasticSearch, CosmosDB, InfluxDB, MongoDB and RavenDB stores unconfigured

## Context

Found on 2026-09-26 from the field, by the Affiliate consumer (`BirkoWorks/Affiliate`, its TASK-017). With
every other defect bypassed, both its importer and its web front end got ElasticSearch stores with no
connection.

`StoreLocator.GetStore<TStore, TSettings>(settings)` (`Birko.Data.Stores/StoreLocator.cs`) creates the store
parameterlessly. It passes the settings on **only if** the store `is ISettingsStore<ISettings>`.
`ISettingsStore<T>` is invariant, and ten stores implement only `ISettingsStore<their own Settings>`. For
those the check is false, `SetSettings` is never called, and the caller gets a store with no client:

| Project | Stores |
|---|---|
| Birko.Data.ElasticSearch | `ElasticSearchStore<T>`, `AsyncElasticSearchStore<T>` |
| Birko.Data.CosmosDB | `CosmosDBStore<T>`, `AsyncCosmosDBStore<T>` |
| Birko.Data.InfluxDB | `InfluxDBStore<T>`, `AsyncInfluxDBStore<T>` |
| Birko.Data.MongoDB | `MongoDBStore<T>`, `AsyncMongoDBStore<T>` |
| Birko.Data.RavenDB | `RavenDBStore<T>`, `AsyncRavenDBStore<T>` |

All ten already have a public `SetSettings(Birko.Configuration.ISettings)` that type-checks and forwards to
the typed overload. The interface declaration is the only thing missing. The JSON and XML stores are not
affected: they inherit `ISettingsStore<ISettings>` from `JsonStore<T>` / `XmlStore<T>` / `AsyncXmlStore<T>`.

For ElasticSearch the gap dates from `5c4a0e46` (2026-03-05), when the declaration was dropped.

## Acceptance criteria

- [x] All ten stores are assignable to `ISettingsStore<ISettings>`, asserted per project in
      `StoreLocatorSettingsContractTests` (ElasticSearch, CosmosDB, InfluxDB, MongoDB, RavenDB).
- [x] A store obtained through `StoreLocator.GetStore<ElasticSearchStore<T>, ISettings>(settings)` has a
      client (`Connector` is not null). This behavioural check needs no server.
- [x] Every new test fails on the unfixed code (11 of 11 red) and passes with the fix.
- [x] The five projects' full suites stay green: ElasticSearch 165, CosmosDB 63, InfluxDB 28, MongoDB 99,
      RavenDB 69 — 0 failed.

## Behaviour change for consumers

`StoreLocator.GetStore` now **configures** these stores. Before, it returned them unconfigured. A consumer
that worked around the gap by calling `SetSettings` itself after `GetStore` now configures the store twice.
The InfluxDB stores dispose the previous client on a repeated `SetSettings` (CR-H049). The other stores
replace their client. Such workarounds can now be removed.

## Out of scope

- Making `ISettingsStore<T>` contravariant, or having `StoreLocator` discover `ISettingsStore<TSettings>`
  by reflection. Either would cover a future store that forgets the declaration, but it changes a shared
  interface's variance for every implementer. It is a decision, not a fix.

## Human test plan

N/A: covered by the automated contract tests. The consumer-side check (a real import against a live
ElasticSearch) is Affiliate TASK-017's human test plan.

## Progress log

- 2026-09-26 — Red: 11 new tests failed on `main` (`c728e285`): 10 contract theory cases plus the ES locator fact (`Connector` null).
- 2026-09-26 — Fix: `ISettingsStore<Birko.Configuration.ISettings>` added to the ten declarations. The new tests and the five full suites (424 tests) pass.
