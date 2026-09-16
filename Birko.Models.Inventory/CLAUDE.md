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

Recovered from `FisData.Stock.Core` — **its current committed models, not its git history**. The
retired project's own source is gone from disk, and it is *not* in that consumer's history either:
measured at TASK-444's close, `git log --all -S "Birko.Models.Warehouse"` there returns nothing and no
`*Warehouse*` file was ever deleted. What survives is better than a deleted file — FisData **never
migrated off** this hierarchy, so `Models/AbstractItemRepository.cs` at its `HEAD` still declares the
shape, with all its descendants beside it. (The earlier "git history" wording was carried unchecked
through three retellings; corrected rather than dropped, because *evidence described as needing
excavation invites less checking than evidence sitting in a working file* is the reusable half.)

`Warehouse.AbstractItemRepository` was an **abstract coordinate base**: `ItemGuid`,
`ItemVariantGuid`, `RepositoryGuid`, `AgendaGuid`, `Batch` — **no quantity and no date at all**. Three
concrete descendants supplied the payload:

| Retired type | Added | Was |
|---|---|---|
| `ItemRepository` | `Amount` | the **balance** |
| `ItemRepositoryMovement` | amount, selling/purchase prices, VAT, document, date | the **ledger** |
| `ItemRepositoryInventory` (abstract, ×5 periods) | start/add/remove/end amounts, date | period **snapshots** |

Mapping that base onto the concrete `StockMovement` collapsed four types into one and left the
domain unable to express a balance. Both consumers carry the concept the framework lost — FisData
**never migrated off** the old hierarchy (0 files at its `HEAD` reference `Birko.Models.Inventory`; an
uncommitted working tree is part-way through, bolting `StorageLocationGuid` + `Amount` onto
`StockMovement` and shadowing its movement-shaped fields), and Symbio wrote its own `StockItem` with
`QuantityOnHand`. `StockBalance` closes that gap.

**Not** reinstated: the abstract coordinate base (one implementor does not justify it — `StockMovement`
has different coordinates, two locations rather than one) and the period snapshots (reporting, not a
domain model). The old name `ItemRepository` is unusable regardless — *Repository* means the
data-access pattern in this framework (`Birko.Data.Repositories`).

### Batch vocabulary

`StockMovement.Batch` and `InventoryDocumentLine.Batch` were renamed to **`BatchNumber`** in TASK-444
and both gained `ExpiryDate`, so all three batch-bearing models implement `IBatchable` and the
namespace uses one word for one concept. The old name came from `Warehouse.AbstractItemRepository.Batch`.
Breaking, and taken deliberately while nothing in the framework or in Symbio read either property.
Re-checked at TASK-444's close against the one other consumer that looked likely to: **no committed
consumer reads them either** — `FisData.Stock.Core` references `Birko.Models.Inventory` in 0 files at
`HEAD`. Its uncommitted migration does inherit `Batch` in three models, and whoever finishes that
migration meets `BatchNumber`; that is reconciliation against current models, not a break in shipped
code.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
