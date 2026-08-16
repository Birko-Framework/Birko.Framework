# Birko.Models.Inventory

Clean inventory domain models for the Birko Framework.

## Features

- Stock item management with variants and categories
- Storage location hierarchy (replaces "Repository" naming)
- Stock movement tracking (receipt, issue, transfer)
- Inventory documents with line items
- No SQL attributes — persistence-agnostic
- Implements Birko.Models.Contracts interfaces

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.Contracts\Birko.Models.Contracts.projitems" Label="Shared" />
<Import Project="..\Birko.Models.Inventory\Birko.Models.Inventory.projitems" Label="Shared" />
```

## Dependencies

- Birko.Data.Core (AbstractLogModel, ViewModels, ILoadable, ICopyable)
- Birko.Models.Contracts (ICatalogItem, ICategorizeable, IHierarchical, IDocument)

## API Reference

### Models

| Class | Contracts | Description |
|-------|-----------|-------------|
| **StockItem** | ICatalogItem, ICategorizeable | Inventory item with code, barcode, name |
| **StockItemVariant** | — | Size/color/config variant of an item |
| **StorageLocation** | IHierarchical | Warehouse location (shelf, bin, zone) |
| **StockMovement** | IDocumentLine, IBatchable | Stock in/out/transfer record (a *change*) |
| **StockBalance** | IBatchable | Quantity of an item at a location right now (the *state*), keyed item x variant x location x batch |
| **InventoryDocument** | IDocument | Document header (receipt/issue/transfer) |
| **InventoryDocumentLine** | IDocumentLine | Line item with quantity and pricing |

## Related Projects

- [Birko.Models.Contracts](../Birko.Models.Contracts/) — Domain contracts
- [Birko.Models.Pricing](../Birko.Models.Pricing/) — Pricing domain (extracted from warehouse)
- [Birko.Models.Warehouse](../Birko.Models.Warehouse/) — Legacy warehouse models (still supported)

## License

Part of the Birko Framework.
