# Birko.Data.Localization.MongoDB

MongoDB support for [Birko.Data.Localization](../Birko.Data.Localization/README.md): the translation rows' indexes, and
one transaction for an entity and its translations.

## Features

- `LocalizationMongoIndexes.EnsureAsync(store)` creates, on the translation store's collection and only where missing:
  - a **unique key** over `(EntityGuid, EntityType, FieldName, Culture)`, as SQL's `EntityTranslationMapping` declares it;
  - an index over **`ValueHash`**, which lookups by a translated value select rows by. Without it such a lookup scans
    the collection (measured: `COLLSCAN`; with the index, `IXSCAN`).
- With the unique key, MongoDB refuses a duplicate key under **any** Guid: a row written around the upsert, or one
  stored with a random Guid before TASK-698. The store reports it as `StoreUniqueViolationException`, so the
  translation upsert's retry updates the other writer's row instead.
- `MongoLocalizedWriteBoundary.For(entityStore, translationStore)` makes a localized write **one transaction** when both
  stores are on one MongoDB database (TASK-700), as `SqlLocalizedWriteBoundary` does on SQL. Needs a replica set.

## Usage

```csharp
using Birko.Data.Localization.MongoDB;
using Birko.Data.MongoDB.Stores;

var translations = new AsyncMongoDBStore<EntityTranslationModel>();
translations.SetSettings(settings);
await LocalizationMongoIndexes.EnsureAsync(translations);   // once, at startup
```

The collection is the one the store uses: the translation model's type name. A collection that already holds a
duplicate key cannot get the unique index: `EnsureAsync` throws `IndexManagementException`. Remove the duplicates,
keeping the newest row of each key (`UpdatedAt`), and call it again.

## One transaction for the entity and its translations

```csharp
var products = new AsyncMongoDBStore<Product>();
products.SetSettings(settings);
var boundary = MongoLocalizedWriteBoundary.For(products, translations);   // after SetSettings
var store = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product>(products, translations, context, boundary);
```

- **One database** means both stores' settings have the same `GetId()`. Any other pairing is best effort
  (`requireAtomic: true` refuses it instead).
- A failed translation write **rolls the entity write back**. Inside your own `MongoDbUnitOfWork` on that database the
  write joins it; a failure then makes your `CommitAsync` throw `TransactionRollbackOnlyException`, so roll back.
- A store given a session with `SetTransactionContext` makes the write run in that session.
- **A race is retried once.** Inside a transaction another writer taking the same key is a write conflict (MongoDB's
  `TransientTransactionError`), even when it committed first: the transaction reads a snapshot. The whole write is
  aborted and re-run once in a new transaction, which updates the other writer's row. Measured live.
- **Needs a replica set** or a sharded cluster: a standalone `mongod` refuses every write in a transaction.
- Create the collections (`InitAsync`) and indexes before the first write.

## Running tests

```
BIRKO_MONGO_HOST=localhost dotnet test tests/Birko.Data.Localization.MongoDB.Tests --nologo
```
