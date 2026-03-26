# Birko.Data.ViewModel

ViewModel pattern implementation for the Birko Framework, enabling separation between presentation models and data persistence models.

## Features

- ViewModel base classes with Model mapping
- Change tracking for detecting modifications
- Sync and async repository interfaces
- Bulk operation support
- Log/audit ViewModel support

## Installation

```bash
dotnet add package Birko.Data.ViewModel
```

## Dependencies

- Birko.Data.Core (AbstractModel, ViewModels)
- Birko.Data.Stores (store interfaces)
- Birko.Data.Repositories (repository interfaces)

## Usage

```csharp
// Define a ViewModel
public class ProductViewModel : ModelViewModel<ProductViewModel, Product>
{
    public Guid? Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// Use ViewModel Repository
IViewModelRepository<ProductViewModel, Product> repository;
var viewModel = await repository.LoadAsync(productId);
viewModel.Name = "Updated Name";
await repository.SaveAsync(viewModel);
```

## API Reference

### ViewModels

- **ViewModel\<T\>** - Base ViewModel class
- **ModelViewModel\<TViewModel, TModel\>** - ViewModel with Model mapping
- **AbstractLogViewModel** - Base for audit/log ViewModels (no Guid)
- **LogViewModel** - Concrete log ViewModel with Guid and timestamps

### Repository Interfaces

- **IViewModelRepository** / **IAsyncViewModelRepository** - Sync/async repositories
- **IBulkViewModelRepository** / **IAsyncBulkViewModelRepository** - Bulk operations

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
