# Birko.Models.Pricing

## Overview
Clean pricing domain models for the Birko Framework. Consolidates pricing from Warehouse and Accounting into a dedicated domain. No SQL attributes.

## Project Location
`C:\Source\Birko.Models.Pricing\`

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
