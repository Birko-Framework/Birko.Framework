# Birko.Models.Pricing.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Pricing` domain. Provides ready-to-use fluent SQL mappings for Currency, Tax, and PriceGroup — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`C:\Source\Birko.Models.Pricing.SQL\`

## Components (`Birko.Models.Pricing.SQL.Mappings`)
- **CurrencyMapping** → `Currencies` table (Code unique, Name, Symbol)
- **TaxMapping** → `Taxes` table (Name, ShortCut, Percentage at 22,6 precision)
- **PriceGroupMapping** → `PriceGroups` table (Name, Percentage at 22,6 precision)

All three are in a single `CurrencyMapping.cs` file (kept for convenience — small mappings).

## File Structure
```
Mappings/
└── CurrencyMapping.cs    (Currency + Tax + PriceGroup)
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>`
- **Birko.Models.Pricing** — `Currency`, `Tax`, `PriceGroup`

## Notes
Prior to the 2026-05-24 split, this file also contained `MeasureUnitMapping` and `UnitConversionMapping`. Those are now in [`Birko.Models.Product.SQL`](../Birko.Models.Product.SQL/) since `MeasureUnit`/`UnitConversion` live in `Birko.Models.Product`, not `Birko.Models.Pricing`.

## Usage

```xml
<Import Project="..\Birko.Models.Pricing\Birko.Models.Pricing.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                  Label="Shared" />
<Import Project="..\Birko.Models.Pricing.SQL\Birko.Models.Pricing.SQL.projitems"  Label="Shared" />
```

```csharp
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
