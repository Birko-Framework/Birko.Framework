# Birko.Models.Contracts

## Overview
Domain contract interfaces for the Birko Framework. Defines cross-cutting behavioral contracts that model projects implement for gradual compatibility and clean domain boundaries.

## Project Location
`C:\Source\Birko.Models.Contracts\`

## Components

### Contracts (`Birko.Models.Contracts`)
- **ICatalogItem** — Name, Code, BarCode, Description. Implemented by: `Product`, `Warehouse.Item`
- **IPriceable** — Price, PriceVAT, VAT. Implemented by: `ValueData`
- **IVariantable\<TVariant\>** — Generic variants collection
- **ICategorizeable** — CategoryGuid. Implemented by: `Warehouse.Item`
- **IBatchable** — BatchNumber (`string?`), ExpiryDate (`DateTime?`). **Both optional**: batch
  tracking is a per-item choice and a batch may never expire. Implemented by
  `Inventory.StockBalance`, `Inventory.StockMovement`, `Inventory.InventoryDocumentLine`.
  `BatchNumber` was non-nullable until TASK-444, which is the likely reason the contract had zero
  implementors for its whole life — the entities it was written for could not satisfy it.
- **ILocatable** — LocationGuid
- **IHierarchical** — ParentGuid, Path. Implemented by: `AbstractTree`
- **IDocument\<TLine\>** — DocumentNumber, Status, Lines collection
- **IDocumentLine** — Quantity, UnitPrice
- **IContactable** — Phone, Email. Implemented by: `Address`, `ContactPerson`
- **IAddressable** — Street, StreetNumber, City, ZIP, Country. Implemented by: `Address`

## File Structure
```
Contracts/
├── IAddressable.cs
├── IBatchable.cs
├── ICatalogItem.cs
├── ICategorizeable.cs
├── IContactable.cs
├── IDocument.cs
├── IHierarchical.cs
├── ILocatable.cs
├── IPriceable.cs
└── IVariantable.cs
```

## Dependencies
None — zero-dependency contract project.

## Patterns
- All interfaces use `{ get; set; }` properties for compatibility with existing model patterns
- Contracts are independent of persistence (no SQL attributes, no base class requirements)
- Models implement contracts additively — existing APIs remain unchanged

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
