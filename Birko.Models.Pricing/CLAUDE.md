# Birko.Models.Pricing

## Overview
Clean pricing domain models for the Birko Framework. Consolidates pricing from Warehouse and Accounting into a dedicated domain. No SQL attributes.

## Project Location
`Birko.Models.Pricing/`

## Components

### Models (`Birko.Models.Pricing`)
- **Currency** — Currency with exchange rates (replaces Accounting.Currency). Clean property names: `FromRate`, `ToRate`
- **Tax** — Tax rate (replaces Accounting.Tax)
- **PriceGroup** — Customer price group with percentage (moved from Accounting)
- **PriceList** — Named price list with validity period and currency
- **PriceListEntry** — Price for an item in a list. Implements `IPriceable`
- **Discount** — Percentage or fixed-amount discount with validity period
- **DiscountType** — Enum: Percentage, FixedAmount

### ViewModels (`Birko.Models.Pricing.ViewModels`)
Parallel ViewModels with INotifyPropertyChanged for all models.

### Filters (`Birko.Models.Pricing.Filters`)
Filter DTOs ship **only** for the reference/lookup entities commonly filtered in UIs — `Currency`,
`PriceGroup`, `Tax`. The transactional entities (`Discount`, `PriceList`, `PriceListEntry`, `CurrencyRate`)
are queried through their stores / parent relations and deliberately have no filter DTO (CR-L313). Add one
here only when a concrete query surface needs it. Note also (CR-L312): `PriceList` VM mapping is header-only
— `PriceList.Entries` is not round-tripped through the view model (entries are managed via the
PriceListEntry store).

## File Structure
```
Models/
├── Currency.cs
├── Tax.cs
├── PriceGroup.cs
├── PriceList.cs
├── PriceListEntry.cs
└── Discount.cs
ViewModels/
├── Currency.cs
├── Tax.cs
├── PriceGroup.cs
├── PriceList.cs
├── PriceListEntry.cs
└── Discount.cs
```

## Dependencies
- **Birko.Data.Core** — AbstractLogModel, LogViewModel, ILoadable, ICopyable, IDefault
- **Birko.Models.Contracts** — IPriceable

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
