# Birko.Models

Base abstract models, ViewModels, and extensions for the Birko Framework.

## Features

- Abstract percentage model with decimal value
- Abstract tree model with hierarchical path (slash-separated GUIDs)
- ValueData model for price/VAT/PriceVAT with configurable decimal precision
- Generic SourceValue for key-value pairs with source tracking
- Parallel ViewModel implementations with INotifyPropertyChanged
- Extension methods for SourceValue collections

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models\Birko.Models.projitems" Label="Shared" />
```

## Dependencies

- Birko.Data.Core (AbstractLogModel, ViewModels, ILoadable, ICopyable)

## Usage

### AbstractPercentage

```csharp
using Birko.Models;

public class Discount : AbstractPercentage
{
    public string Name { get; set; }
}

var discount = new Discount { Percentage = 15.5m, Name = "Summer Sale" };
```

### AbstractTree

```csharp
using Birko.Models;

// Build a path from a hierarchy of GUIDs
var path = AbstractTree.BuildPath(new[] { parentGuid, childGuid });
// "/parentGuid/childGuid"
```

### ValueData

```csharp
using Birko.Models;

var price = new ValueData
{
    Price = 100.00m,
    VAT = 20.00m,
    PriceVAT = 120.00m
};
```

### SourceValue

```csharp
using Birko.Models;
using Birko.Extensions;

var values = new[]
{
    new SourceValue<string> { Source = "en", Value = "Hello" },
    new SourceValue<string> { Source = "sk", Value = "Ahoj" }
};

var english = values.GetValue("en"); // "Hello"
values = values.SetValue("de", "Hallo"); // Appends new entry
```

## API Reference

### Models (Birko.Models)

| Class | Base | Description |
|-------|------|-------------|
| **AbstractPercentage** | AbstractLogModel | Abstract model with `decimal Percentage` |
| **AbstractTree** | AbstractLogModel | Abstract model with `string Path` (slash-separated GUIDs) |
| **ValueData** | AbstractLogModel | Price data with `Price`, `PriceVAT`, `VAT` |
| **SourceValue\<T\>** | — | Generic value with `Source` string and `Value` of type T |

### Interfaces

| Interface | Description |
|-----------|-------------|
| **ITreePath** | `string Path { get; set; }` |
| **IValueData** | Price/PriceVAT/VAT properties, extends ILoadable and ICopyable |

### ViewModels (Birko.Models.ViewModels)

| Class | Base | Description |
|-------|------|-------------|
| **AbstractPercentage** | LogViewModel | ViewModel with `decimal Percentage` |
| **AbstractTree** | LogViewModel | ViewModel with `IEnumerable<Guid> Path` |
| **Value** | LogViewModel | ViewModel with `Price`, `PriceVAT`, `VAT` |
| **SourceValue\<T\>** | ViewModel | ViewModel with `Source` and `Value` |

### Extensions (Birko.Extensions)

| Method | Description |
|--------|-------------|
| `GetValue<T>(source)` | Find value by source string in SourceValue collection |
| `SetValue<T>(source, value)` | Update or append SourceValue in array |

## Related Projects

- [Birko.Models.Product](../Birko.Models.Product/) - Product models
- [Birko.Models.Category](../Birko.Models.Category/) - Category models
- [Birko.Models.SEO](../Birko.Models.SEO/) - SEO models
- [Birko.Models.Accounting](../Birko.Models.Accounting/) - Accounting models
- [Birko.Models.Customers](../Birko.Models.Customers/) - Customer models
- [Birko.Models.Users](../Birko.Models.Users/) - User models
- [Birko.Models.Warehouse](../Birko.Models.Warehouse/) - Warehouse models

## License

Part of the Birko Framework.
