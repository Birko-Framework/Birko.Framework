# Birko.Data.ViewModel

The **repository abstractions** for the Birko ViewModel pattern — sync/async and bulk base
repositories that separate presentation models (ViewModels) from data persistence models (Models),
with built-in change tracking.

> **Scope:** this project ships the repository base classes and interfaces **only**. The ViewModel
> base classes themselves (`ViewModel`, `ModelViewModel`, `AbstractLogViewModel`, `LogViewModel`,
> namespace `Birko.Data.ViewModels`) live in **Birko.Data.Core**. A repository's `TViewModel` need
> only implement `ILoadable<TModel>` — you can extend the Core base classes or roll your own type.

## Features

- Abstract sync/async ViewModel repositories with ViewModel↔Model mapping (`MapToModel` / `LoadInstance`)
- Change tracking (SHA-256 model hash) so a no-op update is skipped
- `ReadMode` guard — write methods throw `InvalidOperationException` when the repo is read-only
- Bulk operation support (`Create`/`Update`/`Delete` over `IEnumerable<TViewModel>` + filter-based overloads)

## Installation

```bash
dotnet add package Birko.Data.ViewModel
```

## Dependencies

- Birko.Data.Core (AbstractModel, ILoadable, and the `Birko.Data.ViewModels` base classes)
- Birko.Data.Stores (store interfaces)
- Birko.Data.Repositories (repository interfaces)
- Birko.Serialization (ISerializer for the change-tracking hash; defaults to SystemJsonSerializer)

## Usage

```csharp
// A ViewModel is any type implementing ILoadable<TModel>. The Birko.Data.Core base classes
// (Birko.Data.ViewModels.ModelViewModel, etc.) are a convenient starting point.
public class ProductViewModel : Birko.Data.ViewModels.ModelViewModel, ILoadable<Product>
{
    public string? Name { get; set; }
    public decimal Price { get; set; }

    public void LoadFrom(Product data) { Name = data.Name; Price = data.Price; }
}

// A concrete repository derives from an abstract base here and supplies MapToModel.
public class ProductRepository : AbstractAsyncViewModelRepository<ProductViewModel, Product>
{
    public ProductRepository(IAsyncStore<Product> store) : base(store) { }
    protected override void MapToModel(ProductViewModel src, Product dst)
    {
        dst.Name = src.Name; dst.Price = src.Price;
    }
}

// Use it via the repository API.
IAsyncViewModelRepository<ProductViewModel, Product> repository = new ProductRepository(store);
var vm = repository.CreateInstance();
vm.Name = "Widget";
await repository.CreateAsync(vm);
```

## API Reference

### Abstract repositories (this project)

- **AbstractViewModelRepository** / **AbstractAsyncViewModelRepository** — single-item sync/async bases
- **AbstractBulkViewModelRepository** / **AbstractAsyncBulkViewModelRepository** — add bulk operations

### Repository Interfaces

- **IViewModelRepository** / **IAsyncViewModelRepository** - Sync/async repositories
- **IBulkViewModelRepository** / **IAsyncBulkViewModelRepository** - Bulk operations

### ViewModel base classes (in Birko.Data.Core, namespace `Birko.Data.ViewModels`)

- **ViewModel** - `INotifyPropertyChanged` base (non-generic)
- **ModelViewModel** - adds a `Guid` and `LoadFrom` (non-generic)
- **AbstractLogViewModel** / **LogViewModel** - audit/log ViewModel bases

## Platform Implementations

- [Birko.Data.SQL.ViewModel](../Birko.Data.SQL.ViewModel/) - SQL databases
- [Birko.Data.MongoDB.ViewModel](../Birko.Data.MongoDB.ViewModel/) - MongoDB
- [Birko.Data.ElasticSearch.ViewModel](../Birko.Data.ElasticSearch.ViewModel/) - Elasticsearch
- [Birko.Data.JSON.ViewModel](../Birko.Data.JSON.ViewModel/) - JSON files
- [Birko.Data.RavenDB.ViewModel](../Birko.Data.RavenDB.ViewModel/) - RavenDB
- [Birko.Data.TimescaleDB.ViewModel](../Birko.Data.TimescaleDB.ViewModel/) - TimescaleDB
- [Birko.Data.InfluxDB.ViewModel](../Birko.Data.InfluxDB.ViewModel/) - InfluxDB

## Filter-Based Bulk Operations

ViewModel bulk repositories expose filter-based operations at the model level (not the view model level), delegating directly to the underlying bulk store:
- `Update(Expression<Func<TModel, bool>> filter, PropertyUpdate<TModel> updates)`
- `Update(Expression<Func<TModel, bool>> filter, Action<TModel> updateAction)`
- `Delete(Expression<Func<TModel, bool>> filter)`

## License

Part of the Birko Framework.
