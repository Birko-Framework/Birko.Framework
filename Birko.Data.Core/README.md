# Birko.Data.Core

Core data layer foundation for the Birko Framework. Contains base models, view models, filters, and exceptions that all data projects depend on.

## Components

### Models
- `AbstractModel` — Base class for all data entities with nullable `Guid` property
- `AbstractLogModel` — Extends AbstractModel with `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt` audit timestamps
- `ICopyable<T>` — Interface for cloning objects
- `IDefault` — Interface for marking default items
- `ILoadable<T>` — Generic interface for loading/mapping from another object

### ViewModels
- `ViewModel` — Base class with `INotifyPropertyChanged` support
- `ModelViewModel` — ViewModel with `Guid` property, maps to `AbstractModel`
- `LogViewModel` — Extends ModelViewModel with audit timestamps
- `AbstractLogViewModel` — ViewModel with audit timestamps but no Guid

### Filters
- `IRepositoryFilter<TModel>` — Generic filter interface for composable query predicates
- `ModelByGuid<TModel>` — Filter for single GUID lookup
- `ModelsByGuid<TModel>` — Filter for multiple GUID lookup

### Exceptions
- `StoreException` — Custom exception for store operations

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
```

## License

MIT License - see [License.md](License.md)
