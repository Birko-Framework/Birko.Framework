# Birko.Models.Category

## Overview
Category domain models for the Birko Framework.

## Project Location
`C:\Source\Birko.Models.Category\`

## Purpose
- Category entity models
- Hierarchical category support
- Category filters
- Category view models

## Components

### Models
- `Category` - Base category entity (implements `IHierarchical`, `ISluggable` — slug auto-generated from `Title`)
- `IRelatedToCategory` - Interface for entities related to a category

### ViewModels
- `CategoryViewModel` - Category display model
- `CategoryTreeViewModel` - Category tree model

### Filters
- `CategoryFilter` - Category search filter

## Category Entity

```csharp
using Birko.Models.Category.Models;

public class Category : Entity
{
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public string ImageUrl { get; set; }
    public bool IsActive { get; set; }
}
```

## Category Tree

```csharp
public class CategoryTree
{
    public Category Category { get; set; }
    public IList<CategoryTree> Children { get; set; }
    public int Depth { get; set; }
}
```

## Category Filter

```csharp
using Birko.Models.Category.Filters;

var filter = new CategoryFilter
{
    ParentId = parentId,
    IsActive = true,
    IncludeChildren = true
};
```

## Dependencies
- Birko.Models, Birko.Models.Contracts (IHierarchical), Birko.Data.Patterns (ISluggable)

## Use Cases
- E-commerce categories
- Content categorization
- Navigation menus
- Product organization

## Related Models
- [Birko.Models.Product](../Birko.Models.Product/CLAUDE.md) - Product models

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
