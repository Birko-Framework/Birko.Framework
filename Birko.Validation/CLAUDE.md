# Birko.Validation

## Overview
Fluent validation framework for Birko.Data models. Platform-agnostic — no separate platform projects needed.

## Structure
```
Birko.Validation/
├── Core/
│   ├── IValidator.cs              - IValidator<T>: Validate + ValidateAsync
│   ├── IValidationRule.cs         - IValidationRule: IsValid(value, context) → bool
│   ├── ValidationResult.cs        - IsValid, Errors, AddError, Merge, ToDictionary
│   ├── ValidationContext.cs       - Instance, InstanceType, PropertyName, Items
│   └── ValidationException.cs     - Thrown by store wrappers on validation failure
├── Rules/
│   ├── RequiredRule.cs            - Not null/empty/whitespace/empty-guid
│   ├── EmailRule.cs               - Email format (GeneratedRegex)
│   ├── RangeRule.cs               - IComparable min/max
│   ├── LengthRule.cs              - String min/max length
│   ├── RegexRule.cs               - Custom regex pattern
│   └── CustomRule.cs              - Func<> predicate + strongly-typed CustomRule<T>
├── Fluent/
│   ├── AbstractValidator.cs       - Base class with RuleFor<TProp>(expression)
│   ├── RuleBuilder.cs             - Chaining: Required/Email/MaxLength/Range/Must/In/NotEqual
│   └── PropertyRule.cs            - Holds rules per property, expression-based value extraction
└── Integration/
    ├── ValidatingStoreWrapper.cs          - Sync IStore<T> wrapper
    ├── AsyncValidatingStoreWrapper.cs     - Async IAsyncStore<T> wrapper
    └── AsyncValidatingBulkStoreWrapper.cs - Async IAsyncBulkStore<T> wrapper
```

## Dependencies
- **Birko.Data** — for IStore<T>/IAsyncStore<T>/IAsyncBulkStore<T>/IStoreWrapper<T> (store integration only)

## Usage

### Define a validator
```csharp
using Birko.Validation.Fluent;

public class DeviceValidator : AbstractValidator<Device>
{
    public DeviceValidator()
    {
        RuleFor(x => x.Name).Required().MaxLength(100);
        RuleFor(x => x.SerialNumber).Required().Matches(@"^[A-Z0-9-]+$");
        RuleFor(x => x.Temperature).Range(-50m, 150m);
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.Status).In("Active", "Inactive", "Maintenance");
        RuleFor(x => x.EndDate).MustSatisfy(d => d.EndDate > d.StartDate, "End must be after start");
    }
}
```

### Validate manually
```csharp
var validator = new DeviceValidator();
var result = validator.Validate(device);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.PropertyName}: {error.Message}");
}
```

### Auto-validate via store wrapper
```csharp
var store = new AsyncPostgreSQLStore<Device>();
var validated = new AsyncValidatingStoreWrapper<IAsyncStore<Device>, Device>(store, new DeviceValidator());
// Throws ValidationException on Create/Update if invalid
await validated.CreateAsync(device);
```

## Key Design Decisions
- Rules skip null values (use RequiredRule for null checks) — composable, no false positives
- ValidationResult.ToDictionary() groups errors by property — ready for API problem details
- Store wrappers follow same pattern as SoftDeleteStoreWrapper/AuditStoreWrapper (IStoreWrapper<T>)
- CustomRule<T> receives full model instance for cross-property validation
- AbstractValidator.ValidateAsync delegates to sync Validate (override for truly async rules)

## Conventions
- Namespace: `Birko.Validation` (core), `Birko.Validation.Rules`, `Birko.Validation.Fluent`, `Birko.Validation.Integration`
- All rules implement `IValidationRule` (non-generic, works with `object?` values)
- RuleBuilder methods return `this` for fluent chaining

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
