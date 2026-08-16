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
- **StockMovement** — Stock in/out/transfer (a *change* to stock). Implements `IDocumentLine`, `IBatchable`
- **StockBalance** — How much of an item is at a location right now (the *state*). Implements `IBatchable`
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
├── StockBalance.cs
├── InventoryDocument.cs
└── InventoryDocumentLine.cs
ViewModels/
├── StockItem.cs
├── StockItemVariant.cs
├── StorageLocation.cs
├── StockMovement.cs
├── StockBalance.cs
├── InventoryDocument.cs
└── InventoryDocumentLine.cs
```

## Dependencies
- **Birko.Data.Core** — AbstractLogModel, LogViewModel, ILoadable, ICopyable
- **Birko.Models.Contracts** — ICatalogItem, ICategorizeable, IHierarchical, IDocument, IDocumentLine, IBatchable

## Migration from Birko.Models.Warehouse
| Old | New |
|-----|-----|
| `Warehouse.Item` | `Inventory.StockItem` |
| `Warehouse.ItemVariant` | `Inventory.StockItemVariant` |
| `Warehouse.Repository` | `Inventory.StorageLocation` |
| `Warehouse.WareHouseDocument` | `Inventory.InventoryDocument` |
| `Warehouse.WareHouseDocumentItem` | `Inventory.InventoryDocumentLine` |
| `Warehouse.AbstractItemRepository` | `Inventory.StockMovement` **+ `Inventory.StockBalance`** — see below |
| `AgendaGuid` | `TenantGuid` |

### ⚠ The AbstractItemRepository row was not a one-to-one rename (TASK-444)

Recovered from `FisData.Stock.Core`'s git history — the retired project's own source is gone, but a
consumer's history still carries the file. `Warehouse.AbstractItemRepository` was an **abstract
coordinate base**: `ItemGuid`, `ItemVariantGuid`, `RepositoryGuid`, `AgendaGuid`, `Batch` — **no
quantity and no date at all**. Three concrete descendants supplied the payload:

| Retired type | Added | Was |
|---|---|---|
| `ItemRepository` | `Amount` | the **balance** |
| `ItemRepositoryMovement` | amount, selling/purchase prices, VAT, document, date | the **ledger** |
| `ItemRepositoryInventory` (abstract, ×5 periods) | start/add/remove/end amounts, date | period **snapshots** |

Mapping that base onto the concrete `StockMovement` collapsed four types into one and left the
domain unable to express a balance. Two consumers re-added it independently — FisData bolted
`StorageLocationGuid` + `Amount` onto `StockMovement`, shadowing its movement-shaped fields, and
Symbio wrote its own `StockItem` with `QuantityOnHand`. `StockBalance` closes that gap.

**Not** reinstated: the abstract coordinate base (one implementor does not justify it — `StockMovement`
has different coordinates, two locations rather than one) and the period snapshots (reporting, not a
domain model). The old name `ItemRepository` is unusable regardless — *Repository* means the
data-access pattern in this framework (`Birko.Data.Repositories`).

### Batch vocabulary

`StockMovement.Batch` and `InventoryDocumentLine.Batch` were renamed to **`BatchNumber`** in TASK-444
and both gained `ExpiryDate`, so all three batch-bearing models implement `IBatchable` and the
namespace uses one word for one concept. The old name came from `Warehouse.AbstractItemRepository.Batch`.
Breaking, and taken deliberately while nothing in the framework or in Symbio read either property.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
