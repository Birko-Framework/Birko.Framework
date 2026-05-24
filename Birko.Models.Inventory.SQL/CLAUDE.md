# Birko.Models.Inventory.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Inventory` domain. Provides ready-to-use fluent SQL mappings for StockItem, StorageLocation, and InventoryDocumentLine — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`C:\Source\Birko.Models.Inventory.SQL\`

## Components (`Birko.Models.Inventory.SQL.Mappings`)
- **StockItemMapping** → `Items` table (Code, BarCode, Name, ShortName, Type)
- **StorageLocationMapping** → `Repositories` table (Title, SortOrder)
- **InventoryDocumentLineMapping** → `WareHouseDocumentItems` table (Quantity / UnitPrice / UnitPriceVAT / VAT / TotalPrice / TotalPriceVAT — all 22,6 decimals)

Note: legacy table names — `Items`, `Repositories`, `WareHouseDocumentItems` — are preserved for compatibility with existing schemas.

## File Structure
```
Mappings/
├── StockItemMapping.cs
├── StorageLocationMapping.cs
└── InventoryDocumentLineMapping.cs
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>`
- **Birko.Models.Inventory** — `StockItem`, `StorageLocation`, `InventoryDocumentLine`

## Usage

```xml
<Import Project="..\Birko.Models.Inventory\Birko.Models.Inventory.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                      Label="Shared" />
<Import Project="..\Birko.Models.Inventory.SQL\Birko.Models.Inventory.SQL.projitems"  Label="Shared" />
```

```csharp
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
