# Birko.Data.Repositories

## Overview
Repository abstractions for the Birko Framework. Contains repository interfaces, abstract implementations, service locator, and DI extensions.

## Project Location
`Birko.Data.Repositories/`

## Components

### Repository Interfaces (`Birko.Data.Repositories`)
- **IBaseRepository** / **IAsyncBaseRepository** — Base with Destroy
- **IRepository\<T\>** — Combines ICountRepository, IReadRepository, ICreateRepository, IUpdateRepository, IDeleteRepository
- **IAsyncRepository\<T\>** — Async equivalent
- **IBulkRepository\<T\>** — Extends IRepository with bulk operations and ordering
- **IAsyncBulkRepository\<T\>** — Async equivalent

### Abstract Implementations
- **AbstractRepository\<T\>** — Base sync repository, delegates to IStore\<T\>
- **AbstractAsyncRepository\<T\>** — Base async repository, delegates to IAsyncStore\<T\>
- **AbstractBulkRepository\<T\>** — Extends AbstractRepository with bulk operations via IBulkStore\<T\>, including filter-based Update/Delete and PropertyUpdate
- **AbstractAsyncBulkRepository\<T\>** — Extends AbstractAsyncRepository with bulk operations, including filter-based Update/Delete and PropertyUpdate

### Utilities
- **RepositoryLocator** — Thread-safe service locator for repository instances
- **ServiceCollectionExtensions** — DI registration helpers (AddRepository, AddRepositorySingleton, etc.)

## Dependencies
- **Birko.Data.Core** — Models (AbstractModel), Filters (IFilter)
- **Birko.Data.Stores** — Store interfaces (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore), OrderBy, Settings

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
