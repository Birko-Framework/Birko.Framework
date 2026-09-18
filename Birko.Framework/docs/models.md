# Domain Models Guide

## Overview

The `Birko.Models.*` projects provide a domain model layer split into three layers:

1. **Contracts** (`Birko.Models.Contracts`) — zero-dependency domain interfaces (`ICatalogItem`, `IPriceable`, `IHierarchical`, ...). Any model anywhere can implement these.
2. **Base + value objects** (`Birko.Models`) — abstract bases (`AbstractPercentage`, `AbstractTree`, `ValueData`) and immutable value objects (`Money`, `MoneyWithTax`, `Percentage`, `PostalAddress`, `Quantity`).
3. **Domain models** — 8 concrete projects (`Customers`, `Users`, `Inventory`, `Pricing`, `Product`, `Category`, `SEO`, `SQL`) that implement the contracts additively.

The architecture keeps concrete models free of SQL mapping attributes; schema mapping lives in `Birko.Models.SQL` as a fluent registry.

## Layer 1: Contracts

`Birko.Models.Contracts` has zero external dependencies. Any model can implement one or more of these interfaces to opt in to cross-cutting behavior.

| Interface | Purpose | Key members |
|---|---|---|
| `ICatalogItem` | Catalog-able item (products, stock items) | `Name`, `Code`, `BarCode`, `Description` |
| `IPriceable` | Model with pricing | `Price?`, `PriceVAT?`, `VAT?` |
| `IVariantable<TVariant>` | Supports variants (size, color) | `ICollection<TVariant> Variants` |
| `ICategorizeable` | Belongs to a category | `Guid? CategoryGuid` |
| `IBatchable` | Batch-tracked (pharma, food) | `BatchNumber`, `ExpiryDate?` |
| `ILocatable` | Physical storage location | `Guid? LocationGuid` |
| `IHierarchical` | Parent-child hierarchy | `ParentGuid?`, `Path`, `Depth` + `HierarchyHelper` |
| `INamedHierarchical` | Hierarchy with breadcrumb | adds `HierarchyName`, `NamePath` |
| `ISortable` | Manual ordering | `SortOrder` |
| `IDocument<TLine>` | Document header (invoice, receipt) | `DocumentNumber`, `Status`, `Lines` |
| `IDocumentLine` | Document line | `Quantity`, `UnitPrice?` |
| `IContactable` | Contact info | `Phone`, `Email` |
| `IAddressable` | Postal address | `Street`, `StreetNumber`, `City`, `ZIP`, `Country` |

`HierarchyHelper` (ships with `IHierarchical`) provides `ComputePath(parentPath, guid)` and `RewriteDescendantPaths(oldPath, newPath, descendants)` for materialized-path maintenance.

## Layer 2: Base + Value Objects

### Abstract base models (`Birko.Models`)

| Class | Extends | Purpose |
|---|---|---|
| `AbstractPercentage` | `AbstractLogModel` | Models storing a decimal percentage (price groups, discounts) |
| `AbstractTree` | `AbstractLogModel`, `IHierarchical` | Materialized-path tree base; `BuildPath(IEnumerable<Guid>)` helper |
| `ValueData` | `AbstractLogModel`, `IPriceable` | Pricing container (`Price`, `PriceVAT`, `VAT`) — precision `22,6` |
| `SourceValue<T>` | `AbstractLogModel` | Multi-sourced value wrapper (`Source`, `Value`) |

### Value objects

Immutable, arithmetic-capable, use them to keep primitives from leaking across domain boundaries:

```csharp
// Money — currency-safe arithmetic
var price = new Money(100m, "EUR");
var discounted = price.Subtract(new Money(15m, "EUR"));  // OK
var mixed     = price.Add(new Money(15m, "USD"));        // throws — currency mismatch

// MoneyWithTax — factories for net/gross
var withTax = MoneyWithTax.FromNetAndVat(100m, 20m);   // Price=100, VAT=20, PriceVAT=120
var fromGross = MoneyWithTax.FromGrossAndVat(120m, 20m); // Price=100, VAT=20, PriceVAT=120

// Percentage — apply or add
var vat  = new Percentage(20m);
vat.ApplyTo(100m);   // 20 (20% of 100)
vat.AddTo(100m);     // 120 (100 + 20%)

// Quantity — unit-safe
var q1 = new Quantity(5m, "kg");
var q2 = new Quantity(3m, "kg");
q1.Add(q2);   // 8 kg
q1.Add(new Quantity(3m, "g"));  // throws — unit mismatch

// PostalAddress
var addr = new PostalAddress("Main St", "42", "Bratislava", "811 01", "SK");
```

## Layer 3: Domain Models

### Customers (`Birko.Models.Customers`)

| Class | Implements | Purpose |
|---|---|---|
| `Address` | `IAddressable`, `IContactable` | Generic address (shipping/billing/registered) |
| `InvoiceAddress` | extends `Address` | Adds `BIN`, `TIN`, `VATIN`, `BankAccount` |
| `ContactPerson` | `IContactable` | Named contact with phone/email |
| `BaseCustomer` | — | Base with `Name`, `Code` |
| `Customer` | extends `BaseCustomer` | Adds `PriceGroupGuid?` |
| `CustomerAddress` | — | Join Customer ↔ Address |
| `CustomerBankAccount` | — | Bank account entry |

Supporting interfaces: `IRelatedToAddress`, `IRelatedToInvoiceAddress`, `IRelatedToCustomer`.

### Users (`Birko.Models.Users`)

Comprehensive auth + RBAC + profile + tenant layer.

| Class | Purpose |
|---|---|
| `User` | Core auth entity (`UserName`, `Email`, `IsActive`, `LastLoginAt?`) |
| `Tenant` | Application context/module (`Name`, `IsActive`) — formerly named `Agenda` |
| `UserTenant` | Join User ↔ Tenant (`IsOwner`, `JoinedAt`) |
| `UserLogin` | Multi-provider auth (`Provider`, `ProviderKey`, `PasswordHash`, `RefreshToken`) |
| `UserProfile` | 1:1 with User for GDPR separation (`FirstName`, `DisplayName`, `Locale`, `TimeZone`, `Bio`) |
| `Role` | Named permission group (`IsSystem` = immutable) |
| `RolePermission` | Role ↔ permission code (format: `{module}:{entity}:{action}`) |
| `UserRole` | Assign Role to User (`TenantGuid?` null = global) |

### Inventory (`Birko.Models.Inventory`)

Clean replacement for legacy `Birko.Models.Warehouse` — no SQL attributes, contract-based.

| Class | Implements | Purpose |
|---|---|---|
| `StockItem` | `ICatalogItem`, `ICategorizeable` | Catalog stock item |
| `StockItemVariant` | — | Variant (size, color) with SKU |
| `StorageLocation` | `IHierarchical` | Warehouse → aisle → shelf tree |
| `StockMovement` | `IDocumentLine` | Single stock movement |
| `InventoryDocument` | `IDocument<InventoryDocumentLine>` | Receipt / Issue / Transfer document |
| `InventoryDocumentLine` | `IDocumentLine` | Document line with location |

### Pricing (`Birko.Models.Pricing`)

| Class | Purpose |
|---|---|
| `Currency` | Currency with `FromRate`, `ToRate` |
| `CurrencyRate` | Historical exchange rate |
| `Tax` | Tax rate entity (`Code`, `Rate`) |
| `PriceGroup` | Customer price group (extends `AbstractPercentage`) |
| `PriceList` | Named price set with validity range |
| `PriceListEntry` | `IPriceable` — single item price in a list |
| `Discount` | Validity-scoped discount (Percentage / FixedAmount) |

### Product (`Birko.Models.Product`)

| Class | Implements | Purpose |
|---|---|---|
| `Product` | `ICatalogItem`, `ISluggable` | Product (slug auto-generated from `Name`) |
| `ProductPartnerCode` | — | Partner SKU mapping |
| `MeasureUnit` | — | Unit of measure (pcs, kg, m) |
| `UnitConversion` | — | Conversion rate between units |

Mixin interfaces: `IProductManufacturer`, `IProductProperties`, `IProductTags`.

### Category (`Birko.Models.Category`)

- `Category` — `IHierarchical`, `ISluggable` (slug from `Title`)
- `IRelatedToCategory` — mixin for entities that reference a category

### SEO (`Birko.Models.SEO`)

- `SEO` — page metadata (`Title`, `Description`, `Keywords`, `OGTitle`, `CanonicalUrl`, `NoIndex`, `StructuredData`)
- `URLAlias` — friendly-URL redirect (`IsPermanent` = 301 vs 302)
- `SitemapItem` — sitemap entry with `ChangeFrequency`, `Priority`

## Layer 4: SQL Mapping (`Birko.Models.SQL` + domain siblings)

Replaces attribute-based SQL mapping (`[Table]`, `[UniqueField]`, `[PrecisionField]`) with a fluent registry. Concrete models stay clean.

The framework is split into two layers so consumers only pull in what they need:

| Project | Contents |
|---|---|
| `Birko.Models.SQL` | Framework only — `ModelMap<T>`, `FieldBuilder<T>`, `IModelMapping<T>`, `ModelMapRegistry`. No canonical mappings. |
| `Birko.Models.Users.SQL` | Canonical mappings for `Birko.Models.Users`: UserMapping, UserLoginMapping, UserProfileMapping, UserRoleMapping, UserTenantMapping, RoleMapping, RolePermissionMapping, TenantMapping |
| `Birko.Models.Customers.SQL` | Canonical mappings for `Birko.Models.Customers`: AddressMapping (Address + InvoiceAddress + ContactPerson), CustomerMapping |
| `Birko.Models.Inventory.SQL` | Canonical mappings for `Birko.Models.Inventory`: StockItemMapping, StorageLocationMapping, InventoryDocumentLineMapping |
| `Birko.Models.Pricing.SQL` | Canonical mappings for `Birko.Models.Pricing`: CurrencyMapping (Currency + Tax + PriceGroup) |
| `Birko.Models.Product.SQL` | Canonical mappings for `Birko.Models.Product`: MeasureUnitMapping (MeasureUnit + UnitConversion), ProductPartnerCodeMapping |

Import only the SQL-mapping siblings whose models you actually persist. A consumer that uses `Birko.Models.Inventory` but doesn't touch users or pricing just imports `Birko.Models.SQL` + `Birko.Models.Inventory.SQL`.

> **Framework vs. consumer aggregator.** The repo's own `Birko.Framework.csproj` imports all five domain `.SQL` siblings — that project is the kitchen-sink build validator and intentionally compiles everything together. **Consumer aggregators (`{YourSolution}.Birko`) should not mirror that.** Treat each `Birko.Models.{Domain}.SQL` as opt-in per persisted domain: import the framework (`Birko.Models.SQL`) once, then add a domain sibling only if you actually persist that domain's models via SQL. Skipping unused siblings keeps your aggregator's binary footprint tight and avoids dragging `Birko.Data.SQL` mappings into NoSQL-only or read-only consumers.

### Defining a mapping

```csharp
using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace MyApp.Mappings;

public class StockItemMapping : IModelMapping<StockItem>
{
    public void Configure(ModelMap<StockItem> map)
    {
        map.ToTable("Items")
           .HasPrimary(x => x.Guid)
           .HasUnique(x => x.Code);

        map.Property(x => x.Code).HasMaxLength(64);
        map.Property(x => x.Name).HasMaxLength(256);
    }
}
```

### Registering and querying

```csharp
var registry = new ModelMapRegistry();

// Auto-discover all IModelMapping<> in the consumer assembly (picks up every domain sibling
// you imported via .projitems — they all compile into the same aggregator DLL)
registry.RegisterFromAssembly(typeof(Program).Assembly);

// Apply table names + field metadata to the SQL layer
registry.ApplyToDatabase();

var map = registry.GetMap<StockItem>();
```

### Fluent API

| Method | Effect |
|---|---|
| `ToTable(string)` | Target table name |
| `HasPrimary<TProp>(expr)` | Primary key |
| `HasUnique<TProp>(expr)` | Unique constraint |
| `Ignore<TProp>(expr)` | Exclude property |
| `Property<TProp>(expr)` | Enter property-level builder |
| `.HasColumnName(string)` | Column alias |
| `.HasPrecision(int)` / `.HasScale(int)` | Decimal precision/scale |
| `.HasMaxLength(int)` | String length |
| `.IsUnique()` / `.IsPrimary()` | Constraints |
| `.HasIndex()` | Index creation |

## Cross-cutting contract map

Which concrete model implements which domain contract:

| Contract | Implemented by |
|---|---|
| `ICatalogItem` | `Product`, `StockItem` |
| `ICategorizeable` | `Product`, `StockItem` |
| `IPriceable` | `ValueData`, `PriceListEntry`, `MoneyWithTax` (value object) |
| `IVariantable<T>` | `StockItem`, `Product` |
| `IHierarchical` | `Category`, `StorageLocation`, `AbstractTree` (base) |
| `IDocument<TLine>` | `InventoryDocument` |
| `IDocumentLine` | `InventoryDocumentLine`, `StockMovement` |
| `IAddressable` | `Address`, `InvoiceAddress` |
| `IContactable` | `Address`, `ContactPerson`, `UserProfile` |
| `ISluggable` | `Category` (from `Title`), `Product` (from `Name`) |
| `ILoadable<TViewModel>` | all models ↔ ViewModels (bidirectional) |
| `ICopyable<T>` | `AbstractPercentage`, `AbstractTree`, `ValueData`, `Address` |

## Architectural notes

- **Contracts are zero-dependency** — You can reference `Birko.Models.Contracts` from any project without pulling in SQL, storage, or validation dependencies.
- **Models are clean** — No `[Table]`, `[Column]`, `[PrecisionField]` attributes. Mapping lives in `Birko.Models.SQL`.
- **Dual Model/ViewModel** — Every model has a parallel ViewModel with `INotifyPropertyChanged`. `ILoadable<T>` is used to round-trip between them.
- **Materialized paths** — `IHierarchical` uses `"/guid/guid/guid"` paths so you can find descendants with a single `LIKE '/root/%'` query.
- **Immutable value objects** — `Money`, `MoneyWithTax`, `Percentage`, `Quantity`, `PostalAddress` are sealed and arithmetic-capable, so domain arithmetic stays type-safe.
- **Tenant-awareness opt-in** — Entities that need tenant isolation (e.g. `StockItem`, `InventoryDocument`) carry `TenantGuid`; others don't.
- **RBAC with permission codes** — `RolePermission.PermissionCode` follows `{module}:{entity}:{action}` convention (e.g. `inventory:stockitem:create`).

## See also

- [Data Patterns Guide](patterns.md) — `ISluggable`, `IAuditable`, `ITimestamped`, `ISoftDeletable`
- [Store Composition Guide](composition.md) — runtime wrapping of stores for models implementing patterns interfaces
- [Tagging Guide](tagging.md) — `ITaggable` mixin for attaching tags to any model
- [Validation Guide](validation.md) — fluent validation rules over these models
- [Views Guide](views.md) — cross-platform projections and aggregations
