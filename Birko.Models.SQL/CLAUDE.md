# Birko.Models.SQL

## Overview
Fluent SQL mapping framework for Birko domain models. Replaces attribute-based mapping (`[Table]`, `[UniqueField]`, `[PrecisionField]`, etc.) with a code-first fluent API.

## Project Location
`C:\Source\Birko.Models.SQL\`

## Components

### Mapping Framework (`Birko.Models.SQL.Mapping`)
- **ModelMap\<T\>** — Fluent configuration: `ToTable()`, `HasUnique()`, `HasPrimary()`, `Ignore()`, `Property()`
- **PropertyMap** — Property metadata: ColumnName, IsUnique, IsPrimary, Precision, Scale, MaxLength, Index
- **PropertyMapBuilder\<T\>** — Fluent property builder: `HasColumnName()`, `HasPrecision()`, `HasScale()`, `IsUnique()`, `IsPrimary()`, `HasIndex()`
- **IModelMapping\<T\>** — Implement to define SQL mappings for a model type
- **ModelMapRegistry** — Central registry with assembly scanning (`RegisterFromAssembly`) and caching

### Example Mappings (`Birko.Models.SQL.Mappings`)
- **StockItemMapping** — Maps StockItem to "Items" table
- **StorageLocationMapping** — Maps StorageLocation to "Repositories" table
- **InventoryDocumentLineMapping** — Maps InventoryDocumentLine with decimal precision

## File Structure
```
Mapping/
├── IModelMapping.cs
├── ModelMap.cs
├── ModelMapRegistry.cs
├── PropertyMap.cs
└── PropertyMapBuilder.cs
Mappings/
├── StockItemMapping.cs
├── StorageLocationMapping.cs
└── InventoryDocumentLineMapping.cs
```

## Dependencies
- **Birko.Models.Inventory** — For example mappings

## Usage

```csharp
// Define a mapping
public class StockItemMapping : IModelMapping<StockItem>
{
    public void Configure(ModelMap<StockItem> map)
    {
        map.ToTable("Items")
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);
        map.Property(x => x.Code).HasPrecision(256);
    }
}

// Register and use
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(StockItemMapping).Assembly);
var map = registry.GetMap<StockItem>();
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
