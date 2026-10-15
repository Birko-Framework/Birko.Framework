# Validation Guide

## Overview

Birko.Validation provides a fluent validation framework for Birko.Data.Core models. It supports manual validation and automatic store-level validation via decorator wrappers.

## Core Interfaces

### IValidator<T>

```csharp
public interface IValidator<T>
{
    ValidationResult Validate(T instance);
    Task<ValidationResult> ValidateAsync(T instance, CancellationToken ct = default);
}
```

### ValidationResult

```csharp
public class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }
    public IDictionary<string, string[]> ToDictionary();  // Groups errors by property
}
```

## Defining Validators

Extend `AbstractValidator<T>` and use the fluent `RuleFor<TProp>()` API:

```csharp
using Birko.Validation;

public class DeviceValidator : AbstractValidator<Device>
{
    public DeviceValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(100);
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.SerialNumber).Required().Regex(@"^[A-Z]{2}\d{6}$");
        RuleFor(x => x.Temperature).Range(-40, 120);
    }
}
```

## Built-in Rules

| Rule | Description |
|------|-------------|
| `Required()` | Property must not be null or empty |
| `Email()` | Valid email format |
| `MaxLength(n)` | String max length |
| `MinLength(n)` | String min length |
| `Length(min, max)` | String length range |
| `Range(min, max)` | Range; bounds of another numeric type compare by value (`Range(-50, 150)` on a `double` works) |
| `GreaterThanOrEqual(v)` / `LessThanOrEqual(v)` / `NotEqual(v)` | Comparisons |
| `In(values)` / `In(values, message)` | One of the allowed values |
| `Matches(pattern)` / `Matches(regex)` | Regex match; a pattern runs with a 1 s match timeout (a timeout is invalid) and is not `Compiled` by default |
| `Must(predicate)` / `MustSatisfy(predicate)` | Custom logic on the value / the whole model |

Rules skip null values by default — use `Required()` explicitly to enforce non-null. `In` follows this too.

A nested `RuleFor(x => x.Address.City)` reads a null `Address` as a null `City` (no exception) and reports errors under
the whole path, `Address.City` (TASK-156).

## Manual Validation

```csharp
var validator = new DeviceValidator();
var result = validator.Validate(device);

if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.PropertyName}: {error.Message}");
    }

    // For API responses (groups by property):
    var problemDetails = result.ToDictionary();
}
```

## Store Integration (Decorator Pattern)

Wrap any store with automatic validation on Create/Update:

```csharp
// Async store
var store = new AsyncElasticSearchStore<Device>();
var validator = new DeviceValidator();
var validatedStore = new AsyncValidatingStoreWrapper<IAsyncStore<Device>, Device>(store, validator);

await validatedStore.CreateAsync(device);  // Throws ValidationException if invalid
await validatedStore.UpdateAsync(device);  // Also validates

// Async bulk store
var bulkStore = new AsyncDataBaseBulkStore<PostgreSQLConnector, Device>();
var validatedBulk = new AsyncValidatingBulkStoreWrapper<IAsyncBulkStore<Device>, Device>(bulkStore, validator);
```

Validation wrappers throw `ValidationException` with the `ValidationResult` attached.

The bulk wrapper validates its filtered updates too (TASK-156). `UpdateAsync(filter, action)` reads the matches, applies
the action, validates, and writes them as one collection update. `UpdateAsync(filter, PropertyUpdate)` validates the
matches with the update applied, then runs the native filtered update, so an `Increment` stays atomic; a row changed
between the two is validated as it was read.

## Custom Rules

```csharp
RuleFor(x => x.StartDate)
    .Custom((date, context) =>
    {
        if (date < DateTime.UtcNow)
            context.AddError("StartDate must be in the future");
    });
```

## See Also

- [Birko.Validation](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Validation)
