# Birko.Models.Inventory.SQL

Canonical fluent SQL mappings for the `Birko.Models.Inventory` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

| Class | Table | Notable fields |
|---|---|---|
| `StockItemMapping` | `Items` | Code, BarCode, Name, ShortName, Type |
| `StorageLocationMapping` | `Repositories` | Title, SortOrder |
| `InventoryDocumentLineMapping` | `WareHouseDocumentItems` | Quantity, UnitPrice, UnitPriceVAT, VAT, TotalPrice, TotalPriceVAT — all 22,6 decimals |

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
