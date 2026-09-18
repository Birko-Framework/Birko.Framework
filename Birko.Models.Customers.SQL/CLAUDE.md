# Birko.Models.Customers.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Customers` domain. Provides ready-to-use fluent SQL mappings for Address, InvoiceAddress, ContactPerson, and Customer — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`C:\Source\Birko.Models.Customers.SQL\`

## Components (`Birko.Models.Customers.SQL.Mappings`)
- **AddressMapping** → `Addresses` table
- **InvoiceAddressMapping** → `InvoiceAddresses` table
- **ContactPersonMapping** → `ContactPersons` table (Name, Position, Phone, Email)
- **CustomerMapping** → `Customers` table

`AddressMapping.cs` contains all three address-family mappings (Address, InvoiceAddress, ContactPerson) because the address shape is shared.

## File Structure
```
Mappings/
├── AddressMapping.cs       (Address + InvoiceAddress + ContactPerson)
└── CustomerMapping.cs
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>`
- **Birko.Models.Customers** — `Address`, `InvoiceAddress`, `ContactPerson`, `Customer`

## Usage

Import alongside `Birko.Models.SQL` and `Birko.Models.Customers`:

```xml
<Import Project="..\Birko.Models.Customers\Birko.Models.Customers.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"                      Label="Shared" />
<Import Project="..\Birko.Models.Customers.SQL\Birko.Models.Customers.SQL.projitems"  Label="Shared" />
```

```csharp
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
