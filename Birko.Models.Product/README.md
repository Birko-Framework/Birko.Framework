# Birko.Models.Product

Product domain models for the Birko Framework. Provides reusable product catalog entities, view models, and query filters shared across all Birko-based applications (e-shop, warehouse, POS, etc.).

## Installation

```bash
dotnet add package Birko.Models.Product
```

## Dependencies

- Birko.Data (AbstractLogModel, AbstractDatabaseLogModel, ILoadable, IFilter)
- Birko.Data.SQL (field attributes: PrecisionField, Table)
- Birko.Models (SourceValue, AbstractDatabasePercentage)

## Models

### Product

Core product catalog entity. Represents a single sellable or trackable item.

| Property | Type | Description |
|----------|------|-------------|
| `Guid` | `Guid?` | Primary key (inherited from AbstractLogModel) |
| `CreatedAt` | `DateTime` | Record creation timestamp (inherited) |
| `UpdatedAt` | `DateTime?` | Last modification timestamp (inherited) |
| `SKUCode` | `string` | Stock Keeping Unit code — internal identifier used for inventory tracking and ordering |
| `BarCode` | `string` | Barcode value (EAN-13, UPC-A, etc.) for scanning at POS or warehouse |
| `Name` | `string` | Display name of the product shown to users and in catalogs |
| `Slug` | `string` | URL-friendly identifier for SEO and web routing (e.g. `blue-widget-500ml`) |
| `Description` | `string` | Full text description of the product, may contain HTML or markdown |
| `Category` | `string` | Hierarchical category path (e.g. `Electronics/Phones/Accessories`) |

#### Optional interfaces

Product supports mixin interfaces for optional features. Implementing classes can opt in by also implementing the interface:

- **IProductManufacturer** — adds a `Manufacturer` collection (deduplicated list of manufacturer names)
- **IProductProperties** — adds a `Properties` collection of key-value pairs grouped by source (e.g. `{"color": ["red","blue"], "size": ["M","L"]}`)
- **IProductTags** — adds a `Tags` collection of key-value pairs grouped by source, used for filtering and search facets

### ProductPartnerCode

External PLU (Price Look-Up) code assigned per partner/system. Enables mapping between the internal product and external systems (e.g. supplier catalog codes, marketplace SKUs, POS register PLUs).

| Property | Type | Description |
|----------|------|-------------|
| `Guid` | `Guid?` | Primary key (inherited) |
| `CreatedAt` | `DateTime` | Record creation timestamp (inherited) |
| `UpdatedAt` | `DateTime?` | Last modification timestamp (inherited) |
| `ProductGuid` | `Guid?` | Foreign key to the Product this code belongs to |
| `PartnerName` | `string` | Name of the external partner or system (e.g. `"Alza"`, `"Mall.cz"`, `"SupplierX"`) — max 256 chars |
| `Code` | `string` | The external code/PLU value in the partner's system — max 256 chars |

**Table:** `ProductPartnerCodes`

**Use cases:**
- Warehouse receiving: match incoming supplier delivery note codes to internal products
- Marketplace integration: map internal SKU to Amazon ASIN, Alza code, etc.
- POS systems: register-specific PLU codes that differ from internal SKU

## Filters

### Product\<T\>

Finds a single product by its GUID (primary key lookup).

```csharp
var filter = new Filters.Product<MyProduct>(productGuid);
```

### ProductBySlug\<T\>

Finds a single product by its URL slug. Used for web routing and SEO-friendly URLs.

```csharp
var filter = new Filters.ProductBySlug<MyProduct>("blue-widget-500ml");
```

| Property | Type | Description |
|----------|------|-------------|
| `Slug` | `string` | Exact slug value to match |

### ProductList\<T\>

Multi-criteria search filter for product listings. Supports text search, category filtering, and faceted filtering by tags and properties.

```csharp
var filter = new Filters.ProductList<MyProduct>(
    search: "widget",
    category: "Electronics",
    tags: new() { { "brand", new() { "Acme" } } }
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Search` | `string?` | Free-text search — matches against product Name |
| `Category` | `string?` | Category prefix filter — matches products whose Category starts with this value |
| `Parameters` | `Dictionary<string, List<string>>?` | Property facet filter — only products implementing IProductProperties whose properties match all specified key-value pairs |
| `Tags` | `Dictionary<string, List<string>>?` | Tag facet filter — only products implementing IProductTags whose tags match all specified key-value pairs |

### ProductPartnerCode

Simple property-based filter DTO for querying partner codes.

| Property | Type | Description |
|----------|------|-------------|
| `ProductGuid` | `Guid?` | Filter by product — find all partner codes for a specific product |
| `PartnerName` | `string?` | Filter by partner name — find codes assigned to a specific external system |
| `Code` | `string?` | Filter by code value — reverse-lookup from external code to product |

## ViewModels

All ViewModels implement `INotifyPropertyChanged` for UI data binding. Each property raises change notifications, and a composite "object changed" event aggregates all property changes.

### Product (ViewModel)

ViewModel counterpart of the Product model. Two-way `LoadFrom` supports loading from both the Model and another ViewModel instance.

### ProductPartnerCode (ViewModel)

ViewModel counterpart of ProductPartnerCode. Properties: `ProductGuid`, `PartnerName`, `Code`.

## License

Part of the Birko Framework.
