# Birko.Models.Inventory.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Inventory` domain. Provides ready-to-use fluent SQL mappings for StockItem, StorageLocation, and InventoryDocumentLine — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`C:\Source\Birko.Models.Inventory.SQL\`

## Components (`Birko.Models.Inventory.SQL.Mappings`)
- **StockItemMapping** → `Items` table (Code, BarCode, Name, ShortName, Type)
- **StorageLocationMapping** → `Repositories` table (Title, SortOrder)
- **StockBalanceMapping** → `StockBalances` table (Quantity 22,6; BatchNumber 256)
- **StockMovementMapping** → `StockMovements` table (Quantity + UnitPrice 22,6; BatchNumber 256)
- **InventoryDocumentLineMapping** → `WareHouseDocumentItems` table (Quantity / UnitPrice / UnitPriceVAT / VAT / TotalPrice / TotalPriceVAT — all 22,6 decimals)

Note: legacy table names — `Items`, `Repositories`, `WareHouseDocumentItems` — are preserved for compatibility with existing schemas.

**`StockBalances` and `StockMovements` deliberately do NOT preserve theirs (TASK-444).** Retired tables
existed for both — `ItemRepositories` and `ItemRepositoryMovements` — but a column name defaults to the
property name, and every coordinate was renamed in the move to Inventory (`ItemGuid` →
`StockItemGuid`, `RepositoryGuid` → `StorageLocationGuid`, `AgendaGuid` → `TenantGuid`, `Batch` →
`BatchNumber`, `Amount` → `Quantity`); the movement was reshaped too, one location becoming
`From`/`To`. Reusing either name would promise a drop-in compatibility the columns break. The three
above keep theirs precisely because their columns *did* survive.

All five decimal columns use 22,6 — the framework's canonical `ValueData.StoreDecimalPrecision` /
`StoreDecimalPlaces`, which the retired models applied via `[PrecisionField]`/`[ScaleField]`. An
unmapped decimal takes the provider default (18,2 on several), silently truncating quantities.

## File Structure
```
Mappings/
├── StockBalanceMapping.cs
├── StockMovementMapping.cs
├── StockItemMapping.cs
├── StorageLocationMapping.cs
└── InventoryDocumentLineMapping.cs
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>`
- **Birko.Models.Inventory** — `StockItem`, `StorageLocation`, `InventoryDocumentLine`, `StockBalance`, `StockMovement`

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
