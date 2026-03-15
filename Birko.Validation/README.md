# Birko.Validation

Fluent validation framework for the Birko Framework.

## Features

- IValidator\<T\> interface with fluent rule building
- Built-in rules: Required, Email, Range, Length, Regex, Custom
- AbstractValidator\<T\> base class
- Store wrappers for automatic validation on create/update

## Installation

```bash
dotnet add package Birko.Validation
```

## Dependencies

- Birko.Data.Core (AbstractModel, ILoadable)

## Usage

```csharp
using Birko.Validation;

public class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(c => c.Name).Required().Length(1, 100);
        RuleFor(c => c.Email).Required().Email();
        RuleFor(c => c.Age).Range(18, 120);
    }
}

var validator = new CustomerValidator();
var result = validator.Validate(customer);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine(error.Message);
}
```

## API Reference

- **IValidator\<T\>** - Validation interface
- **AbstractValidator\<T\>** - Base class with `RuleFor()` fluent API
- **ValidationResult** - Contains `IsValid` and `Errors`
- Built-in rules: `Required()`, `Email()`, `Range()`, `Length()`, `Regex()`, `Custom()`

## License

Part of the Birko Framework.
