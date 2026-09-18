# Birko.Models.Pricing.SQL

Canonical fluent SQL mappings for the `Birko.Models.Pricing` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

| Class | Table | Notable fields |
|---|---|---|
| `CurrencyMapping` | `Currencies` | Code unique, Name, Symbol |
| `TaxMapping` | `Taxes` | Name, ShortCut, Percentage (22,6) |
| `PriceGroupMapping` | `PriceGroups` | Name, Percentage (22,6) |

All three live in a single `CurrencyMapping.cs` file for convenience.

> **Note:** Prior to the 2026-05-24 split, this file also contained `MeasureUnitMapping` and `UnitConversionMapping`. Those moved to [`Birko.Models.Product.SQL`](../Birko.Models.Product.SQL/) because they belong to the Product domain.

## Installation

```xml
<Import Project="..\Birko.Models.Pricing\Birko.Models.Pricing.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                  Label="Shared" />
<Import Project="..\Birko.Models.Pricing.SQL\Birko.Models.Pricing.SQL.projitems"  Label="Shared" />
```

## Usage

```csharp
var registry = new Birko.Models.SQL.Mapping.ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Dependencies

- [`Birko.Models.SQL`](../Birko.Models.SQL/) — mapping framework
- [`Birko.Models.Pricing`](../Birko.Models.Pricing/) — domain models

## License

Part of the Birko Framework.
