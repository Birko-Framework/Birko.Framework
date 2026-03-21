# Birko Framework

Modular .NET framework providing data access, communication, and model infrastructure for enterprise applications.

See also:
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration

## Architecture

### Store Hierarchy
```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

### SQL Stores
```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
  -> Settings (Location, Name)
    -> PasswordSettings (+Password)
      -> RemoteSettings (+UserName, +Port, +UseSecure)
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
    -> Birko.Data.Stores (store interfaces, imports Configuration)
      -> Birko.Data.Repositories

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)
```

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage

## Conventions
- All stores implement: IStore, IAsyncStore, IBulkStore, IAsyncBulkStore
- All repositories implement: IRepository, IAsyncRepository, IBulkRepository, IAsyncBulkRepository
- Use protected setters for properties that derived classes need to modify
- RemoteSettings should be passed via base.SetSettings(), not constructed inline

## Code Style
- **Guard clauses:** Use early returns instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... }`.
- **No nullable warnings:** All new code must compile without CS8600–CS8605, CS8618, CS8625. Use proper null checks, `!` only when provably safe, or `?` annotations.

## Important Notes

### Recent Updates

#### New Model Projects (2026-03-06)
Extracted reusable models from FisData.Stock:
- **Birko.Models.Accounting** — Currency, Tax, PriceGroup, MeasureUnit
- **Birko.Models.Customers** — Address, Customer, InvoiceAddress
- **Birko.Models.Users** — User, UserAgenda, Agenda
- **Birko.Models.Warehouse** — Item, ItemVariant, Repository, WareHouseDocument
- **Birko.Models** — Added AbstractPercentage, AbstractTree, ValueData

#### Recent Fixes (2026-03-05)
- Replaced `NativeAsyncDataBaseStore` with `AsyncDataBaseStore` in async stores/repos
- Fixed `AbstractAsyncStore.CreateAsync` return type: `Task` -> `Task<Guid>`
- Changed `Connector` property from `private set` to `protected set` in DataBaseStore/AsyncDataBaseStore
- Added parameterless constructor to `DataBaseRepository`
- Fixed PostgreSQL/MySQL stores settings handling

## Documentation
- [TODO.md](./TODO.md) — Planned features and roadmap
- [docs/](docs/) — Detailed documentation (architecture, store/repository guides, migrations, patterns, caching, validation, background jobs, message queue, event bus, event sourcing, storage, messaging, telemetry, security, rules, workflow, CQRS, health, processors, serialization, sync, time, localization, tenant, communication, dependencies, consumers)

Each project has its own CLAUDE.md at `../Birko.{ProjectName}/CLAUDE.md`.
