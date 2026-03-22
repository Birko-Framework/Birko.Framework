# Birko.Models.Pricing

Clean pricing domain models for the Birko Framework.

## Features

- Currency with exchange rates
- Tax rate definitions
- Customer price groups with percentage modifiers
- Price lists with validity periods
- Per-item price list entries with quantity breaks
- Discount definitions (percentage or fixed amount)
- No SQL attributes — persistence-agnostic
- Implements Birko.Models.Contracts interfaces

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.Contracts\Birko.Models.Contracts.projitems" Label="Shared" />
<Import Project="..\Birko.Models.Pricing\Birko.Models.Pricing.projitems" Label="Shared" />
```

## Dependencies

- Birko.Data.Core (AbstractLogModel, ViewModels, ILoadable, ICopyable, IDefault)
- Birko.Models.Contracts (IPriceable)

## API Reference

### Models

| Class | Contracts | Description |
|-------|-----------|-------------|
| **Currency** | — | Currency with FromRate/ToRate exchange |
| **Tax** | — | Tax rate with name and shortcut |
| **PriceGroup** | — | Customer group with percentage modifier |
| **PriceList** | — | Named list with validity and currency |
| **PriceListEntry** | IPriceable | Item price in a list |
| **Discount** | — | Percentage or fixed discount |

## Related Projects

- [Birko.Models.Contracts](../Birko.Models.Contracts/) — Domain contracts
- [Birko.Models.Inventory](../Birko.Models.Inventory/) — Inventory domain
- [Birko.Models.Accounting](../Birko.Models.Accounting/) — Legacy accounting models (still supported)

## License

Part of the Birko Framework.
