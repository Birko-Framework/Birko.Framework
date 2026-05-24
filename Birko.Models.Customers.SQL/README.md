# Birko.Models.Customers.SQL

Canonical fluent SQL mappings for the `Birko.Models.Customers` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

| Class | Table | Notable fields |
|---|---|---|
| `AddressMapping` | `Addresses` | — |
| `InvoiceAddressMapping` | `InvoiceAddresses` | — |
| `ContactPersonMapping` | `ContactPersons` | Name, Position, Phone, Email |
| `CustomerMapping` | `Customers` | — |

The address-family mappings (Address, InvoiceAddress, ContactPerson) live in a single `AddressMapping.cs` file because they share a similar shape.

## Installation

```xml
<Import Project="..\Birko.Models.Customers\Birko.Models.Customers.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                      Label="Shared" />
<Import Project="..\Birko.Models.Customers.SQL\Birko.Models.Customers.SQL.projitems"  Label="Shared" />
```

## Usage

```csharp
var registry = new Birko.Models.SQL.Mapping.ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Dependencies

- [`Birko.Models.SQL`](../Birko.Models.SQL/) — mapping framework
- [`Birko.Models.Customers`](../Birko.Models.Customers/) — domain models

## License

Part of the Birko Framework.
