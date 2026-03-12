# Birko.Models

## Overview
Base models and extensions for the Birko Framework.

## Project Location
`C:\Source\Birko.Models\`

## Purpose
- Define base entity classes
- Provide common model properties
- Model extensions and utilities
- ViewModels base classes

## Components

### Models
- `Entity` - Base entity with Id
- `LogEntity` - Entity with logging support
- `DateEntity` - Entity with date tracking

### ViewModels
- `ViewModel` - Base view model
- `LogViewModel` - View model with logging

### Extensions
- Model extensions for common operations
- Conversion utilities

### ViewModels
- `PagedViewModel` - Pagination support
- `FilteredViewModel` - Filtering support

## Base Entity

```csharp
using Birko.Models.Models;

public class Product : Entity
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}

var product = new Product
{
    Id = Guid.NewGuid(), // From Entity
    Name = "Widget",
    Price = 19.99m
};
```

## Log Entity

```csharp
using Birko.Models.Models;

public class AuditedProduct : LogEntity
{
    public string Name { get; set; }
    // Includes: CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
}
```

## ViewModel

```csharp
using Birko.Models.ViewModels;

public class ProductViewModel : ViewModel
{
    public string Name { get; set; }
    public string PriceFormatted { get; set; }
}
```

## Dependencies
- .NET 10.0

## Specialized Models

Different domains have their own models:
- [Birko.Models.Product](../Birko.Models.Product/CLAUDE.md) - Product models
- [Birko.Models.Category](../Birko.Models.Category/CLAUDE.md) - Category models
- [Birko.Models.SEO](../Birko.Models.SEO/CLAUDE.md) - SEO models

## Best Practices

1. **Inheritance** - Always inherit from Entity for database entities
2. **Guid IDs** - Use Guid for entity IDs
3. **Immutability** - Consider immutable DTOs
4. **Validation** - Add data annotations for validation
5. **Namespaces** - Organize models by domain

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
