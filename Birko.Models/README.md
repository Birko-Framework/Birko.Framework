# Birko.Models

Base models, ViewModels, and extensions for the Birko Framework.

## Features

- Entity base classes (Entity, LogEntity, DateEntity)
- ViewModel base classes (ViewModel, LogViewModel, PagedViewModel, FilteredViewModel)
- Abstract model types (AbstractPercentage, AbstractTree, ValueData, SourceValue)
- Model extensions and utilities

## Installation

```bash
dotnet add package Birko.Models
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Models;

public class Product : LogEntity
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```

## API Reference

### Entities

- **Entity** - Base with `Id` (Guid)
- **LogEntity** - Adds `CreatedAt`, `UpdatedAt` timestamps
- **DateEntity** - Date-specific entity

### ViewModels

- **ViewModel** - Base ViewModel
- **ModelViewModel** - ViewModel with Guid
- **LogViewModel** - Adds timestamps (extends ModelViewModel)
- **AbstractLogViewModel** - Extends ViewModel directly (no Guid)

### Abstract Types

- **AbstractPercentage** - Percentage value model
- **AbstractTree** - Hierarchical tree model
- **ValueData** - Key-value data model
- **SourceValue** - Value with source tracking

## Related Projects

- [Birko.Models.Product](../Birko.Models.Product/) - Product models
- [Birko.Models.Category](../Birko.Models.Category/) - Category models
- [Birko.Models.Accounting](../Birko.Models.Accounting/) - Accounting models
- [Birko.Models.Customers](../Birko.Models.Customers/) - Customer models
- [Birko.Models.Users](../Birko.Models.Users/) - User models
- [Birko.Models.Warehouse](../Birko.Models.Warehouse/) - Warehouse models
- [Birko.Models.SEO](../Birko.Models.SEO/) - SEO models

## License

Part of the Birko Framework.
