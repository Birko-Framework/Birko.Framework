# Birko.Models.Product

## Overview
Product domain models for the Birko Framework.

## Project Location
`C:\Source\Birko.Models.Product\`

## Purpose
- Product entity models
- Product-related view models
- Product filters
- Product DTOs

## Components

### Models
- `Product` - Base product entity (implements `ICatalogItem`, `ISluggable` — slug auto-generated from `Name`)
- `ProductPartnerCode` - Partner code mapping
- `MeasureUnit` - Measure unit
- `UnitConversion` - Unit conversion

### ViewModels
- `ProductViewModel` - Product display model
- `ProductListViewModel` - Product list model

### Filters
- `ProductFilter` - Product search filter
- `ProductCategoryFilter` - Category filter

## Product Entity

```csharp
using Birko.Models.Product.Models;

public class Product : Entity
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string SKU { get; set; }
    public decimal Price { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableTo { get; set; }
}
```

## Product Variant

```csharp
public class ProductVariant : Entity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } // e.g., "Red", "Large"
    public string SKU { get; set; }
    public decimal Price { get; set; }
    public int StockLevel { get; set; }
}
```

## Product Filter

```csharp
using Birko.Models.Product.Filters;

var filter = new ProductFilter
{
    CategoryId = categoryId,
    SearchTerm = "widget",
    MinPrice = 10,
    MaxPrice = 100,
    IsActive = true,
    Page = 1,
    PageSize = 20
};
```

## Dependencies
- Birko.Models, Birko.Models.Contracts (ICatalogItem), Birko.Data.Patterns (ISluggable)

## Use Cases
- E-commerce platforms
- Inventory management
- Product catalogs
- Pricing systems

## Related Models
- [Birko.Models.Category](../Birko.Models.Category/CLAUDE.md) - Category models

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
