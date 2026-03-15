# Birko.Models

## Overview
Base abstract models, ViewModels, and extensions for the Birko Framework. Provides reusable value-type abstractions that domain model projects (Product, Category, Accounting, etc.) build upon.

## Project Location
`C:\Source\Birko.Models\`

## Components

### Models (`Birko.Models`)
- **AbstractPercentage** — Abstract model with `decimal Percentage` property. Extends `AbstractLogModel`, implements `ILoadable<ViewModels.AbstractPercentage>`, `ICopyable<AbstractPercentage>`
- **AbstractTree** — Abstract hierarchical model with `string Path` property (slash-separated GUIDs). Extends `AbstractLogModel`, implements `ITreePath`, `ILoadable<ViewModels.AbstractTree>`. Static `BuildPath(IEnumerable<Guid>)` helper
- **ITreePath** — Interface: `string Path { get; set; }`
- **ValueData** — Price data model with `decimal? Price`, `PriceVAT`, `VAT`. Extends `AbstractLogModel`, implements `IValueData`, `ILoadable<ValueData>`. Constants: `StoreDecimalPlaces = 6`, `StoreDecimalPrecision = 22`
- **IValueData** — Interface combining `ILoadable<ViewModels.Value>`, `ILoadable<IValueData>`, `ICopyable<ValueData>` with Price/PriceVAT/VAT properties
- **SourceValue\<T\>** — Generic value with `string Source` and `T Value`. Implements `ILoadable<ViewModels.SourceValue<T>>`

### ViewModels (`Birko.Models.ViewModels`)
- **AbstractPercentage** — ViewModel for percentage. Extends `LogViewModel` with `decimal Percentage` and INotifyPropertyChanged
- **AbstractTree** — ViewModel for tree path. Extends `LogViewModel` with `IEnumerable<Guid> Path` and INotifyPropertyChanged
- **Value** — ViewModel for price data (in file `ViewModels/ValueData.cs`). Extends `LogViewModel` with `decimal? Price`, `PriceVAT`, `VAT`
- **SourceValue\<T\>** — Generic ViewModel. Extends `ViewModel` with `string Source`, `T Value`. Implements `ILoadable<Models.SourceValue<T>>` and `ILoadable<SourceValue<T>>`

### Extensions (`Birko.Extensions`)
- **SourceValueExtensions** — Static extension methods:
  - `GetValue<T>(this IEnumerable<SourceValue<T>>, string source)` — Find value by source string
  - `SetValue<T>(this SourceValue<T>[], string source, T value)` — Update or append value in array

## File Structure
```
Models/
├── AbstractPercentage.cs
├── AbstractTree.cs
├── SourceValue.cs
└── ValueData.cs
ViewModels/
├── AbstractPercentage.cs
├── AbstractTree.cs
├── SourceValue.cs
└── ValueData.cs
Extensions/
└── SourceValueExtensions.cs
```

## Dependencies
- **Birko.Data.Core** — AbstractLogModel, ViewModel, ModelViewModel, LogViewModel, ILoadable, ICopyable

## Patterns
- **Dual Model/ViewModel:** Each abstract type has parallel implementations in Models/ and ViewModels/
- **INotifyPropertyChanged:** All ViewModels use property constants and raise change notifications
- **ILoadable:** Models implement bidirectional loading with their ViewModel counterparts
- **ICopyable:** Models support deep cloning via `CopyTo()` methods
- **Virtual properties and methods:** Allow derived classes to override behavior

## Specialized Model Projects
- [Birko.Models.Product](../Birko.Models.Product/CLAUDE.md)
- [Birko.Models.Category](../Birko.Models.Category/CLAUDE.md)
- [Birko.Models.SEO](../Birko.Models.SEO/CLAUDE.md)
- [Birko.Models.Accounting](../Birko.Models.Accounting/CLAUDE.md)
- [Birko.Models.Customers](../Birko.Models.Customers/CLAUDE.md)
- [Birko.Models.Users](../Birko.Models.Users/CLAUDE.md)
- [Birko.Models.Warehouse](../Birko.Models.Warehouse/CLAUDE.md)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
