# Birko.Models.Customers

Customer management models for the Birko Framework.

## Features

- Address, InvoiceAddress, BaseCustomer, Customer models
- Customer-Address join table
- Related-to interfaces

## Installation

```bash
dotnet add package Birko.Models.Customers
```

## Dependencies

- Birko.Models

## API Reference

### Models (namespace: Birko.Models.Customers)

- **Address** - Full address with contact info
- **InvoiceAddress** - Billing address (BIN, TIN, VATIN, BankAccount)
- **BaseCustomer** / **Customer** - Customer with optional PriceGroup
- **CustomerAddress** - Customer-Address join table

### Interfaces

- **IRelatedToAddress** / **IRelatedToInvoiceAddress** / **IRelatedToCustomer**

## License

Part of the Birko Framework.
