# Birko.Data.Localization.MongoDB

MongoDB indexes for [Birko.Data.Localization](../Birko.Data.Localization/README.md)'s translation rows.

## Features

- `LocalizationMongoIndexes.EnsureAsync(store)` creates, on the translation store's collection and only where missing:
  - a **unique key** over `(EntityGuid, EntityType, FieldName, Culture)`, as SQL's `EntityTranslationMapping` declares it;
  - an index over **`ValueHash`**, which lookups by a translated value select rows by. Without it such a lookup scans
    the collection (measured: `COLLSCAN`; with the index, `IXSCAN`).
- With the unique key, MongoDB refuses a duplicate key under **any** Guid: a row written around the upsert, or one
  stored with a random Guid before TASK-698. The store reports it as `StoreUniqueViolationException`, so the
  translation upsert's retry updates the other writer's row instead.

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

## Running tests

```
BIRKO_MONGO_HOST=localhost dotnet test tests/Birko.Data.Localization.MongoDB.Tests --nologo
```
