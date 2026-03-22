# Birko.Models.Inventory

## Overview
Clean inventory domain models for the Birko Framework. Replaces `Birko.Models.Warehouse` for new consumers — no SQL attributes, uses contracts and value objects.

## Project Location
`C:\Source\Birko.Models.Inventory\`

## Components

### Models (`Birko.Models.Inventory`)
- **StockItem** — Inventory item (replaces Warehouse.Item). Implements `ICatalogItem`, `ICategorizeable`
- **StockItemVariant** — Item variant (replaces Warehouse.ItemVariant)
- **StorageLocation** — Physical location (replaces Warehouse.Repository). Implements `IHierarchical`
- **StockMovement** — Stock in/out/transfer. Implements `IDocumentLine`
- **InventoryDocument** — Document header (replaces WareHouseDocument). Implements `IDocument<InventoryDocumentLine>`
- **InventoryDocumentLine** — Document line (replaces WareHouseDocumentItem). Implements `IDocumentLine`
- **InventoryDocumentType** — Enum: Receipt, Issue, Transfer

### ViewModels (`Birko.Models.Inventory.ViewModels`)
Parallel ViewModels with INotifyPropertyChanged for all models.

## File Structure
```
Models/
├── StockItem.cs
├── StockItemVariant.cs
├── StorageLocation.cs
├── StockMovement.cs
├── InventoryDocument.cs
└── InventoryDocumentLine.cs
ViewModels/
├── StockItem.cs
├── StockItemVariant.cs
├── StorageLocation.cs
├── StockMovement.cs
├── InventoryDocument.cs
└── InventoryDocumentLine.cs
```

## Dependencies
- **Birko.Data.Core** — AbstractLogModel, LogViewModel, ILoadable, ICopyable
- **Birko.Models.Contracts** — ICatalogItem, ICategorizeable, IHierarchical, IDocument, IDocumentLine

## Migration from Birko.Models.Warehouse
| Old | New |
|-----|-----|
| `Warehouse.Item` | `Inventory.StockItem` |
| `Warehouse.ItemVariant` | `Inventory.StockItemVariant` |
| `Warehouse.Repository` | `Inventory.StorageLocation` |
| `Warehouse.WareHouseDocument` | `Inventory.InventoryDocument` |
| `Warehouse.WareHouseDocumentItem` | `Inventory.InventoryDocumentLine` |
| `Warehouse.AbstractItemRepository` | `Inventory.StockMovement` |
| `AgendaGuid` | `TenantGuid` |

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
