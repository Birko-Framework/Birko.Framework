# Birko.Models.Contracts

Domain contract interfaces for the Birko Framework.

## Features

- Cross-cutting behavioral interfaces for domain models
- Zero dependencies — no base class or persistence requirements
- Gradual adoption — existing models implement contracts additively

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.Contracts\Birko.Models.Contracts.projitems" Label="Shared" />
```

## Contracts

| Interface | Properties | Implemented By |
|-----------|-----------|----------------|
| **ICatalogItem** | Name, Code, BarCode, Description | Product, Item |
| **IPriceable** | Price, PriceVAT, VAT | ValueData |
| **IVariantable\<T\>** | Variants collection | — |
| **ICategorizeable** | CategoryGuid | Item |
| **IBatchable** | BatchNumber, ExpiryDate | — |
| **ILocatable** | LocationGuid | — |
| **IHierarchical** | ParentGuid, Path | AbstractTree |
| **IDocument\<TLine\>** | DocumentNumber, Status, Lines | — |
| **IDocumentLine** | Quantity, UnitPrice | — |
| **IContactable** | Phone, Email | Address, ContactPerson |
| **IAddressable** | Street, StreetNumber, City, ZIP, Country | Address |

## Usage

```csharp
using Birko.Models.Contracts;

// Query any catalog item regardless of concrete type
IEnumerable<ICatalogItem> items = GetAllCatalogItems();
var matches = items.Where(i => i.Code.StartsWith("SKU-"));

// Work with any priceable entity
void ApplyDiscount(IPriceable item, decimal percentage)
{
    if (item.Price.HasValue)
        item.Price = item.Price.Value * (1 - percentage / 100);
}
```

## Related Projects

- [Birko.Models](../Birko.Models/) — Base abstract models and value objects
- [Birko.Models.Product](../Birko.Models.Product/) — Product models (implements ICatalogItem)
- [Birko.Models.Warehouse](../Birko.Models.Warehouse/) — Warehouse models (implements ICatalogItem, ICategorizeable)
- [Birko.Models.Customers](../Birko.Models.Customers/) — Customer models (implements IAddressable, IContactable)

## License

Part of the Birko Framework.
