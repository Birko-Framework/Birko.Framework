# Birko.Data.Localization.RavenDB

RavenDB support for [Birko.Data.Localization](../Birko.Data.Localization/README.md): one transaction for an entity and
its translations.

## Features

- `RavenLocalizedWriteBoundary.For(entityStore, translationStore)` makes a localized write **one transaction** when both
  stores are on one RavenDB database (TASK-700), as `SqlLocalizedWriteBoundary` does on SQL. Both stores write into one
  session, saved as one cluster-wide transaction.

## Usage

```csharp
using Birko.Data.Localization.RavenDB;
using Birko.Data.RavenDB.Stores;

var products = new AsyncRavenDBStore<Product>(url, database);
var translations = new AsyncRavenDBStore<EntityTranslationModel>(url, database);
var boundary = RavenLocalizedWriteBoundary.For(products, translations);   // after the settings are set
var store = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product>(products, translations, context, boundary);
```

- **One database** means both document stores have the same URLs and database. Any other pairing is best effort
  (`requireAtomic: true` refuses it instead).
- A failed translation write **discards the entity write** with the session. Inside your own `RavenDbUnitOfWork` on that
  database the write joins its session; a failure then makes your `CommitAsync` throw `TransactionRollbackOnlyException`,
  so roll back.
- A store given a session with `SetTransactionContext` makes the write go into that session; you save it.
- **A race is retried once.** Another writer saving the same key after this write checked it makes the save fail with
  `ConcurrencyException` (measured: `ClusterTransactionConcurrencyException`). The whole write re-runs once in a new
  session, which updates the other writer's row.
- The sync wrapper uses a sync session of its own and does not join an async `RavenDbUnitOfWork`.
- Cluster-wide transactions allow no patching, attachments, counters or time-series inside them (see
  [Birko.Data.RavenDB](../Birko.Data.RavenDB/README.md)).

## Running tests

```
BIRKO_RAVEN_URL=http://localhost:8080 dotnet test tests/Birko.Data.Localization.RavenDB.Tests --nologo
```
