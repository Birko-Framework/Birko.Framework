# Birko.Models.SQL

## Overview
Fluent SQL mapping framework for Birko domain models. Replaces attribute-based mapping (`[Table]`, `[UniqueField]`, `[PrecisionField]`, etc.) with a code-first fluent API.

**This project is framework-only as of 2026-05-24.** The canonical `IModelMapping<T>` implementations for `Birko.Models.Users` / `.Customers` / `.Inventory` / `.Pricing` / `.Product` moved into dedicated sibling projects (`Birko.Models.{Domain}.SQL`) so consumers can pick exactly the domains they persist.

## Project Location
`Birko.Models.SQL/`

## Components (`Birko.Models.SQL.Mapping`)
- **ModelMap\<T\>** — Fluent configuration: `ToTable()` (and `ToTable(name, schema)`, TASK-272), `HasUnique()`, `HasPrimary()`, `Ignore()`, `Property()`
- **FieldBuilder\<T\>** — Fluent field builder wrapping `FieldDescriptor` from Birko.Data.Patterns: `HasColumnName()`, `HasPrecision()`, `HasScale()`, `IsUnique()`, `IsPrimary()`, `IsAutoIncrement()`, `IsIgnored()`, `HasMaxLength()`, `HasIndex()`, `HasUniqueIndex()`, `And()`
- **IModelMapping\<T\>** — Implement to define SQL mappings for a model type
- **ModelMapRegistry** — Central registry with assembly scanning (`RegisterFromAssembly`), `GetMap<T>()`, `GetPropertyMaps()`, `ApplyToDatabase()`

## File Structure
```
Mapping/
├── IModelMapping.cs
├── ModelMap.cs
├── ModelMapRegistry.cs
└── FieldBuilder.cs
```

## Dependencies
- **Birko.Data.Patterns** — `FieldDescriptor` (shared type for both mapping and migrations)
- **Birko.Data.SQL** — `ApplyToDatabase()` registers table names and applies the column name, the primary/unique/required/auto-increment flags, and `HasIndex` / `HasUniqueIndex`, written into the table's index declarations so schema-ensure creates them (TASK-595: before that they were metadata nothing read, and every `Birko.Models.Users.SQL` table had zero indexes). `HasMaxLength` is applied too (TASK-691): registered with `DataBase.RegisterMaxLength` before the table loads, so a `string` becomes the bounded column `[MaxLengthField]` gives (winning over one) and a `byte[]` is bounded likewise; on any other type `ApplyToDatabase` refuses it (rule 55). A length above a provider's index-key cap is rendered as declared, and the database refuses the index, as with the attribute. `HasPrecision`/`HasScale` stay mapping metadata only (TASK-692): not applied, and a string's `HasPrecision` is **not** a length on this path, unlike `[PrecisionField]`. Both remain readable through `GetPropertyMaps()`. Mixing `HasIndex` and `HasUniqueIndex` under one index name is refused

## Sibling Projects (canonical mappings)
| Sibling | Contains |
|---|---|
| `Birko.Models.Users.SQL` | UserMapping, UserLoginMapping, UserProfileMapping, UserRoleMapping, UserTenantMapping, RoleMapping, RolePermissionMapping, TenantMapping |
| `Birko.Models.Customers.SQL` | AddressMapping (Address + InvoiceAddress + ContactPerson), CustomerMapping |
| `Birko.Models.Inventory.SQL` | StockItemMapping, StorageLocationMapping, InventoryDocumentLineMapping |
| `Birko.Models.Pricing.SQL` | CurrencyMapping (Currency + Tax + PriceGroup) |
| `Birko.Models.Product.SQL` | MeasureUnitMapping (MeasureUnit + UnitConversion), ProductPartnerCodeMapping |

Consumers import only the siblings they need; they all share the same `Birko.Models.SQL.Mapping` namespace via `IModelMapping<T>` but live in `Birko.Models.{Domain}.SQL.Mappings` namespaces.

## Usage

```csharp
// 1. Define a mapping (in your own assembly or use a sibling project's canonical one)
public class StockItemMapping : IModelMapping<StockItem>
{
    public void Configure(ModelMap<StockItem> map)
    {
        map.ToTable("Items")
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);
        map.Property(x => x.Code).HasMaxLength(256);
    }
}

// 2. Register and use
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);   // picks up every IModelMapping<T> in the consumer DLL
var map = registry.GetMap<StockItem>();

// 3. Apply to database (registers table names + field metadata)
registry.ApplyToDatabase();
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
