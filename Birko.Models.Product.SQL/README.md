# Birko.Models.Product.SQL

Canonical fluent SQL mappings for the `Birko.Models.Product` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

| Class | Table | Notable fields |
|---|---|---|
| `MeasureUnitMapping` | `MeasureUnits` | Code unique, Name, Symbol |
| `UnitConversionMapping` | `UnitConversions` | Factor (18,6) |
| `ProductPartnerCodeMapping` | `ProductPartnerCodes` | PartnerName, Code |

`MeasureUnitMapping.cs` bundles MeasureUnit + UnitConversion since both are unit-system primitives. `ProductPartnerCodeMapping.cs` is separate.

> **Note:** `MeasureUnitMapping` and `UnitConversionMapping` were previously bundled with Currency/Tax/PriceGroup in `Birko.Models.SQL/Mappings/CurrencyMapping.cs`. They moved here on 2026-05-24 because they belong to the Product domain.

## Installation

```xml
<Import Project="..\Birko.Models.Product\Birko.Models.Product.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                  Label="Shared" />
<Import Project="..\Birko.Models.Product.SQL\Birko.Models.Product.SQL.projitems"  Label="Shared" />
```

## Usage

```csharp
var registry = new Birko.Models.SQL.Mapping.ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Dependencies

- [`Birko.Models.SQL`](../Birko.Models.SQL/) — mapping framework
- [`Birko.Models.Product`](../Birko.Models.Product/) — domain models

## License

Part of the Birko Framework.
