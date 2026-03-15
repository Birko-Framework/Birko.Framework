# Birko.Data.Stores

## Overview
Store abstractions for the Birko Framework. Contains store interfaces, abstract implementations, settings hierarchy, ordering, and service locator.

## Project Location
`C:\Source\Birko.Data.Stores\`

## Components

### Store Interfaces (`Birko.Data.Stores`)
- **IStore\<T\>** — Combines IBaseStore, ISettingsStore, ICountStore, IReadStore, ICreateStore, IUpdateStore, IDeleteStore
- **IAsyncStore\<T\>** — Async equivalent of IStore
- **IBulkStore\<T\>** — Extends IStore with bulk read/create/update/delete
- **IAsyncBulkStore\<T\>** — Async equivalent of IBulkStore
- **IStoreWrapper** — Interface for store decorators (caching, audit, etc.)
- **ITransactionalStore\<T, TContext\>** — Interface for stores participating in external transactions

### Abstract Implementations
- **AbstractStore\<T\>** — Base sync store with Create, Read, Update, Delete, Save, Count
- **AbstractAsyncStore\<T\>** — Base async store
- **AbstractBulkStore\<T\>** — Extends AbstractStore with bulk operations and ordering
- **AbstractAsyncBulkStore\<T\>** — Extends AbstractAsyncStore with bulk operations

### Settings (`Birko.Data.Stores`)
- **Settings** — Base settings (Location, Name), implements ISettings
- **PasswordSettings** — Extends Settings with Password
- **RemoteSettings** — Extends PasswordSettings with UserName, Port, UseSecure

### Utilities
- **OrderBy\<T\>** — Type-safe sorting specification with expression-based API
- **StoreLocator** — Thread-safe service locator for store instances
- **StoreExtensions** — Helper methods for unwrapping store decorators

## Dependencies
- **Birko.Data.Core** — Models (AbstractModel), Filters (ModelByGuid), ILoadable (used by Settings)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
