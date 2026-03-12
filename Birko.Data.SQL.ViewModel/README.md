# Birko.Data.SQL.ViewModel

ViewModel repository implementations for SQL databases in the Birko Framework.

## Features

- Sync and async ViewModel repositories for SQL databases
- Works with all SQL connectors (MSSQL, PostgreSQL, MySQL, SQLite, TimescaleDB)
- Multi-tenancy integration via Birko.Data.Tenant

## Installation

```bash
dotnet add package Birko.Data.SQL.ViewModel
```

## Dependencies

- Birko.Data
- Birko.Data.ViewModel
- Birko.Data.SQL

## Usage

```csharp
// Sync repository
var repo = new DataBaseRepository<MSSqlConnector, ProductViewModel, Product>();
var viewModel = repo.Load(productId);

// Async repository
var asyncRepo = new AsyncDataBaseRepository<ProductViewModel, Product>();
var viewModel = await asyncRepo.LoadAsync(productId);
```

## API Reference

### Interfaces

- **IDataBaseRepository\<TConnector, TViewModel, TModel\>** - SQL ViewModel repository interface

### Classes

- **DataBaseRepository\<TConnector, TViewModel, TModel\>** - Sync ViewModel repository
- **AsyncDataBaseRepository\<TViewModel, TModel\>** - Async ViewModel repository

## Related Projects

- [Birko.Data.ViewModel](../Birko.Data.ViewModel/) - Base ViewModel abstractions
- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes

## License

Part of the Birko Framework.
