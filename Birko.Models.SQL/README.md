# Birko.Models.SQL

Fluent SQL mapping framework for Birko domain models.

## Features

- Fluent `ModelMap<T>` API replacing attribute-based mapping
- Expression-based property configuration (type-safe)
- `ModelMapRegistry` with assembly scanning for auto-discovery
- Supports: table name, column name, unique, primary, precision, scale, max length, index, ignore

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems" Label="Shared" />
```

## Dependencies

- Birko.Models.Inventory (for example mappings only)

## Usage

### Define a mapping

```csharp
using Birko.Models.SQL.Mapping;

public class CustomerMapping : IModelMapping<Customer>
{
    public void Configure(ModelMap<Customer> map)
    {
        map.ToTable("Customers")
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);

        map.Property(x => x.Name).HasPrecision(256).IsRequired();
        map.Property(x => x.Email).HasPrecision(256);
        map.Property(x => x.Balance)
            .HasPrecision(22)
            .HasScale(6);
    }
}
```

### Register mappings

```csharp
var registry = new ModelMapRegistry();

// Register from assembly (scans for IModelMapping<T> implementations)
registry.RegisterFromAssembly(typeof(CustomerMapping).Assembly);

// Or register individually
registry.Register(new CustomerMapping());

// Retrieve mapping
var map = registry.GetMap<Customer>();
Console.WriteLine(map?.TableName); // "Customers"
```

### Attribute equivalent

| Old Attribute | New Fluent API |
|--------------|----------------|
| `[Table("X")]` | `map.ToTable("X")` |
| `[UniqueField]` | `.IsUnique()` or `map.HasUnique(x => x.Prop)` |
| `[PrimaryField]` | `.IsPrimary()` or `map.HasPrimary(x => x.Prop)` |
| `[PrecisionField(N)]` | `.HasPrecision(N)` |
| `[ScaleField(N)]` | `.HasScale(N)` |
| `[NamedField("X")]` | `.HasColumnName("X")` |
| `[RequiredField]` | `.IsRequired()` |
| `[IgnoreField]` | `map.Ignore(x => x.Prop)` |
| `[MaxLengthField(N)]` | `.HasMaxLength(N)` |
| `[IndexedField("idx", order)]` | `.HasIndex("idx", order)` |
| `[IncrementField]` | `.IsIncrement()` |

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) — SQL base classes (attribute-based, legacy)
- [Birko.Models.Inventory](../Birko.Models.Inventory/) — Clean inventory models
- [Birko.Models.Pricing](../Birko.Models.Pricing/) — Clean pricing models

## License

Part of the Birko Framework.
