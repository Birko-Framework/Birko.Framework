# Birko.Models.SQL

Fluent SQL mapping framework for Birko domain models. Framework only — canonical mappings live in the sibling projects.

## Features

- Fluent `ModelMap<T>` API replacing attribute-based mapping
- Expression-based property configuration (type-safe)
- `ModelMapRegistry` with assembly scanning for auto-discovery
- Supports: table name, column name, unique, primary, precision, scale, max length, index, ignore
- Bridges to `Birko.Data.SQL` via `ApplyToDatabase()` — registers table names + patches the column name, the primary/unique/required/auto-increment flags and the `HasIndex` / `HasUniqueIndex` indexes onto the SQL layer and `HasMaxLength` (TASK-691; refused on a type with no length). `HasPrecision`/`HasScale` on a `decimal` (TASK-692; refused on any other type, a string's length is `HasMaxLength`) — see CLAUDE.md

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems" Label="Shared" />
```

For pre-built canonical mappings of standard Birko domain models, also import one or more of:

```xml
<Import Project="..\Birko.Models.Users.SQL\Birko.Models.Users.SQL.projitems"       Label="Shared" />
<Import Project="..\Birko.Models.Customers.SQL\Birko.Models.Customers.SQL.projitems" Label="Shared" />
<Import Project="..\Birko.Models.Inventory.SQL\Birko.Models.Inventory.SQL.projitems" Label="Shared" />
<Import Project="..\Birko.Models.Pricing.SQL\Birko.Models.Pricing.SQL.projitems"     Label="Shared" />
<Import Project="..\Birko.Models.Product.SQL\Birko.Models.Product.SQL.projitems"     Label="Shared" />
```

Each sibling depends on its corresponding domain model project (e.g. `Birko.Models.Users.SQL` requires `Birko.Models.Users` to be imported too).

## Dependencies

- [`Birko.Data.Patterns`](../Birko.Data.Patterns/) — `FieldDescriptor`
- [`Birko.Data.SQL`](../Birko.Data.SQL/) — `DataBase.RegisterTableNames`, `DataBase.LoadTable`

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

        map.Property(x => x.Name).HasMaxLength(256);
        map.Property(x => x.Email).HasMaxLength(256);
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
registry.RegisterFromAssembly(typeof(Program).Assembly);

// Or register individually
registry.Register(new CustomerMapping());

// Apply to SQL layer (registers table names + patches field metadata)
registry.ApplyToDatabase();

// Retrieve mapping
var map = registry.GetMap<Customer>();
Console.WriteLine(map?.TableName); // "Customers"
```

### Attribute equivalent

| Old Attribute | New Fluent API |
|--------------|----------------|
| `[Table("X")]` | `map.ToTable("X")` |
| `[Table("X", Schema = "s")]` | `map.ToTable("X", "s")` (TASK-272) |
| `[UniqueField]` | `.IsUnique()` or `map.HasUnique(x => x.Prop)` |
| `[PrimaryField]` | `.IsPrimary()` or `map.HasPrimary(x => x.Prop)` |
| `[PrecisionField(N)]` on a decimal | `.HasPrecision(N)` |
| `[ScaleField(N)]` on a decimal | `.HasScale(N)` |
| `[PrecisionField(N)]` on a string (a length) | `.HasMaxLength(N)` — `HasPrecision` on a string is refused (TASK-692) |
| `[NamedField("X")]` | `.HasColumnName("X")` |
| `[RequiredField]` | `.IsRequired()` |
| `[IgnoreField]` | `map.Ignore(x => x.Prop)` |
| `[MaxLengthField(N)]` | `.HasMaxLength(N)` |
| `[IndexedField("idx", order)]` | `.HasIndex("idx", order)` |
| `[CompositeIndex]` unique across columns | `.HasUniqueIndex("ux", order)` on each column |
| a column in several indexes | one `.HasIndex` / `.HasUniqueIndex` call per index (TASK-695; one index twice is refused) |
| `[IncrementField]` | `.IsIncrement()` |

## Sibling projects

Pre-built canonical mappings — pick the domains you actually persist:

- [`Birko.Models.Users.SQL`](../Birko.Models.Users.SQL/) — User, UserLogin, UserProfile, UserRole, UserTenant, Role, RolePermission, Tenant
- [`Birko.Models.Customers.SQL`](../Birko.Models.Customers.SQL/) — Address, InvoiceAddress, ContactPerson, Customer
- [`Birko.Models.Inventory.SQL`](../Birko.Models.Inventory.SQL/) — StockItem, StorageLocation, InventoryDocumentLine
- [`Birko.Models.Pricing.SQL`](../Birko.Models.Pricing.SQL/) — Currency, Tax, PriceGroup
- [`Birko.Models.Product.SQL`](../Birko.Models.Product.SQL/) — MeasureUnit, UnitConversion, ProductPartnerCode

## License

Part of the Birko Framework.
