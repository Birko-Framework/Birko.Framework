# Birko.Data.ViewModel

ViewModel support for the Birko data layer.

## Overview

This project provides the **repository abstractions** for the Birko ViewModel pattern, enabling
separation between presentation models (ViewModels) and data persistence models (Models). It ships
the abstract repositories + interfaces **only** — the ViewModel base classes themselves live in
`Birko.Data.Core` (namespace `Birko.Data.ViewModels`).

## Features

- **ViewModel Repositories**: abstract sync/async (+ bulk) repositories and their interfaces
- **ViewModel↔Model mapping**: `MapToModel` (abstract) + `LoadInstance` / `LoadModelInstance`
- **Change Tracking**: SHA-256 model hash so a no-op update is skipped
- **ReadMode guard**: write methods throw `InvalidOperationException` when the repository is read-only (CR-L239)
- **Async Support**: full async/await pattern support

> The `ViewModel`, `ModelViewModel`, `AbstractLogViewModel`, `LogViewModel` base classes are **not**
> in this project — they live in `Birko.Data.Core` (`Birko.Data.ViewModels`) and are non-generic.

## Dependencies

- **Birko.Data.Core** - Models (AbstractModel, ILoadable, ICopyable) and the `Birko.Data.ViewModels` base classes
- **Birko.Data.Stores** - Store interfaces and settings
- **Birko.Data.Repositories** - Repository interfaces and abstractions
- **Birko.Serialization** — ISerializer for ViewModel hash calculation (optional, defaults to SystemJsonSerializer)

## Usage

```csharp
// A ViewModel is any type implementing ILoadable<TModel>; the Core base classes are a convenient start.
public class ProductViewModel : Birko.Data.ViewModels.ModelViewModel, ILoadable<Product>
{
    public string? Name { get; set; }
    public void LoadFrom(Product data) => Name = data.Name;
}

// A concrete repository derives from an abstract base here and supplies MapToModel.
public class ProductRepository : AbstractAsyncViewModelRepository<ProductViewModel, Product>
{
    public ProductRepository(IAsyncStore<Product> store) : base(store) { }
    protected override void MapToModel(ProductViewModel src, Product dst) => dst.Name = src.Name;
}

// Use it via the repository API (CreateAsync / ReadAsync / UpdateAsync / DeleteAsync).
IAsyncViewModelRepository<ProductViewModel, Product> repository = new ProductRepository(store);
var vm = repository.CreateInstance();
vm.Name = "Widget";
await repository.CreateAsync(vm);
```

## Platform Implementations

Platform-specific ViewModel repositories are available in:
- `Birko.Data.SQL.ViewModel` - SQL databases
- `Birko.Data.MongoDB.ViewModel` - MongoDB
- `Birko.Data.ElasticSearch.ViewModel` - ElasticSearch
- `Birko.Data.JSON.ViewModel` - JSON file storage
- `Birko.Data.RavenDB.ViewModel` - RavenDB
- `Birko.Data.TimescaleDB.ViewModel` - TimescaleDB
- `Birko.Data.InfluxDB.ViewModel` - InfluxDB

## Architecture

```
Birko.Data.ViewModel/
└── Repositories/                          (the only compiled folder — 8 files)
    ├── IViewModelRepository.cs            - Sync repository interface
    ├── IAsyncViewModelRepository.cs       - Async repository interface
    ├── IBulkViewModelRepository.cs        - Bulk operations interface
    ├── IAsyncBulkViewModelRepository.cs   - Async bulk operations interface
    ├── AbstractViewModelRepository.cs     - Sync single-item base
    ├── AbstractAsyncViewModelRepository.cs - Async single-item base
    ├── AbstractBulkViewModelRepository.cs - Sync bulk base
    └── AbstractAsyncBulkViewModelRepository.cs - Async bulk base

(The ViewModel base classes — ViewModel / ModelViewModel / AbstractLogViewModel / LogViewModel —
 live in Birko.Data.Core under the Birko.Data.ViewModels namespace, NOT here.)
```

## Notes

- This project only contains abstractions and base classes
- Concrete implementations are in platform-specific packages
- Reference only if your application uses the ViewModel pattern
- For direct Model access, use Birko.Data.Repositories directly

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
