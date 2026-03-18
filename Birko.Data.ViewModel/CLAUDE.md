# Birko.Data.ViewModel

ViewModel support for the Birko data layer.

## Overview

This project provides ViewModel pattern implementation for the Birko data layer, enabling separation between presentation models (ViewModels) and data persistence models (Models).

## Features

- **ViewModel Base Classes**: `ViewModel<T>`, `ModelViewModel<TViewModel, TModel>`
- **ViewModel Repositories**: Abstract and interface definitions for ViewModel-based data access
- **Change Tracking**: Built-in support for detecting model changes
- **Async Support**: Full async/await pattern support

## Dependencies

- **Birko.Data.Core** - Models (AbstractModel, ILoadable, ICopyable)
- **Birko.Data.Stores** - Store interfaces and settings
- **Birko.Data.Repositories** - Repository interfaces and abstractions
- **Birko.Serialization** — ISerializer for ViewModel hash calculation (optional, defaults to SystemJsonSerializer)

## Usage

```csharp
// Define your ViewModel
public class ProductViewModel : ModelViewModel<ProductViewModel, Product>
{
    public Guid? Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    // Override Load methods for conversion
}

// Use ViewModel Repository
IViewModelRepository<ProductViewModel, Product> repository;
var viewModel = await repository.LoadAsync(productId);
viewModel.Name = "Updated Name";
await repository.SaveAsync(viewModel);
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
├── ViewModels/
│   ├── ViewModel.cs                    - Base ViewModel class
│   ├── ModelViewModel.cs               - ViewModel with Model mapping
│   ├── AbstractLogViewModel.cs         - Base for audit/log ViewModels
│   └── LogViewModel.cs                 - Concrete log ViewModel
└── Repositories/
    ├── IViewModelRepository.cs         - Sync repository interface
    ├── IAsyncViewModelRepository.cs    - Async repository interface
    ├── IBulkViewModelRepository.cs     - Bulk operations interface
    ├── IAsyncBulkViewModelRepository.cs - Async bulk operations
    └── Abstract implementations...
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
