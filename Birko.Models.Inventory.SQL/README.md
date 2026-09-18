# Birko.Models.Inventory.SQL

Canonical fluent SQL mappings for the `Birko.Models.Inventory` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

The first three keep their Warehouse-era table names so an existing schema still works. The last two do
not: retired tables existed (`ItemRepositories`, `ItemRepositoryMovements`) but every coordinate column
was renamed, so reusing those names would promise a compatibility that does not hold — see CLAUDE.md.

| Class | Table | Notable fields |
|---|---|---|
| `StockItemMapping` | `Items` | Code, BarCode, Name, ShortName, Type |
| `StorageLocationMapping` | `Repositories` | Title, SortOrder |
| `InventoryDocumentLineMapping` | `WareHouseDocumentItems` | Quantity, UnitPrice, UnitPriceVAT, VAT, TotalPrice, TotalPriceVAT — all 22,6 decimals |
| `StockBalanceMapping` | `StockBalances` | Quantity (22,6), BatchNumber (256) |
| `StockMovementMapping` | `StockMovements` | Quantity, UnitPrice (22,6), BatchNumber (256) |

Legacy table names (`Items`, `Repositories`, `WareHouseDocumentItems`) are preserved for compatibility with existing schemas.

## Installation

```xml
<Import Project="..\Birko.Models.Inventory\Birko.Models.Inventory.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                      Label="Shared" />
<Import Project="..\Birko.Models.Inventory.SQL\Birko.Models.Inventory.SQL.projitems"  Label="Shared" />
```

## Usage

```csharp
var registry = new Birko.Models.SQL.Mapping.ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Dependencies

- [`Birko.Models.SQL`](../Birko.Models.SQL/) — mapping framework
- [`Birko.Models.Inventory`](../Birko.Models.Inventory/) — domain models

## License

Part of the Birko Framework.
