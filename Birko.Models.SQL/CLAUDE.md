# Birko.Models.SQL

## Overview
Fluent SQL mapping framework for Birko domain models. Replaces attribute-based mapping (`[Table]`, `[UniqueField]`, `[PrecisionField]`, etc.) with a code-first fluent API.

## Project Location
`C:\Source\Birko.Models.SQL\`

## Components

### Mapping Framework (`Birko.Models.SQL.Mapping`)
- **ModelMap\<T\>** — Fluent configuration: `ToTable()`, `HasUnique()`, `HasPrimary()`, `Ignore()`, `Property()`
- **FieldBuilder\<T\>** — Fluent field builder wrapping `FieldDescriptor` from Birko.Data.Patterns: `HasColumnName()`, `HasPrecision()`, `HasScale()`, `IsUnique()`, `IsPrimary()`, `IsAutoIncrement()`, `IsIgnored()`, `HasMaxLength()`, `HasIndex()`, `And()`
- **IModelMapping\<T\>** — Implement to define SQL mappings for a model type
- **ModelMapRegistry** — Central registry with assembly scanning (`RegisterFromAssembly`), `GetMap<T>()`, `GetPropertyMaps()`, `ApplyToDatabase()`

### Example Mappings (`Birko.Models.SQL.Mappings`)
- **StockItemMapping** — Maps StockItem to "Items" table
- **StorageLocationMapping** — Maps StorageLocation to "Repositories" table
- **InventoryDocumentLineMapping** — Maps InventoryDocumentLine with decimal precision
- Plus 14 additional model mappings for Users, Roles, Customers, Currencies, etc.

## File Structure
```
Mapping/
├── IModelMapping.cs
├── ModelMap.cs
├── ModelMapRegistry.cs
└── FieldBuilder.cs
Mappings/
├── StockItemMapping.cs
├── StorageLocationMapping.cs
├── InventoryDocumentLineMapping.cs
├── CurrencyMapping.cs
├── UserMapping.cs
├── TenantMapping.cs
├── RoleMapping.cs
├── ... (14 more)
```

## Dependencies
- **Birko.Data.Patterns** — FieldDescriptor (shared type for both mapping and migrations)
- **Birko.Data.SQL** — ApplyToDatabase() registers table names and field metadata with the SQL layer
- **Birko.Models.Inventory** / **Birko.Models.Pricing** / **Birko.Models.Users** / **Birko.Models.Customers** — For example mappings

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

// Apply to database (registers table names + field metadata)
registry.ApplyToDatabase();
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
