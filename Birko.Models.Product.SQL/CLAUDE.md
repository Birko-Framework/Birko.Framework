# Birko.Models.Product.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Product` domain. Provides ready-to-use fluent SQL mappings for MeasureUnit, UnitConversion, and ProductPartnerCode — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`C:\Source\Birko.Models.Product.SQL\`

## Components (`Birko.Models.Product.SQL.Mappings`)
- **MeasureUnitMapping** → `MeasureUnits` table (Code unique, Name, Symbol)
- **UnitConversionMapping** → `UnitConversions` table (Factor at 18,6 precision)
- **ProductPartnerCodeMapping** → `ProductPartnerCodes` table (PartnerName, Code)

`MeasureUnitMapping.cs` bundles MeasureUnit + UnitConversion since both are unit-system primitives.

## File Structure
```
Mappings/
├── MeasureUnitMapping.cs        (MeasureUnit + UnitConversion)
└── ProductPartnerCodeMapping.cs
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>`
- **Birko.Models.Product** — `MeasureUnit`, `UnitConversion`, `ProductPartnerCode`

## Notes
`MeasureUnitMapping` and `UnitConversionMapping` were originally bundled with Currency/Tax/PriceGroup in `Birko.Models.SQL/Mappings/CurrencyMapping.cs`. They moved here on 2026-05-24 because `MeasureUnit`/`UnitConversion` live in `Birko.Models.Product`, not `Birko.Models.Pricing`.

## Usage

```xml
<Import Project="..\Birko.Models.Product\Birko.Models.Product.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                  Label="Shared" />
<Import Project="..\Birko.Models.Product.SQL\Birko.Models.Product.SQL.projitems"  Label="Shared" />
```

```csharp
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
